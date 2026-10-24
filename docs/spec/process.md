# Reading processes

Platform-neutral. The macOS and Windows implementations live in this repository (see the platform notes).

## A process record

| Field | Meaning |
|---|---|
| identity | process id + start time (see below) |
| parent, owner | parent process id; owner user id |
| name, executable path | short name; full path of the program file |
| working directory | the folder the process runs in |
| command line | its arguments |
| status | running, or exited but not yet collected ("zombie"); a zombie counts as exited |
| open files | each with its path as the system reports it and its file identity (device + inode) |
| memory-mapped files | the same, for files mapped into memory (libraries, databases); expensive to read, so callers take one snapshot and refresh it after 10 s |
| listening sockets | below |
| usage | physical memory footprint, CPU time, bytes read and written, **idle wakeups** (times the process woke the processor package from idle), **interrupt wakeups**, and **energy** (the system's own per-process energy estimate, in nanojoules; reported as *absent* only when the system cannot report it at all, never invented as 0; a system that keeps no energy counters may report 0 for every process, which callers treat like absent) |

Processes of the administrator or of other users are **unreadable**: every field reads "unreadable" instead of failing, and callers count them ("N system processes could not be checked").

## Identity

A process is identified by **(process id, start time)**: process ids are reused, this pair is not. It identifies the same *process*, not the same *program*: replacing the program inside a process (exec) keeps both values, so anything that acts on a process re-checks its executable path immediately before acting.

## Listening sockets

Every TCP socket in the LISTEN state of a readable process, one record per (process, socket descriptor):

| Field | Meaning |
|---|---|
| port | host byte order |
| family | IPv4 or IPv6 |
| address | printable local address |
| address class | **any** (wildcard), **loopback** (`127.0.0.0/8`, `::1`, or an IPv4-mapped loopback such as `::ffff:127.0.0.1`), **specific** (one interface address) |
| socket id | the system's handle of the socket: equal for processes that share one listening socket (a master and its workers) |

## Sampling

CPU % is per core (one fully busy core = 100), computed from the CPU time used between two samples of the **same identity**, divided by the time between the samples. Disk bytes per second and idle and interrupt wakeups per second the same way.

Each sampled process also carries its **deltas**: what changed since the previous sample of the same identity — CPU time in nanoseconds, idle wakeups, interrupt wakeups, energy, bytes read, bytes written — and the **interval** between the two samples in nanoseconds. Callers sum deltas over processes and over time windows; summing rates would lose the interval.

Rules:
- The first sample of a process has no deltas and no rates.
- **All or nothing:** if any counter present in both samples went down (a reused id, a reset), that process has no deltas and no rates in that sample, rather than wrong ones. Counters are cumulative for a live process, so this does not happen in practice; the rule guards against a reused identity.
- Energy: present in the deltas only when both samples have it; 0 stays 0.
- A zero interval (two samples at the same instant) gives no deltas and no rates.
- The interval comes from a **monotonic clock that does not advance while the computer sleeps**. A caller that keeps wall-clock windows must not mix the two.
- A process that starts and exits between two samples, and the last stretch of a process before it exits, are never counted: per-process totals are an undercount, and callers present them as estimates.
- Identity and counters are read so that they belong together: the start time is read, then the counters, then the start time again; if it changed (the id was reused in between), the process is skipped for that sample.
- Sampled records also say whether a process is a zombie (see "status").

Windows note:
- **Snapshot and counters:** `NtQuerySystemInformation(SystemProcessInformation)` reads the process table in one pass without opening handles. Each entry supplies the process id, inherited parent id, short image name, session id, thread count, creation time (`FILETIME`, 100-ns ticks since 1601-01-01 UTC; `identity` = process id + creation time), private working set in bytes (`WorkingSetPrivateSize`, physical memory footprint), user and kernel CPU times (100-ns ticks, converted to nanoseconds), and `ReadTransferCount` / `WriteTransferCount` (bytes read and written; on Windows these count all process I/O transfers — file, network and device/pipe I/O — not disk-only bytes). `PID 0` (`System Idle Process`) and any entry with `NumberOfThreads == 0` (already terminated) are excluded; a process that has exited while another process still holds a handle to it (`ExitTime != 0` from `GetProcessTimes`, or `ERROR_INVALID_PARAMETER` on open) counts as exited and is excluded, so Windows never reports zombies.
- **Liveness and handle binding:** because `CreateTime` and the counters sit in the same `SystemProcessInformation` entry, they are captured together. When a handle is opened (`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`) to read the remaining fields, `GetProcessTimes` is checked before and after reading those fields: if the process exited (`ExitTime != 0`) or its `CreationTime` differs from the snapshot entry's `CreateTime`, the process ended or its id was reused during the read and it is skipped.
- **Readable vs unreadable (owner decisions 2026-10-24):** a process is readable only when it can be opened, its token (`OpenProcessToken(TOKEN_QUERY)`) has the current user's SID (`TokenUser`), and its token is not elevated (`TokenElevation`, `TokenIsElevated == 0`). Elevated processes of the current user, and processes of `SYSTEM`, services or other users, are **unreadable** and counted in the unreadable count; because the kernel snapshot already provides their `identity` `(process id, creation time)`, validated `parent`, short `name` and `session id` without opening the process, an unreadable record carries those four fields while `owner`, `executable path`, `working directory`, `command line`, `arguments` and `usage` read as unreadable (`null`).
- **Parent:** Windows records `InheritedFromUniqueProcessId` at creation and never clears it when the parent exits, and the id may be reused later. A parent id is reported only when a surviving (non-skipped, live) entry with that id exists in the same snapshot (`parentId > 0`, `parentId != processId`) and its creation time is at or before the child's (`parent.creationTime <= child.creationTime`); otherwise `parent` is absent (`null`).
- **Executable path, command line and working directory:** for a readable process, the executable path comes from `QueryFullProcessImageNameW` (DOS path); the command line comes from `NtQueryInformationProcess(ProcessCommandLineInformation)`, with arguments split by `CommandLineToArgvW` (an approximate split of the raw command line; an empty or whitespace-only command line yields an empty argument list without calling `CommandLineToArgvW`, which would otherwise return the caller's own program path); the working directory is read for 64-bit processes (`IsWow64Process2` reports `processMachine == 0`) from the 64-bit PEB (`PROCESS_VM_READ`, `PebBaseAddress + 0x20` → `RTL_USER_PROCESS_PARAMETERS + 0x38`), normalized by stripping a leading `\\?\` and trimming a trailing `\` except on a drive root (`C:\`). For 32-bit (WOW64) processes (`processMachine != 0`), the working directory reads as unreadable (`null`) while the process and its other fields stay readable.
- **Open and memory-mapped files:** not provided per process on Windows (enumerating handles across the system can block on named pipes; file-centric in-use checking lives in the file-safety gate).
- **Listening sockets:** `GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_LISTENER` for IPv4 (`MIB_TCPROW_OWNER_PID`) and IPv6 (`MIB_TCP6ROW_OWNER_PID`, scope id at offset 16 in host byte order, port in the first two bytes of `dwLocalPort` at offset 20 in network byte order). Rows with owner id `0` are ignored. Windows provides no socket handle id in this table, so `socket id` is always absent (`null`). In addition to the readable processes' listening sockets, the Windows library exposes all listening sockets paired with the snapshot's process identity and short name (when the owner is in the snapshot) so callers can display another user's or `System`'s listener name without spawning `netstat`.
- **Sampling, clock, wakeups and energy:** the monotonic interval clock is `QueryUnbiasedInterruptTime` (100-ns units converted to nanoseconds; does not advance while the computer sleeps). There is no public per-process wakeup counter, and per-process energy exists only as the system resource usage monitor's estimates (`powercfg /srumutil`), not per interval: Windows reports idle wakeups, interrupt wakeups and energy as absent (`null`, never `0`), and callers fall back to CPU time. Implemented in `Ambysto.Kit.Process` (`namespace Ambysto.Kit.Processes`: `ProcessIdentity`, `ProcessCounters`, `ProcessRecord`, `ProcessSnapshot`, `ProcessReader`, `ListeningSocket`, `ListeningSockets`, `UsageDeltas`, `SampledProcess`, `ProcessSample`, `ProcessSampler`).

macOS note: libproc and sysctl (`proc_listallpids`, `PROC_PIDTBSDINFO`, `proc_pid_rusage` with `RUSAGE_INFO_V6` — falling back to `RUSAGE_INFO_V4`, without energy, where the kernel refuses V6 — and ticks converted through the mach timebase, `KERN_PROCARGS2`, `PROC_PIDLISTFDS` with `PROC_PIDFDVNODEPATHINFO` and `PROC_PIDFDSOCKETINFO`, `PROC_PIDREGIONPATHINFO`); `ProcessReader`, `ProcessIdentity`, `ListeningSockets`, `ProcessSampler` in `AmbystoProcess`.

macOS note on wakeups and energy: idle wakeups are `ri_pkg_idle_wkups`, interrupt wakeups `ri_interrupt_wkups`, energy `ri_energy_nj` (V6). **Not `ri_billed_energy`:** measured on an M1 iMac (macOS 26.6, 2026-10-12), a one-second busy loop moved a process's own `ri_billed_energy` by 0 and its `ri_energy_nj` by about 2.8 × 10⁹; billed energy appears to count work other processes do on the process's behalf. The interval uses `DispatchTime` (uptime, sleep excluded). In the plan review's run on that machine (13,242 sample pairs; a throw-away probe, not committed) no counter went down; `scripts/energy-spike.sh` counts decreases on every run; it passed three runs on an M3 (macOS 26.6.2, 2026-10-18) with the same result. Intel: to be recorded by the energy spike.
