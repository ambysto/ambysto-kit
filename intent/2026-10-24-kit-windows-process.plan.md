# Plan: Ambysto Kit for Windows 0.3 — process reading (APS-39)

Intent: [2026-10-24-kit-windows-process.md](2026-10-24-kit-windows-process.md) (approved 2026-10-24). Plan accepted by the owner 2026-10-24. Branch `aps-39-kit-windows-process`. Contract: the intent and `docs/spec/process.md` (with the Windows notes group 1 adds). Never the Swift code.

This plan is self-contained with the intent and the spec. HARD throughout: the main session writes the spec and the library; **a separate agent writes the acceptance tests from the spec, the intent and the public API stubs before the library exists** (group 3), so the tests are not shaped by the code; `hard-reviewer` reviews this plan, the acceptance tests and the code.

## Principles

- **Read only; never terminate, signal, suspend, spawn or modify.** `Ambysto.Kit.Process` opens processes only with `PROCESS_QUERY_LIMITED_INFORMATION` (and `PROCESS_VM_READ` only when reading the 64-bit PEB working directory of a verified readable 64-bit process), opens tokens only with `TOKEN_QUERY`, and never writes files or the registry. `scripts/windows-choke-points.sh` enforces this boundary.
- **Identity and liveness first.** A process is identified by `(ProcessId, StartTimeTicks)`. In `NtQuerySystemInformation(SystemProcessInformation)`, the kernel captures `UniqueProcessId`, `CreateTime` and the counters (`WorkingSetPrivateSize`, `UserTime`, `KernelTime`, `ReadTransferCount`, `WriteTransferCount`) atomically in a single entry. Any handle opened for metadata (`OwnerSid`, `TokenIsElevated`, `ExecutablePath`, `CommandLine`, `WorkingDirectory`) is verified via `GetProcessTimes` (`ExitTime == 0` and `CreationTime == snapshot.CreateTime`) before reading metadata and checked again (`ExitTime == 0` and unchanged start time) after reading metadata; a mismatch or exit skips the entry. Parent PIDs are validated in a second pass over the surviving entries of the snapshot (`parent.StartTimeTicks <= child.StartTimeTicks`).
- **Unreadable, never wrong.** Processes of other users, `SYSTEM`, services or elevated tokens (`TokenIsElevated != 0`) are unreadable (`IsReadable == false`), exposing only `(Identity, ParentProcessId, Name, SessionId)` from the snapshot while all other fields are `null`, and incrementing `UnreadableProcessCount`.
- **Facts, not guesses.** Every Windows struct layout and API behaviour relies on the saved probe outputs on the test machine (`probes/kit-process/p2`–`p8`) and is checked at run time by layout assertions.

## Design

**Names.** Package and assembly `Ambysto.Kit.Process`, namespace `Ambysto.Kit.Processes` (avoiding `CS0118` collision with `System.Diagnostics.Process`), `net10.0-windows`, P/Invoke only (`LibraryImport`, source-generated marshalling), no package reference.

**Public API** (everything else `internal`; `InternalsVisibleTo` `Ambysto.Kit.Process.Tests` only):

