# Plan: Ambysto Kit for Windows 0.2 — the file-safety gate (APS-24)

Intent: [2026-10-16-kit-windows-safety.md](2026-10-16-kit-windows-safety.md) (approved 2026-10-16). Plan accepted by the owner 2026-10-16. Branch `aps-24-kit-windows-safety`. Contract: the intent and `docs/spec/safety.md` (with the Windows notes group 1 adds). Never the Swift code.

This plan is self-contained with the intent and the spec. HARD throughout: the main session writes the library; **a separate agent writes the acceptance tests from the spec and the intent before the library exists** (group 3), so the tests are not shaped by the code; hard-reviewer reviews this plan, the tests and the code.

## Principles

- **Fail closed.** Every check that cannot establish its fact (an API error, access denied, an unknown file system, an unexpected reparse tag, an unreadable registry value) refuses the item with "Could not verify: <check>". No check ever passes on an error.
- **The gate never reads data and never changes anything.** Handles are opened for attributes and security only; nothing in `Ambysto.Kit.Safety` creates, writes, moves, deletes or recycles a file, or writes the registry. A choke-point script enforces it (group 9).
- **Facts, not guesses.** Each Windows behaviour the code relies on is named with its saved probe output (test machine, `probes/recheck-2026-10-16/` and `probes/recycle/results/`), or is re-probed in the group that needs it before it is used.

## Design

**Names.** Package and assembly `Ambysto.Kit.Safety`, namespace `Ambysto.Kit.Safety`, `net10.0-windows`, P/Invoke only (`LibraryImport`, source-generated marshalling), no package reference.

**Public API** (everything else `internal`; `InternalsVisibleTo` the test assembly only):

