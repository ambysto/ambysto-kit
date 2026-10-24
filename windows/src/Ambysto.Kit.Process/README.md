# Ambysto.Kit.Process

Process reading for Ambysto apps on Windows (`namespace Ambysto.Kit.Processes`), built from `docs/spec/process.md` of
[ambysto/ambysto-kit](https://github.com/ambysto/ambysto-kit). It reads processes, listening TCP sockets and per-interval
usage deltas, and never terminates, signals, suspends or spawns a process.

- `new ProcessReader()`: `ReadSnapshot()` returns `ProcessSnapshot` (`Processes`, `UnreadableProcessCount`); `Read(identity)`
  and `ExecutablePath(identity)` re-check a single process by `(ProcessId, StartTimeTicks)` immediately before acting.
- `new ListeningSockets()`: `ReadForReadableProcesses(snapshot)` and `ReadAll(snapshot)`.
- `new ProcessSampler()`: `Sample()` returns per-interval deltas (`UsageDeltas`) and rates (`CpuPercent`,
  `BytesReadPerSecond`, `BytesWrittenPerSecond`) over `QueryUnbiasedInterruptTime`, with the all-or-nothing rule when any
  counter decreases; wakeups and energy are `null` (absent on Windows).

Windows 11, x64, .NET 10.