| Type | Shape |
|---|---|
| `ProcessIdentity` | `readonly record struct ProcessIdentity(int ProcessId, long StartTimeTicks)`: validates `ProcessId > 0` and `StartTimeTicks > 0` (`ArgumentOutOfRangeException`) |
| `ProcessCounters` | `sealed class`: `MemoryBytes` (`long`), `CpuTimeNanoseconds` (`long`), `BytesRead` (`long`), `BytesWritten` (`long`), `IdleWakeups` (`long?`, `null` on Windows), `InterruptWakeups` (`long?`, `null` on Windows), `EnergyNanojoules` (`long?`, `null` on Windows); public constructor validating non-negative values |
| `ProcessRecord` | `sealed class`: `Identity` (`ProcessIdentity`), `ParentProcessId` (`int?`), `Name` (`string`), `SessionId` (`int`), `IsReadable` (`bool`), `OwnerSid` (`string?`), `ExecutablePath` (`string?`), `WorkingDirectory` (`string?`), `CommandLine` (`string?`), `Arguments` (`IReadOnlyList<string>?`), `Usage` (`ProcessCounters?`); public factory methods `ProcessRecord.Readable(...)` and `ProcessRecord.Unreadable(ProcessIdentity identity, int? parentProcessId, string name, int sessionId)` |
| `ProcessSnapshot` | `sealed class`: `Processes` (`IReadOnlyList<ProcessRecord>`, all surviving readable and unreadable entries), `UnreadableProcessCount` (`int`); public constructor |
| `ProcessReader` | `sealed class`: `ProcessReader()`, `internal ProcessReader(IProcessSnapshotSource source)`; `ProcessSnapshot ReadSnapshot()`; `ProcessRecord? Read(ProcessIdentity identity)` (queries `source.ReadEntry(identity.ProcessId)`, applies the same Pass 1 liveness and readability rules requiring `IsReadable == true` and `SnapshotStartTimeTicks == identity.StartTimeTicks`, and validates `InheritedFromProcessId` via `source.ReadEntry(entry.InheritedFromProcessId)` with `parent.SnapshotStartTimeTicks <= identity.StartTimeTicks`); `string? ExecutablePath(ProcessIdentity identity)` |
| `SocketAddressFamily` | `enum`: `IPv4`, `IPv6` |
| `SocketAddressClass` | `enum`: `Any`, `Loopback`, `Specific` |
| `ListeningSocket` | `sealed class`: `ProcessId` (`int`), `ProcessIdentity` (`ProcessIdentity?`), `ProcessName` (`string?`), `IsOwnerReadable` (`bool`), `Port` (`ushort`), `Family` (`SocketAddressFamily`), `Address` (`string`), `AddressClass` (`SocketAddressClass`), `SocketId` (`ulong?`, `null` on Windows); public constructor |
| `ListeningSockets` | `sealed class`: `ListeningSockets()`, `internal ListeningSockets(ITcpTableSource source)`; `IReadOnlyList<ListeningSocket> ReadForReadableProcesses(ProcessSnapshot snapshot)`; `IReadOnlyList<ListeningSocket> ReadAll(ProcessSnapshot snapshot)` |
| `UsageDeltas` | `sealed class`: `IntervalNanoseconds` (`long`), `CpuTimeNanoseconds` (`long`), `BytesRead` (`long`), `BytesWritten` (`long`), `IdleWakeups` (`long?`), `InterruptWakeups` (`long?`), `EnergyNanojoules` (`long?`); public constructor |
| `SampledProcess` | `sealed class`: `Process` (`ProcessRecord`), `Deltas` (`UsageDeltas?`), `CpuPercent` (`double?`), `BytesReadPerSecond` (`double?`), `BytesWrittenPerSecond` (`double?`), `IdleWakeupsPerSecond` (`double?`), `InterruptWakeupsPerSecond` (`double?`), `IsZombie` (`bool`, `false` on Windows); public constructor |
| `ProcessSample` | `sealed class`: `Processes` (`IReadOnlyList<SampledProcess>`, readable processes only), `AllProcesses` (`IReadOnlyList<ProcessRecord>`, all surviving processes), `UnreadableProcessCount` (`int`), `TimestampNanoseconds` (`long`); public constructor |
| `ProcessSampler` | `sealed class`: `ProcessSampler()`, `ProcessSampler(ProcessReader reader)`, `internal ProcessSampler(ProcessReader reader, IMonotonicClock clock)`; `ProcessSample Sample()` |