| Type | Shape |
|---|---|
| `FileIdentity` | `readonly record struct`: `VolumeSerial` (`ulong`), `FileId` (`UInt128`) |
| `FileStamp` | `readonly record struct`: `Identity`, `LastWriteTimeUtcTicks` (`long`), `Length` (`long?`, null for folders) |
| `FileFacts` | `static FileFactsResult Read(string path)`: the canonical path and `FileStamp` of a file or folder, for an app's scan and for its post-move check of the item in the Recycle Bin. Like gate step 2 it reads attributes without a handle first and **never opens a `RECALL_ON_OPEN` item** (it returns "cloud, not opened", no stamp); otherwise it opens for attributes only. Input: drive-letter paths, including `\\?\` drive paths and mapped drives; UNC and device spellings are refused as input. A mapped drive returns its canonical `\\?\UNC\…` form and the identity the share reports. It decides nothing; the gate is the only decision |
| `RefusalReason` | `enum`, one value per refusal of intent Outcome 1 plus `CouldNotVerify`; extension `Message` (the spec's wording, added to `docs/spec/safety.md` in group 1) |
| `GateRequest` | the path the app showed and the `FileStamp` it recorded at scan time |
| `ApprovedItem` | canonical path, `FileStamp` now, measured allocated and logical size, the tree's file count, `ItemFacts` |
| `GateResult` | `ApprovedItem?` or (`RefusalReason`, detail), exactly one; `IsApproved` |
| `ItemFacts` | for an allowed-root rule: canonical path, profile folder, system volume serial, for the item and each folder above it the owner SID and `OwnedByCurrentUser` (conservative: `Administrators` is not the user), `IsSystemLocation` |
| `IAllowedRootRule` | `AllowedRootDecision Evaluate(ItemFacts)` and `AllowedRootDecision EvaluateRestore(ItemFacts destinationParent)`; a decision is allow, or refuse with the app's reason text |
| `SafetyContext` | resolved once per session: profile, known folders, never-touch entries (built-in + the app's additions; add-only), system locations, sync roots; `SafetyContext.ForCurrentUser(IEnumerable<string> appProtectedFolders)` |
| `SafetyGate` | `SafetyGate(SafetyContext, IAllowedRootRule)`; `GateResult Check(GateRequest)` (intent Outcome 1, steps 1–12 in order); `GateResult CheckRestoreDestination(string originalPath)` (spec "Putting back") |
| `InUseChecker` | `InUseResult Check(ApprovedItem)`: holders (pid, image name where readable, which method named it), count of processes that could not be read |

**Internal seams** (test-only, HARD like Swift's `@_spi(Testing)`): `IEnvironmentFacts` (elevation, SYSTEM, session id, interactive SID) and `IRecycleBinPolicy` (machine policy values a standard user cannot set). Everything else the tests exercise against the real file system.

**Gate details** (intent Outcome 1):
1. Environment: refuse if the token is elevated (`TokenElevation`), the user is SYSTEM (S-1-5-18), the session id is 0, or the token lacks the INTERACTIVE SID (S-1-5-4). An SSH logon has session 0 and no INTERACTIVE SID; the console task has both (re-probed in group 4).
2. Exists: first `GetFileAttributesExW` (no handle, so a `RECALL_ON_OPEN` file is never opened; such an item goes straight to the cloud refusal of step 9, the one exception to the strict order, recorded in the spec note), then `CreateFileW` with `FILE_READ_ATTRIBUTES | READ_CONTROL`, `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT`; never `FILE_READ_DATA`.
3. Reparse, by **allow-list**: no reparse tag, or a cloud tag (`(tag & ~0xF000) == 0x9000001A`) which goes on to step 9; **every other tag refuses** (symbolic link, junction and mount point, app execution alias `0x8000001B`, which has no name-surrogate bit, WOF, WCI, dedup, ProjFS, any unknown tag).
4. Spelling and canonical path: refuse UNC, `\\?\UNC`, `\\.\`, `\\?\GLOBALROOT`, a `:` after the drive (stream), a trailing dot or space in any component, before opening; then `GetFinalPathNameByHandleW(FILE_NAME_NORMALIZED | VOLUME_NAME_DOS)`, and **the canonical result is checked again by the same rules** (a mapped drive to a share resolves to `\\?\UNC\…` and is refused with the step-4 reason).
5. Volume: `GetVolumeInformationByHandleW` file system name `NTFS` or `ReFS`; `GetDriveTypeW` of the volume root `DRIVE_FIXED`; serial from `FileIdInfo`.
6. Identity and stamp from `FileIdInfo` and `FileBasicInfo` / `FileStandardInfo`, compared with the request.
7. Measure: walk with `FindFirstFileExW` (`FindExInfoBasic`, large fetch) on `\\?\` names, never entering a reparse point; collect per entry: path length of the plain spelling, attributes, reparse tag, allocated size, owner where step 11 needs it. Descendants with a tag, one rule for files and folders alike: a **name-surrogate** (file or directory symbolic link, junction, mount point) is not entered, and whether recycling the folder leaves its target untouched is **re-probed in group 5** for each kind; a kind the shell follows refuses the folder, a kind it does not follow is allowed (so a project folder with links is not untouchable); until group 5 settles a kind, it refuses. Any **other** descendant tag follows step 3's allow-list (cloud goes to step 9, everything else refuses). WOF-compressed files are refused until group 5 shows their recycle is recoverable (a known over-refusal, recorded in the spec note).
8. Recycle precondition: `NukeOnDelete` and `MaxCapacity` for this volume's bin from `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{volume GUID}` (an **absent** key or value is the fresh-profile default and means the system default; an **unreadable** one refuses); `NoRecycleFiles` from the machine and user policy keys; the bin's current use from `SHQueryRecycleBinW` on the volume root; room = quota − use; refuse if the measured size exceeds room (which size the bin counts, logical or allocated, and the default quota when `MaxCapacity` is absent are **re-probed in group 5**; until then the larger of the two sizes is used). Refuse if any path in the tree is 260 characters or more (plain spelling).
9. Cloud: refuse if any entry has `RECALL_ON_DATA_ACCESS` or `RECALL_ON_OPEN`, or `CfGetPlaceholderInfo` reports on-disk bytes below the length (a non-placeholder returns its documented "not a cloud file" result, anything else is `CouldNotVerify`); refuse if the item is inside or contains a sync root: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager\*\UserSyncRoots\<SID>` values, `%OneDrive%`, `%OneDriveConsumer%`, `%OneDriveCommercial%`, and the provider folder names directly in the profile that group 1 writes into `safety.md` (`Dropbox`, `Google Drive`, `My Drive`, `OneDrive`, `OneDrive - *`, `Box`, `Box Sync`, `pCloud Drive`, `iCloudDrive`, `iCloud Drive`).
10. Not a volume root, the profile folder, the profiles folder (`FOLDERID_UserProfiles`, never a literal `C:\Users`), another profile, `Public`, `Default`.
11. Protection: never-touch and system locations by path (ordinal ignore-case on canonical paths) and by identity of the item and every folder above it (each opened as in step 2); owner of the item and every descendant from the walk: refuse TrustedInstaller (`S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464`) or SYSTEM (`S-1-5-18`); `SfcIsFileProtected` on files (FALSE with `GetLastError` other than `ERROR_FILE_NOT_FOUND` is `CouldNotVerify`).
12. Allowed roots: `IAllowedRootRule.Evaluate(ItemFacts)`.

**In use** (intent Outcome 3): for every file and folder in the measured tree, `NtQueryInformationFile(FileProcessIdsUsingFileInformation)` on a handle opened as in step 2; plus one Restart Manager session over the tree's files (in batches of 100); plus, for readable processes (`PROCESS_QUERY_LIMITED_INFORMATION` + `PROCESS_VM_READ`), the image path, current directory and command line from the process environment block. Any method naming a holder → in use. Elevated and other-session holders appear through the per-file query and the Restart Manager (`RmCritical`, permission denied) and block; processes whose environment block cannot be read are counted. Microsoft Defender scanning a file is a real holder: it is reported with its image name like any other holder, never filtered out; friendly names and "try again" copy belong to the app. The Restart Manager is used only to list (`RmGetList`), never `RmShutdown` or `RmRestart`.

## Repository layout

```
docs/spec/safety.md                         Windows notes (group 1)
windows/
  Ambysto.Kit.slnx                          gains the two projects below
  src/Ambysto.Kit.Safety/                   the library (no PackageReference)
  tests/Ambysto.Kit.Safety.Tests/           MSTest: acceptance tests (group 3) + unit tests
  tests/Ambysto.Kit.Safety.CloudFixture/    test-only Cloud Files provider (console exe)
scripts/windows-choke-points.sh             the "never changes anything" tripwire, with fixtures
.github/workflows/windows-release.yml       packs both libraries from one windows-v* tag
```

Test categories: `Session0` (runs over SSH as the standard user, environment seam set to "interactive standard user"), `Console` (runs in the test machine's console session through its job runner, real environment), `RealEnvironment` (step 1 against the real token: standard over SSH → refused, administrator over SSH → refused, standard in the console → passes step 1), `AdminSetup` (needs a machine policy the test machine's administrator sets and reverts around the run: `NoRecycleFiles`). Every test that changes a setting restores it in `finally` and in `[ClassCleanup]`.

## Test machine prerequisites (outside this repository)

- A **fixed exFAT volume** (a new virtual disk, formatted exFAT, a drive letter), provisioned by the administrator before group 4, and a new test-bed snapshot.
- The job runner for the console session exists (the test machine's `vm-ui.sh`). A **machine gates runner** (sync, build, the `Session0` and `Console` suites, choke points, Stryker, each with a pass/fail exit) does **not** exist yet; group 0 builds it in the test bed, recorded in the test bed's notes, not here.

## Groups, in order (one commit each)

0. **Test machine** (outside the repository; no commit here): the exFAT volume (done 2026-10-16: `G:`, fixed, exFAT, in the default snapshot), the gates runner, a test-data folder excluded from Defender scanning for the in-use tests, snapshot. Proof: the gates runner runs the license library's 45 tests green as the standard user.
1. **Spec** (HARD, own commit): `docs/spec/safety.md` gains the Windows notes of intent Outcome 6 and the refusal messages (one per `RefusalReason`), and the platform rules (the Trash must be recoverable; an item that would not land in it is refused; any check that cannot be completed refuses). No other spec file changes.
2. **Scaffold and API.** The two projects, the public types of the Design table with `NotImplementedException` bodies, the internal seams, `.slnx`; the test project and the cloud fixture as empty shells. **The release workflow** changes in this group, not later, so `main` stays releasable: it runs the license tests as today and only **builds** the safety projects (their tests need the test machine: a console session, the volumes, the fixture, a standard user; `windows-latest` runs as an administrator). Proof: build 0 warnings in the VM; `dotnet list package` shows none for the library; the workflow change reviewed as HARD.
3. **Acceptance tests** — written by a **separate agent on a different model from the library's author** that sees only the intent, this plan's Design and Principles, `docs/spec/safety.md` and the public API of group 2, never the implementation (none exists yet). One test per refusal and per criterion 1–8 of the intent; **every refusal test has a control**, the same item with only that condition removed, which must be **approved** (so a gate that refuses everything fails the suite), and every test asserts the exact `RefusalReason`; the environment check also has a seam test with session > 0 and no INTERACTIVE SID. Identity tests (criterion 4) use `FileFacts.Read` for "one identity" and the gate for the refusals (a mapped drive to the loopback share carries the local file's identity, which is why criterion 4 calls it one identity, and the gate refuses it at step 4). Plus the cloud fixture (ported from the test bed's provider, which already passed an independent review: placeholders online-only, pinned, partially and fully hydrated, a fetch log). All tests compile and fail (`NotImplementedException`). The main session reviews that each test matches its spec line and would fail if its property were false; hard-reviewer reviews the tests before group 4. Afterwards the tests, and the public API they compile against, change only through a commit that names the spec line it follows, reviewed like code.
   **The test writer is given**: the intent (Outcomes 1–6, criteria), this plan's Principles and the Design's public API table and seams (not its "Gate details" or "In use" paragraphs), `docs/spec/safety.md` after group 1, the group 2 public API and seams, and the test machine's facilities (volumes C: NTFS, E: NTFS second disk, F: FAT32 removable, G: exFAT fixed, the loopback share, the cloud fixture's commands, the test categories, the restore-every-setting rule). **Not given**: this plan's "Gate details" and "In use" paragraphs, the probe programs (a prototype of the implementation) or their logs.
4. **Environment, spelling, volume, identity** (steps 1–6) and `ItemFacts`' owner facts. Re-probe first: the token of the console job runner has session > 0 and the INTERACTIVE SID; an SSH logon has neither.
5. **Measure and recycle precondition** (steps 7–8). Re-probe first: which size counts against the quota (logical or allocated), the default quota when `MaxCapacity` is absent, `SHQueryRecycleBinW` scope (this user's bin only?), the volume-GUID mapping of the `BitBucket` key; for each descendant kind (symbolic link, junction, mount point) whether recycling the folder leaves its target untouched; and intent Outcome 4's question, whether holding the item open (attributes only, all sharing) during the move blocks a provider's dehydration without blocking the recycle. The facts are written into the plan's review log with their outputs; a positive answer to the last one becomes a consumer note in `safety.md`, and an API for it only with another review.
5b. **Spec note** (HARD, own commit): the group 5 facts that change what may be moved (descendant link kinds, WOF, the quota size) written into `safety.md`, reviewed like group 1. **Done before group 4** (2026-10-16): the group 4 and 5 re-probes ran in parallel with the test review, so the spec and the tests were brought to the facts before any implementation. Facts (saved outputs on the test machine): elevation by `TokenIsElevated` (the elevation type is "default" for an SSH administrator); the INTERACTIVE SID is present only in the console session; quota counts logical length in MiB, per user, default written by the shell on first query, key = the volume GUID; junctions and symbolic links inside a recycled folder are recycled as links with their targets untouched and restorable; a mount point inside makes the bin count the mounted volume's capacity (deleted for good); WOF files recycle intact and are invisible to a standard user; a Cloud Files oplock handle blocks dehydration, an attributes-only handle does not. Consequences: links inside folders and WOF are allowed, a folder holding a mount point is refused (detected with `GetVolumeNameForVolumeMountPointW`, read-only, so `DeviceIoControl` stays banned); the affected acceptance tests change in their own commit naming these spec lines.
6. **Cloud** (step 9) with the fixture; sync-root discovery.
7. **Protection, roots, restore** (steps 10–12), never-touch resolution through `SHGetKnownFolderPath` and the environment, `CheckRestoreDestination`.
8. **In use.**
9. **Package, workflow, docs.** csproj package metadata as the license library; the release workflow packs and pushes both libraries from one `windows-vX.Y.Z` tag (the license library's version moves with it; its code is untouched); `scripts/windows-choke-points.sh` (fails on `File.`/`Directory.`/`FileInfo.`/`DirectoryInfo.` writes, moves or deletes, `MoveFile`, `DeleteFile`, `RemoveDirectory`, `IFileOperation`, `SHFileOperation`, `SetFileInformationByHandle`, `DeviceIoControl`, `FILE_READ_DATA`, `GENERIC_READ`, `GENERIC_WRITE`, `DELETE` and `FILE_WRITE_*` access, `CREATE_ALWAYS`/`CREATE_NEW`/`OPEN_ALWAYS`/`TRUNCATE_EXISTING`, `FileAccess.Read`, `File.Open`, `File.ReadAll`, `Registry` writes, `RmShutdown`, `RmRestart`, `CfHydratePlaceholder`, `CfSetPinState`, `Process.Start` anywhere in `windows/src/Ambysto.Kit.Safety`), with a passing and a failing fixture each; `CLAUDE.md` (What it is, Verify, HARD gains `windows/src/Ambysto.Kit.Safety` and its tests and fixture), `README.md`. Rehearsal of criterion 11 with a local feed.

The intent's `status` moves to `planned` when the owner accepts this plan, `building` with group 2's commit, `verified` after the proofs and the code review.

## Risks, and what catches them

| Risk | Caught by |
|---|---|
| A check errs open (an API failure read as "fine") | Principle "fail closed"; each check has a test that injects its failure through a real condition (access denied folder, unknown file system, an **unreadable** `BitBucket\Volume\{GUID}` key: a deny ACE the standard user sets on its own key, removed in `finally`) where one exists |
| The acceptance tests mirror the code | written before the code by another agent; reviewed by hard-reviewer before group 4; changed afterwards only with a spec-line commit |
| The gate starts a cloud download or reads data | the cloud fixture's fetch log stays empty (criterion 3); the choke-point script bans data access |
| Wrong quota arithmetic lets the bin purge older items | group 5 re-probe; criterion 2's purge test with a small quota; the larger of logical and allocated size until the probe settles it |
| Session/elevation detection wrong in one context | `RealEnvironment` tests in all three contexts |
| P/Invoke layouts wrong (`FILE_ID_INFO`, `CF_PLACEHOLDER_BASIC_INFO`, `RM_PROCESS_INFO`) | `LibraryImport` with blittable structs and size asserts in tests; hard-reviewer checks them against the SDK headers |
| A long descendant invisible to the walk | extended-length enumeration; a test with a 300-character descendant |
| Sync roots registered in a layout the code does not read | the test machine's real OneDrive (test account) root must be found; the fixture registers its own root, which must be found |
| Microsoft Defender holds a file it scans, so the per-file query names it: false "in use" for users, flaky tests | reported by name to users (retry); in-use tests use a Defender-excluded test-data folder (group 0) and assert that the expected holder is named, not that it is the only one |
| Recycling a folder follows a descendant link, junction or mount point into its target | group 5 re-probe per kind; a followed kind refuses the folder |
| The release workflow runs tests that need the test machine | group 2 changes it to build-only for the safety projects |
| In-use check misses a holder kind | criterion 6's cases including the elevated holder (an administrator process started by the `AdminSetup` step) |
| Mutation testing too slow for integration suites | Stryker runs the `Session0` suite (environment seam set) on the library; survivors in code only `Console` tests reach are listed and explained |
| The time between gate and move | accepted residual (intent Outcome 4); consumer duties are documented in the spec notes |

**Mutation checks by hand** (break on purpose, see a test fail, restore; recorded in the review log), on top of Stryker: (a) treat a `CfGetPlaceholderInfo` error as "not a placeholder" → the fail-closed cloud test fails; (b) compare volumes by drive letter → the `subst` and loopback-share tests fail; (c) drop the descendant length check → the 300-character test fails; (d) use the logical size only when the bin counts allocated (or the reverse, per group 5's fact) → the quota test fails; (e) skip the INTERACTIVE SID check → the seam test (session > 0, no INTERACTIVE SID) fails; (f) let an allowed-root rule run before protection → criterion 8's test fails; (g) open with `FILE_READ_DATA` → the choke-point script fails.

## Proof (what "done" means)

| Criterion | Proof |
|---|---|
| 1 | All acceptance and unit tests green in the VM as the standard user (`Session0`, `Console`, `RealEnvironment`, `AdminSetup`), counts in the review log; Stryker.NET report with every survivor explained; the hand mutation checks above |
| 2 | `Console` tests for each probe row, each refused before any move, settings restored (verified after the run) |
| 3 | cloud fixture fetch log empty across all cloud tests |
| 4–8 | their acceptance tests |
| 9 | `dotnet build -c Release` 0 warnings; `dotnet list … package` none for the library |
| 10 | `git diff main -- Sources Tests Package.swift windows/src/Ambysto.Kit.License` empty |
| 11 | rehearsed in group 9 with a local feed; after the owner's tag, from the local feed filled from GitHub Packages |
| Code review | hard-reviewer on the acceptance tests (before group 4) and on groups 1–9: PASS |
| Choke points | `scripts/windows-choke-points.sh` passes on the library and fails on each fixture |

Not run here: anything on macOS (out of scope on this machine).

## Owner steps

- ~~Accept this plan.~~ Done 2026-10-16.
- ~~After the merge: push `windows-v0.2.0` on `main` (the tag ruleset already restricts `windows-v*`).~~ Done 2026-10-18 on `7eb116b` (workflow run 39086375948 published `Ambysto.Kit.License` 0.2.0 and `Ambysto.Kit.Safety` 0.2.0).

## Review log

**Round 1 (hard-reviewer: Fable 5.1, FAIL, 2026-10-16).** 4 MAJOR, 7 MINOR, 2 NIT; addressed in this revision.
1. MAJOR: no public API gives an app a `FileStamp` for its scan or its post-move check. Addressed: `FileFacts.Read`; criterion 4 tested through it.
2. MAJOR: the app execution alias has no name-surrogate bit; WOF, WCI, dedup and ProjFS tags unspecified. Addressed: step 3 is an allow-list (no tag, or cloud); every other tag refuses.
3. MAJOR: refusal-only tests pass a gate that refuses everything. Addressed: a control that must be approved for every refusal, exact reasons, the API frozen with the tests, what the test writer gets and does not get.
4. MAJOR: the release workflow would run safety tests on `windows-latest`. Addressed: group 2 makes it build-only for the safety projects.
5. MINOR: dropping the INTERACTIVE-SID check would not fail a test. Addressed: a seam test, hand check (e).
6. MINOR: a mapped drive resolves to a UNC canonical path. Addressed: step 4 re-checks the canonical result.
7. MINOR: the provider folder list referred to a Space spec; iCloud missing. Addressed: step 9, written into `safety.md` in group 1.
8. MINOR: "a folder holding a link refuses" went beyond the intent; an absent `BitBucket` key is the default. Addressed: step 7 (re-probe per kind in group 5), step 8 (absent = default, unreadable = refuse).
9. MINOR: `RECALL_ON_OPEN` may fetch on open; `SfcIsFileProtected` errors. Addressed: step 2 reads attributes without a handle first; step 11.
10. MINOR: choke list gaps. Addressed: group 9.
11. MINOR: Outcome 4's "hold open during the move" question uninvestigated; Defender as a holder. Addressed: group 5, Risks, In use.
12. NIT: the gates runner does not exist. Addressed: group 0 builds it.
13. NIT: literal `C:\Users`. Addressed: step 10.

**Round 2 (hard-reviewer: Fable 5.1, PASS, 2026-10-16).** All round-1 MAJORs verified in the text. 4 MINOR and 3 NIT, fixed after the PASS:
1. MINOR: step 7 contradicted itself for a file symbolic link; the per-kind outcome needed a spec commit after the group 5 probe. Fixed: one rule for name-surrogates (refuse until group 5 settles a kind), other tags by the allow-list, WOF refused until shown recoverable; group 5b, a second HARD spec commit.
2. MINOR: what the test writer sees was stated two ways. Fixed.
3. MINOR: `FileFacts.Read` could open a `RECALL_ON_OPEN` file; its answer for a mapped drive. Fixed.
4. MINOR: the Risks row still treated a missing registry key as a failure. Fixed: an unreadable key, made by the standard user on its own key.
5. NIT: `RECALL_ON_OPEN` is the one exception to the strict order. Fixed: recorded.
6. NIT: the loopback share carries the local file's identity. Fixed.
7. NIT: Defender copy and friendly names belong to the app. Fixed.

**Acceptance tests (plan group 3; written by a separate agent on Fable 5.1 before any implementation).** hard-reviewer: round 1 FAIL (3 MAJOR: a profile picked that is a junction; deny ACEs that do not make an item unreadable for its owner; no descendant *directory* of 260+ characters), round 2 PASS. 196 tests at the end of group 3; all failed with `NotImplementedException` against the stubs (one named helper self-check passed; two ignored with measured facts: a `RECALL_ON_OPEN` item cannot be produced, nor a placeholder query that fails for a real reason). Later test changes were made by the same agent, each naming the spec line it follows; the writer never read the implementation.

**Questions the loop raised and how they were settled (2026-10-16).** The test writer's questions went to the main session, not the owner: partial hydration and recall-on-open (fixture extended; recall-on-open stays ignored), step 10 moved right after step 5 (a path check: a drive or profile is never walked; reported to the owner), Pictures redirection in tests, restore reasons (`RestoreDestinationExists`, `RestoreFolderMissing`), a sync root inside an item is a cloud file (spec order), WOF fixtures by the administrator.

**Spec changes found during the build** (each its own HARD commit, reviewed with the code): 5b, measured before group 4 (links and WOF inside folders allowed, mount points refused, Recycle Bin sizes); an unknown special file inside a folder refuses like a mount point; cloud-sync roots asked per item (a classic Cloud Files registration is in no list); restore order (exists, cloud-sync root, folder gone); an owner above the item that cannot be read is unknown and not the user's, while a folder above that cannot be opened refuses. None loosens a decision of the owner.

**Code review (hard-reviewer: Fable 5.1, 2026-10-16).** Round 1 FAIL: 1 MAJOR (put-back applied the delete-side "contains" rule to the destination's parent, refusing every put-back into the profile or a drive root), 6 MINOR, 3 NIT; all fixed in `c8cd06e`, the mount-point check reworked in `c92cf55` after measuring that junctions answer 87, 206 and other codes. Round 2 PASS with 1 MINOR (placeholder info read without checking the returned length), fixed in `f6c4f02`, and 1 NIT (three test commits named `wip(5b)`, already pushed; left as they are).

**Deviations from this plan.** Group 5b ran before group 4 (the probes were done during the test review). Groups 4–8 are one commit (`e64121d`): the code grew through the same files. The SFC backstop accepts the measured codes for "not protected" (access denied, insufficient buffer for paths of about 250+) instead of "anything but file-not-found", which would refuse every user file; the files Windows Resource Protection covers lie in system locations, which step 11 refuses first. The release workflow runs one command per step (found while writing group 9).

## Proof record (2026-10-16, Windows 11 VM, standard user unless noted)

| Criterion | Result |
|---|---|
| 1 | All suites green on a clean build of `2bf8910` (code of `f6c4f02`): license 45, safety 171 (2 ignored with facts), console 4, real-ssh 3, real-admin 3, admin-setup 24, 0 warnings. Stryker.NET: **running at merge time** (1320 mutants, concurrency 1, several hours); its result and every surviving mutant's explanation follow in a separate PR, and `windows-v0.2.0` is not tagged before it. Hand mutations: (b) trusting the given spelling → `AMappedDriveResolvesToANetworkPathAndIsRefused`; (c) no long-path check → 6 long-path tests; (d) allocated instead of logical size → `TheLogicalLengthCountsNotTheSpaceOnDisk`; (e) no INTERACTIVE-SID check → `ASessionAboveZeroWithoutTheInteractiveSidIsRefusedAsNotInteractive`; (f) the rule before protection → 17 protection tests; (g) `FILE_READ_DATA` → the choke-point tripwire; review fix 1 reverted → `ADestinationDirectlyInTheProfileFolderIsApproved`. Not demonstrable: (a) a placeholder query error read as "not a placeholder" (no real condition makes the query fail; its test is ignored with the facts) and the item-swap check of review item 6 (a race inside one call; defence in depth that can only add a refusal) |
| 2 | Removable (F:), network (loopback share), bin off, `NoRecycleFiles` (admin setup), over quota, purge needed, 260+ in the tree, online-only, partly downloaded, a folder holding an online-only file, FAT32 (F:) and exFAT (G:): each refused before any move with its reason; settings restored by each test |
| 3 | The cloud fixture's fetch log stays empty across every gate and `FileFacts` call |
| 4 | 8.3, case, `subst`, mapped drive, hard link: one identity through `FileFacts.Read`; junction, symbolic link, mount point, UNC, stream, trailing dot or space: refused |
| 5 | Seam tests, plus the real environment: the standard user over SSH refused as not interactive, the administrator over SSH refused as elevated, the standard user in the console approved |
| 6 | Open file (with and without delete sharing), loaded DLL, read-only and read-write mapped views, current-directory folder, image, command line, an elevated (SYSTEM) holder: in use, named by the per-file query and the Restart Manager |
| 7 | Never-touch rows through known folders and the environment; a redirected Pictures folder still protected |
| 8 | An allow-everything rule cannot pass a never-touch item and is not even asked; `Administrators`-owned folders are not the user's |
| 9 | 0 warnings; no package reference in the library |
| 10 | `git diff main -- Sources Tests Package.swift windows/src/Ambysto.Kit.License`: empty |
| 11 | **Done with `windows-v0.2.0`** (`7eb116b`, run 39086375948): in the VM's console session as the standard user, a scratch console app outside the repo restores `Ambysto.Kit.Safety` 0.2.0 from the local feed (filled on the host from GitHub Packages, assembly `0.2.0.0`) and refuses items on the removable NTFS drive (`H:`) and the removable FAT32 drive (`F:`) as `UnsupportedVolume` ("Only fixed NTFS or ReFS drives are supported."); Lean for Windows (ASP-7) also consumes `Ambysto.Kit.Safety` 0.2.0 from the same feed |
| Choke points | `scripts/windows-choke-points.sh` passes on the library; 36 of 36 seeded fail fixtures fail |

Not run: anything on macOS (out of scope on this machine).

## Stryker follow-up (branch `aps-24-stryker-followup`, after PR #60)

Stryker.NET on 2bf8910 (Session0 suite): 1320 mutants, 517 killed, 264 survived, 185 no coverage, 10 timeouts, score 54 %. A read-only triage (`~/vms/probes/kit-stryker/triage.md`) classed the 449 survivors and uncovered mutants: 198 missing tests (41 safety-relevant and constructible, 30 reachable only through a fault seam, 18 pinned by CloudTests that did not run under Stryker, 40 pinned only against a context cached before any mutant is active, 69 reason or precision only), 112 equivalent, 69 message-only, 47 unreachable error paths, 23 outside the Session0 suite. Possible defects it named: protection and sync-root entries that did not exist when the context was resolved keep the spelling they were given (a subst or mapped-drive spelling then matches neither by path nor by identity once created); `Owners.Chain` is dead code; fail-closed branches that no real condition reaches have no guard; and two spec gaps the owner decided:

Owner decisions 2026-10-17 (spec updated in this branch): (1) the process that asks is never an in-use holder; the app answers for its own handles. (2) Put-back applies the folder name rules too: a folder put back under a protected folder name (`WindowsImageBackup`) is refused. `CheckRestoreDestination` takes a path only, so both name rules apply to every destination (no public API change; a file named like a protected folder is refused too, which fails closed).

Order: the tests first (the test writer, from the gap list in spec terms, plus the two harness fixes), then the code fixes, then hard-review, gates and Stryker again; every surviving mutant is explained here before the owner's `windows-v0.2.0` tag.

Follow-up build notes (2026-10-17):
- **Fault seam** (`Internal/Faults.cs`, internal, test-only): every fail-closed system query passes its result through it (Win32 BOOL, HRESULT, Win32 code, NTSTATUS), so a test can make one query fail on its own async flow and the real error branch runs. It can also make Windows Resource Protection answer "covered", change what a listing or the attribute read by name reports, and run a hook on every open and between step 6's stamp and step 11's identity read. With nothing injected, every result passes through unchanged; the per-call cost is one `AsyncLocal` read. It reaches G20–G23, G25 and G26 (tests by a separate agent). **G24** (a system location that did not exist when the context was resolved) is accepted: only `%LOCALAPPDATA%\Microsoft\WindowsApps` qualifies, a context-building seam would reach into the public `SafetyContext`, and step 11 still matches it by path.
- **G13**: a put-back destination behind a link Windows will not follow for anyone (`ERROR_UNTRUSTED_MOUNT_POINT`, a standard user's junction) was refused as CouldNotVerify; it is now refused as a link. The gates on 0c1d780 saw the test fail; it passes with the fix.
- **Test bed incident**: the new G07 fixture (an administrator-made junction to `E:\`) combined with `icacls /T` in setup and `takeown /R` + `icacls /T` in teardown. Both follow junctions, so they rewrote E:'s ACLs, and `SHQueryRecycleBin(E:\)` then blocked a testhost for two hours. Setup and teardown now work item by item and never enter a reparse point; an ACL listing of E: and G: before and after a setup/teardown cycle showed no change outside the fixture. The test machine's E: Recycle Bin was repaired in a new default snapshot, `ambysto-testbed-2026-10-17`.
- **Defect 6, owner decision A (2026-10-17)**: a damaged Recycle Bin can make `SHQueryRecycleBin` block forever. The usage query now runs on a thread of its own and fails closed after 10 s ("Could not verify: Recycle Bin settings"); the stuck thread is left behind; an exception on it is rethrown on the caller's thread. Spec `safety.md` "Windows: failing closed" says so. Other system queries that could in principle block (Restart Manager, `CfGetSyncRootInfoByPath`, `SfcIsFileProtected`, `NtQueryInformationFile`) stay unbounded: none was seen to block, and each would need the same treatment if one is.
- **Defect 7, fixed 2026-10-17, hard review PASS (found by Lean's first real move)**: `ApprovedItem.AllocatedBytes` ("bytes the item's tree uses on disk") sums `GetCompressedFileSize`, which returns the file's length for a file that is neither compressed nor sparse, not the clusters it uses: a 5,002-byte file reported 5,002, not 8,192. Nothing in the gate decides on it (the Recycle Bin size checks use `LogicalBytes`), so it is a wrong number, not a safety gap. Fix (HARD, main session, then hard-reviewer with the other follow-ups): read each file's allocation size from its handle (`FileStandardInfo`, opened for attributes only, never a cloud file), and ask `GetCompressedFileSize` only when that is 0 for a file over 1 KiB (WOF), as Lean's walker does; a test against a fixture of known cluster use. Done: `TreeWalk.Allocated` as described, `FileProbe.AllocationSize` behind the existing `AllocatedSize` fault; test `AnApprovedPlainFileCountsTheClustersItUses` (5,002 bytes → two 4 KiB clusters, from the volume's cluster size; a 100-byte file reports 104, inside its record). Session-0 suite 296 passed, 2 skipped. Hand mutation (every file back to `GetCompressedFileSize`): CAUGHT by `AnApprovedPlainFileCountsTheClustersItUses` (2026-10-17, `vm-mutant.sh`, session-0 test, unlocked mode). Merged in #61 (`7eb116b`, tagged `windows-v0.2.0`).
- **Stryker run 3 and final test PR (#63, `06a48a5`, 2026-10-18)**: run 3 (`probes/kit-stryker/triage-3.md` on the test machine) reached 76.64 % (804 / 1049: 791 killed, 13 timeout, 172 survived, 73 no coverage). The one remaining open S1 (`#669`, put-back drive-type check on a removable NTFS volume, `H:`) and the 5 `C` mutants (`#91`, `#104`, `#107`, `#110`, `#112`, browser/mail rows tested against a cached context) were closed by tests in #63 (written by Fable as test writer; hand mutations caught; all suites green: license 45, safety 297, console 4, real-ssh 3, real-admin 3, admin-setup 28, admin-size-policy 1).

Shipped: #60 (`403372b`), #61 (`7eb116b`, tagged `windows-v0.2.0`) and #63 (`06a48a5`). Criterion 11 passed on `Ambysto.Kit.Safety` 0.2.0.
