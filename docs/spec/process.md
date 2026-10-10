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
| usage | physical memory footprint, CPU time, bytes read and written |

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

CPU % is per core (one fully busy core = 100), computed from the CPU time used between two samples of the **same identity**, divided by the time between the samples. The first sample of a process has no value. If a counter went down (a reused id, a reset), that sample has no value rather than a wrong one. Disk bytes per second the same way.

Windows note: process snapshot (`CreateToolhelp32Snapshot`) for the list; `GetProcessTimes` for CPU; `GetExtendedTcpTable` with owner process ids for listening sockets; identity = process id + creation time.

macOS note: libproc and sysctl (`proc_listallpids`, `PROC_PIDTBSDINFO`, `proc_pid_rusage` with ticks converted through the mach timebase, `KERN_PROCARGS2`, `PROC_PIDLISTFDS` with `PROC_PIDFDVNODEPATHINFO` and `PROC_PIDFDSOCKETINFO`, `PROC_PIDREGIONPATHINFO`); `ProcessReader`, `ProcessIdentity`, `ListeningSockets`, `ProcessSampler` in `AmbystoProcess`.