**Internal seams** (`Seams.cs`, test-only, HARD like APS-24's seams):
- `IMonotonicClock`: `long NowNanoseconds()`
- `RawProcessHandleResult`: `sealed record RawProcessHandleResult(bool ExitedOrMissing, bool AccessDenied, long HandleStartTimeBeforeTicks, long HandleExitTimeBeforeTicks, string? OwnerSid, bool IsElevated, bool IsWow64, string? ExecutablePath, string? WorkingDirectory, string? CommandLine, long HandleStartTimeAfterTicks, long HandleExitTimeAfterTicks)` — `WorkingDirectory` and `CommandLine` are the **raw** PEB / `ProcessCommandLineInformation` strings; `ProcessReader` normalizes `WorkingDirectory` and splits `CommandLine` into `Arguments`.
- `RawProcessEntry`: `sealed record RawProcessEntry(int ProcessId, int InheritedFromProcessId, string Name, int SessionId, uint NumberOfThreads, long SnapshotStartTimeTicks, ProcessCounters Counters, RawProcessHandleResult HandleResult)` — if a synthetic test supplies multiple surviving entries with the same `ProcessId`, PID-keyed lookups (`ParentProcessId`, `ListeningSockets`) use the surviving entry with the greatest `SnapshotStartTimeTicks`.
- `IProcessSnapshotSource`:
  - `string CurrentUserSid { get; }`
  - `IReadOnlyList<RawProcessEntry> ReadEntries()`
  - `RawProcessEntry? ReadEntry(int processId)`
- `RawTcpListenerRow`: `readonly record struct RawTcpListenerRow(SocketAddressFamily Family, byte[] LocalAddressBytes, uint ScopeId, uint RawPortDword, int OwningProcessId)`
- `ITcpTableSource`: `IReadOnlyList<RawTcpListenerRow> ReadListeners()`

## Repository layout

```
docs/spec/process.md                        Windows notes (group 1)
windows/
  Ambysto.Kit.slnx                          gains Ambysto.Kit.Process and Ambysto.Kit.Process.Tests
  gates.json                                gains the process suite
  stryker-process.json                      Stryker.NET configuration for Ambysto.Kit.Process
  src/Ambysto.Kit.Process/                  the library (no PackageReference)
  tests/Ambysto.Kit.Process.Tests/          MSTest: acceptance tests (group 3) + unit tests
scripts/windows-choke-points.sh             checks both Safety and Process, with expanded patterns
scripts/windows-choke-points-fixtures.sh    fixtures for the expanded patterns
.github/workflows/windows-release.yml       packs and pushes License, Safety and Process from one windows-v* tag
```

## Groups, in order (one commit each)

1. **Spec** (HARD, own commit): `docs/spec/process.md` gains the Windows notes of intent Outcome 4 (snapshot via `NtQuerySystemInformation(SystemProcessInformation)`, atomic entry counters + pre/post handle checks, parent validation against surviving snapshot entries by `CreateTime <= child.CreateTime`, readable vs unreadable including elevated processes of the current user and exposing `(identity, parent, name, sessionId)` on unreadable processes, 64-bit PEB `cwd` normalized without trailing `\` and WOW64 `cwd` unreadable, `CommandLineToArgvW` arguments with empty/whitespace guard, open/mapped files absent on Windows, `ReadTransferCount`/`WriteTransferCount` all-I/O note, `QueryUnbiasedInterruptTime`, no `SocketId`). No Swift or C# type names outside the platform notes.
2. **Scaffold and API** (HARD, own commit): `windows/src/Ambysto.Kit.Process/Ambysto.Kit.Process.csproj`, `windows/tests/Ambysto.Kit.Process.Tests/Ambysto.Kit.Process.Tests.csproj`, `windows/Ambysto.Kit.slnx` (which `.github/workflows/windows-release.yml` already builds in its `Build everything` step), and the public types and internal seams of the Design table with `NotImplementedException` bodies (including constructors/factories so every test fails with `NotImplementedException` before group 4). Proof: `dotnet build -c Release` 0 warnings in the VM; `dotnet list src/Ambysto.Kit.Process package` shows none.
3. **Acceptance tests** (HARD, own commit): written by a **separate agent** that sees only the intent (Outcomes 1–4 and Success criteria 1–8), this plan's Principles and Design table, `docs/spec/process.md` after group 1, and the group 2 public API and seams — never any implementation or probe code. One or more tests per rule in Outcomes 1–3 and Criteria 1–7, covering both seam tests (deterministic edge cases: reused PID, pre/post start-time mismatch, mid-read exit, `NumberOfThreads == 0`, `PID 0`, parent created after child, parent skipped, elevated same-user token, other-user SID, `AccessDenied`, WOW64 `WorkingDirectory == null`, `CommandLine` `null`/`""`/`"   "`, `WorkingDirectory` trailing `\` and `\\?\` normalization, IPv4/IPv6/mapped-loopback/link-local `SocketAddressClass`, upper 16 bits of `RawPortDword`, `dwOwningPid == 0`, exited socket owner, first sample, zero/negative interval, each counter decreasing for all-or-nothing, fake wakeups/energy deltas and `0` energy staying `0`) and real-process integration tests in the VM (current process readable with SID/exe/cwd/cmdline/args/usage, `SYSTEM`/service processes unreadable and counted, spawned 32-bit `C:\Windows\SysWOW64\cmd.exe` readable with `WorkingDirectory == null`, spawned child parent PID and orphaned child `ParentProcessId == null`, exited process with open handle excluded and `Read(identity) == null`, bound TCPv4/TCPv6/link-local listeners and comparison with `netstat -ano -p tcp` / `tcpv6` across three runs, spawned CPU busy loop and file I/O helper sampled across two ticks). All tests compile and fail with `NotImplementedException` against the group 2 stubs. Reviewed by `hard-reviewer` before group 4. Afterwards the acceptance tests change only through a commit naming the spec line it follows.
4. **Snapshot, identity, parent validation and single-process re-check** (HARD, own commit): value types/records, `ProcessReader`, `SystemProcessSnapshotSource` (`NtQuerySystemInformation(SystemProcessInformation)`, `OpenProcess`, `GetProcessTimes` pre/post checks, `OpenProcessToken` for `TokenUser` and `TokenElevation`, `QueryFullProcessImageNameW`, `ProcessCommandLineInformation` + `CommandLineToArgvW`, `IsWow64Process2` + 64-bit PEB `WorkingDirectory` and path normalization, two-pass parent validation, `Read(ProcessIdentity)` and `ExecutablePath(ProcessIdentity)`), and struct layout checks.
5. **Listening sockets** (HARD, own commit): `ListeningSockets`, `SystemTcpTableSource` (`GetExtendedTcpTable` for `AF_INET` and `AF_INET6`), port and scope decoding, `SocketAddressClass` classification, `ReadForReadableProcesses` and `ReadAll`.
6. **Sampling** (HARD, own commit): `ProcessSampler`, `SystemMonotonicClock` (`QueryUnbiasedInterruptTime`), delta and rate calculation, all-or-nothing rule, wakeup/energy handling.
7. **Package, workflow, choke points, gates and docs** (HARD, own commit): `scripts/windows-choke-points.sh` and `scripts/windows-choke-points-fixtures.sh` extended to `Ambysto.Kit.Process`, `windows/gates.json` (`process` suite), `windows/stryker-process.json`, `.github/workflows/windows-release.yml` packing and pushing `Ambysto.Kit.Process` alongside `License` and `Safety`, `CLAUDE.md` and `README.md`.

The intent's `status` moves to `planned` when this plan is accepted, `building` with group 2's commit, `verified` after the proofs, Stryker.NET and the code review.

## Risks, and what catches them

| Risk | Caught by |
|---|---|
| Wrong `SYSTEM_PROCESS_INFORMATION`, `MIB_TCPROW_OWNER_PID` or `MIB_TCP6ROW_OWNER_PID` offsets | explicit struct layout assertions checked at run time against the verified SDK offsets (`probes/kit-process/p3.out`, `p4-sdk-header.out`, `p8-complete-facts.out`); integration tests comparing against live process properties and `netstat -ano` |
| `CommandLineToArgvW` called on `""` or whitespace returns the caller's own executable path | explicit `string.IsNullOrWhiteSpace` guard returning `Array.Empty<string>()`; seam tests for `""` and `"   "` |
| Upper 16 bits of `dwLocalPort` contain uninitialised bytes on Windows | port decoded strictly from the first two bytes (`(byte0 << 8) | byte1`); seam test with non-zero upper 16 bits in `RawPortDword` |
| An exited process whose handle is still held open by another process is treated as running | `GetProcessTimes` checks `ExitTime == 0` on every opened handle; integration test holding a `Process` object open after `WaitForExit()` |
| A parent entry that exited (`NumberOfThreads == 0` or skipped mid-read) or a reused parent PID (`StartTimeTicks > child.StartTimeTicks`) is reported as `ParentProcessId` | two-pass parent validation over surviving entries only; seam and orphan integration tests |
| Elevated process of the current user treated as readable | `TokenElevation` (`TokenIsElevated != 0`) check; seam test with same-user SID and `IsElevated = true` |
| A decreasing counter in one field leaves other deltas/rates populated | all-or-nothing rule tested per counter field in seam tests and by Stryker.NET |
| Acceptance tests mirror the code | written before the implementation by a separate agent; reviewed by `hard-reviewer` before group 4 |
| The library calls a process-control or write API | `scripts/windows-choke-points.sh` with seeded fail fixtures |

**Hand mutation checks** (break on purpose, see a test fail, restore; recorded in the review log), on top of Stryker.NET: (a) skip the `TokenIsElevated` check → the same-user elevated seam test fails; (b) accept a parent without comparing `StartTimeTicks` → the reused-parent-PID test fails; (c) decode `dwLocalPort` from all 32 bits instead of the first two bytes → the dirty-upper-bits port test fails; (d) call `CommandLineToArgvW` on `""` → the empty-command-line test fails; (e) drop the post-metadata `ExitTime` / `StartTimeAfterTicks` check → the mid-read exit/reuse test fails; (f) allow remaining deltas when one counter decreases → the all-or-nothing tests fail; (g) add `TerminateProcess` or `PROCESS_VM_WRITE` → the choke-point tripwire fails.

## Proof (what "done" means)

| Criterion | Proof |
|---|---|
| 1 | All acceptance and unit tests green in the VM as the standard user (the test machine's gates runner), counts in the review log; Stryker.NET report on `Ambysto.Kit.Process` with every survivor explained; the hand mutation checks above |
| 2–7 | Their acceptance and integration tests in `Ambysto.Kit.Process.Tests` |
| 8 | `scripts/windows-choke-points.sh` passes on both `Safety` and `Process` and fails on every seeded fixture |
| 9 | `dotnet build -c Release` 0 warnings; `dotnet list src/Ambysto.Kit.Process package` shows none; `docs/spec/process.md` names no Swift or C# types outside platform notes |
| 10 | `git diff main -- Sources Tests Package.swift windows/src/Ambysto.Kit.License windows/src/Ambysto.Kit.Safety` empty |
| 11 | Rehearsed in group 7 with a local feed; after the owner's `windows-v0.3.0` tag, from the local feed filled from GitHub Packages |
| Code review | `hard-reviewer` on the plan, on the acceptance tests (before group 4) and on groups 1–7: PASS |

## Owner steps

- Accept this plan.
- Push `windows-v0.3.0` on `main` after this PR merges.

## Review log

- **Plan review (hard-reviewer, PASS, 2026-10-24):** 0 BLOCKER, 0 MAJOR, 3 MINOR, 2 NIT; all folded into this revision:
  1. MINOR: state in `RawProcessHandleResult` that `WorkingDirectory` and `CommandLine` are the raw strings before `ProcessReader` normalizes/splits them.
  2. MINOR: specify how `ProcessReader.Read(ProcessIdentity)` validates `ParentProcessId` via `source.ReadEntry(entry.InheritedFromProcessId)`.
  3. MINOR: specify tie-breaking (greatest `SnapshotStartTimeTicks`) if a synthetic seam test returns duplicate surviving `ProcessId` entries.
  4–5. NIT: `Ambysto.Kit.slnx` already covers building in `.github/workflows/windows-release.yml` for group 2; include "Accept this plan" under Owner steps.
