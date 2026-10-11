# Reading processes

Platform-neutral. The macOS implementation lives in this repository (see the macOS note).

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
| usage | physical memory footprint, CPU time, bytes read and written, **idle wakeups** (times the process woke the processor package from idle), **interrupt wakeups**, and **energy** (the system's own per-process energy estimate, in nanojoules; *unavailable* where the system does not provide it, which is reported as absent and never as 0) |

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
- The interval comes from a **monotonic clock that does not advance while the computer sleeps**. A caller that keeps wall-clock windows must not mix the two.
- A process that starts and exits between two samples, and the last stretch of a process before it exits, are never counted: per-process totals are an undercount, and callers present them as estimates.
- Sampled records also say whether a process is a zombie (see "status").

Windows note: process snapshot (`CreateToolhelp32Snapshot`) for the list; `GetProcessTimes` for CPU; `GetExtendedTcpTable` with owner process ids for listening sockets; identity = process id + creation time. There is no public per-process wakeup counter, and per-process energy exists only as the system resource usage monitor's estimates (`powercfg /srumutil`), not per interval: Windows reports wakeups and energy as unavailable, and callers fall back to CPU time.

macOS note: libproc and sysctl (`proc_listallpids`, `PROC_PIDTBSDINFO`, `proc_pid_rusage` with `RUSAGE_INFO_V6` — falling back to `RUSAGE_INFO_V4`, without energy, where the kernel refuses V6 — and ticks converted through the mach timebase, `KERN_PROCARGS2`, `PROC_PIDLISTFDS` with `PROC_PIDFDVNODEPATHINFO` and `PROC_PIDFDSOCKETINFO`, `PROC_PIDREGIONPATHINFO`); `ProcessReader`, `ProcessIdentity`, `ListeningSockets`, `ProcessSampler` in `AmbystoProcess`.

macOS note on wakeups and energy: idle wakeups are `ri_pkg_idle_wkups`, interrupt wakeups `ri_interrupt_wkups`, energy `ri_energy_nj` (V6). **Not `ri_billed_energy`:** measured on an M1 iMac (macOS 26.6, 2026-10-12), a one-second busy loop moved a process's own `ri_billed_energy` by 0 and its `ri_energy_nj` by about 2.8 × 10⁹; billed energy appears to count work other processes do on the process's behalf. The interval uses `DispatchTime` (uptime, sleep excluded). Across 13,242 sample pairs on that machine no counter went down. Intel: to be recorded by the energy spike.
