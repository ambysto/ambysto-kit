# Ambysto.Kit.Safety

The file-safety gate for Ambysto apps on Windows, built from `docs/spec/safety.md` of
[ambysto/ambysto-kit](https://github.com/ambysto/ambysto-kit). It decides whether one item may be moved to the user's
Recycle Bin and never changes anything itself.

- `SafetyContext.ForCurrentUser(appFolders)` once per session; `new SafetyGate(context, rule)` with the app's
  `IAllowedRootRule`.
- `SafetyGate.Check(new GateRequest(path, stampShownAtScan))` immediately before each single move: an `ApprovedItem`
  (canonical path, stamp, measured size, facts) or a `RefusalReason` with the spec's message. Failing closed: a check
  that cannot establish its fact refuses with "Could not verify".
- `InUseChecker.Check(approved)`, `SafetyGate.CheckRestoreDestination(path)`, `FileFacts.Read(path)`.

The move itself, its verification in the Recycle Bin, the journal and the restore move are the app's (see the spec's
"Windows: recoverable moves" for the consumer's duties). Windows 11, x64, .NET 10.
