---
status: shipped (PR #60, #61, #63)  # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit for Windows 0.2 — the file-safety gate (.NET)

Ticket: APS-24, part of APS-2 (#5). Follows APS-23 (#53, `Ambysto.Kit.License`), whose intent said each other Kit module for Windows gets its own intent. Its first consumer is Ambysto Lean for Windows (ASP-7 in `ambysto/ambysto-space`).

## Problem

No Windows app of the suite can decide safely whether an item may be moved to the Recycle Bin. `docs/spec/safety.md` holds the rules, but its Windows notes are one line each, several rows say "—", and it is silent on what Windows does differently. Probes on the Windows 11 test machine (2026-10-16, standard user, the shell's `IFileOperation` with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT`) show that "move to the Recycle Bin" is not a safe operation on its own:

- **It deletes for good while `PerformOperations` returns S_OK** for: items on removable and network volumes; online-only cloud files (a test Cloud Files provider and real OneDrive); an item larger than the bin's quota; a volume whose bin is off (`NukeOnDelete`) or a machine with the `NoRecycleFiles` policy; a path of 260 characters or more on a volume without 8.3 names (or from a long-path-aware process). For online-only and over-quota items the shell's pre-delete flags still say "recycle if possible".
- **It silently purges older bin items** when a new item needs room under the quota.
- **Recycling a folder downloads every online-only file inside it** first.
- A file open without delete sharing leaves its folder in place, yet `PerformOperations` returns S_OK (only `GetAnyOperationsAborted` tells).
- A recycled item keeps its volume serial and 128-bit file id. After each delete the shell reports the new bin item; in 89 re-check runs a reported item always matched the original (serial, id, size, hash), and a missing report never came with an item in the bin (the item was either gone or untouched).
- **A short folder holding a descendant path of 260 characters or more** is deleted for good **as a whole**, its short files included, and nothing reaches the bin (the top item's flags say "recycle if possible", each descendant's do not); aborting every callback that lacks the flag left the tree intact. A path of 259 recycles everywhere; 260+ is deleted for good on a volume without 8.3 names, and on any volume when the calling process is long-path-aware.

On macOS the Trash has none of these traps, so the spec never needed to say so. On Windows the gate must refuse every case where the move would not be recoverable **before** the move, and the consumer must still verify after it.

Owner decisions (2026-10-16, answers to the Windows question batch):
1. **Refuse** any item whose move would not land in the Recycle Bin: not on a fixed local NTFS or ReFS volume, the volume's bin off or disallowed by policy, the item larger than the room the bin has, a path the shell would not recycle.
2. **Refuse** a move that would make Windows purge older bin items (the user's own deleted files, or an app's earlier moves that its journal can still put back). The user is told the Recycle Bin is full.
3. **Refuse** online-only cloud files ("they are not on this hard disk"), and **refuse** any folder that holds one (recycling it would download them).
4. **Never touch** cloud-sync roots in v1 (OneDrive, Dropbox, Google Drive, Box, iCloud for Windows and any registered provider), local files included: deleting there deletes on every device. Accepted cost: with OneDrive's folder backup on, Desktop, Documents and Pictures are out of reach.
5. **No move at all** while the process runs elevated (administrator token), as SYSTEM, or outside an interactive session. This is the Windows counterpart of the macOS Full Disk Access check.
6. The never-touch list gains the Windows entries in Outcome 2 (credentials and their encryption keys, the user's registry hives, mail stores, Store app data, device backups, File History, password vaults). A virtual-machine disk (`*.vhdx`, `*.vhd`) in an allowed root is **not** protected, like a VM image on macOS.
7. The gate is a **Kit package**, `Ambysto.Kit.Safety`, from the start; Windows apps never carry a copy (unlike Space/Lean on macOS, which vendored it).
8. Allowed roots stay the app's rule (spec step 8). For Lean v1 the owner chose the home folder plus folders the user created at the root of the system volume (ASP-7); the Kit provides the contract and the facts such a rule needs (Outcome 5).

Owner decision, test machine (2026-10-16): the Windows VM gets Ambysto packages from a **local feed filled on the host** (the host downloads the tagged package with a `read:packages`-only token); no GitHub token is kept inside the VM. This replaces APS-23's "a `read:packages` token in the VM's user NuGet config"; the release workflow is unchanged.

## Outcome

`windows-v0.2.0` of `ambysto/ambysto-kit`: a second .NET library, **`Ambysto.Kit.Safety`** (namespace `Ambysto.Kit.Safety`), in `windows/`, built from `docs/spec/safety.md` (with the Windows notes below) and never from the Swift code. It decides; it never deletes, moves, recycles or restores anything.

### 1. The gate (spec "The gate", Windows form)

Immediately before every move of every item, in this order; the first failure refuses the item with its reason:

1. **Environment**: not elevated, not SYSTEM, an interactive session (decision 5).
2. **Exists**, opened for attributes only (`FILE_READ_ATTRIBUTES`, `FILE_FLAG_BACKUP_SEMANTICS`, `FILE_FLAG_OPEN_REPARSE_POINT`), never for data, so the gate never starts a cloud download.
3. **Not a name-surrogate reparse point** (symbolic link, junction, mount point, app execution alias).
4. **Canonical path** from the open handle (`GetFinalPathNameByHandle`): 8.3 names expanded, case taken from the file system, `subst` and mapped drives resolved. Refused spellings, checked here and therefore before any volume comparison: UNC and `\\?\UNC` (a loopback share carries the local volume's serial and the local file id, so it must never reach step 5), device paths (`\\.\`, `\\?\GLOBALROOT`), alternate data streams, names ending in a dot or a space.
5. **Volume**: a fixed local NTFS or ReFS volume, compared by volume serial, never by drive letter. FAT32 and exFAT are refused: identity (step 6) needs `FILE_ID_INFO`, which neither provides (probe: ERR 87; their file index changes on rename and move).
6. **Unchanged since shown**: same volume serial and 128-bit file id (`FILE_ID_INFO`), same last-write time (100 ns), and for files the same length.
7. **Measure** the item now (never the app's earlier number): the gate walks a folder itself, without entering reparse points and without opening any file for data. Every check below that needs the tree (size, descendants) uses this walk.
8. **Recycle precondition** (decisions 1–2): this user's bin on that volume is on and not disallowed by policy; the measured size is at most the room left in the bin without purging anything; **no path in the item's tree is 260 characters or longer**, measured on the canonical plain spelling (no `\\?\` prefix); the walk enumerates with extended-length names so a long descendant is never invisible to it (Problem: such a tree is deleted for good).
9. **Cloud** (decisions 3–4): neither the item nor anything in its tree is a cloud placeholder that is not fully on disk. Two signals, either one refuses: the attributes `RECALL_ON_DATA_ACCESS` / `RECALL_ON_OPEN` (present in the default .NET process mode in both sessions, probe), and `CfGetPlaceholderInfo` reporting fewer bytes on disk than the file's length (partially hydrated files are refused too). Not inside, and not containing, a cloud-sync root (registered sync roots read from the system, plus the known provider folders).
10. Not a volume root, the profile folder, `C:\Users`, another user's profile, `Public` or `Default`.
11. **Protection**: not inside, and not containing, a never-touch entry or a system location, compared by path and by identity (volume serial + file id) of the item and every folder above it; neither the item nor anything in its tree owned by TrustedInstaller or SYSTEM or protected by Windows Resource Protection.
12. **Allowed roots**: the app's rule, which runs only after protection passed and cannot widen it.

### 2. Never touch, Windows rows

Resolved through known-folder APIs and the environment, never by hard-coded drive letters, so redirected folders are covered:

- `%USERPROFILE%\.claude\projects`, `.ssh`, `.gnupg`; `Pictures` (known folder).
- Credentials and their keys: `%APPDATA%\Microsoft\{Protect, Credentials, Crypto, SystemCertificates}`, `%LOCALAPPDATA%\Microsoft\{Credentials, Vault}`.
- The user's registry hives: `NTUSER.DAT*`, `UsrClass.dat*`.
- Mail: `%LOCALAPPDATA%\Microsoft\Outlook`, any `*.pst` / `*.ost`, Thunderbird profiles.
- Store app data: `%LOCALAPPDATA%\Packages`.
- Device backups: `%APPDATA%\Apple Computer\MobileSync\Backup`, `%USERPROFILE%\Apple\MobileSync\Backup`; File History; `WindowsImageBackup`.
- Password vaults: any `*.kdbx`.
- Browser profiles (Chrome, Edge, Brave, Firefox, Arc), cloud-sync roots (decision 4), the Recycle Bin of every volume (its size may be read).
- System locations: `%SystemRoot%`, `Program Files`, `Program Files (x86)`, `ProgramData`, `$Recycle.Bin`, `System Volume Information`, `Recovery`, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`, `Windows.old`, `$WINDOWS.~BT`, `%LOCALAPPDATA%\Microsoft\WindowsApps`.
- Apps add their own data folders; nothing removes an entry (unchanged rule).

### 3. In use (spec "In use", Windows form)

An item is in use when any process has a file inside it open or mapped, has it as its executable or current directory, or names it in its command line where readable. Methods, from the recorded probe outputs: the per-file process query (`NtQueryInformationFile`, `FileProcessIdsUsingFile`) for every file **and folder** in the tree (a current-directory holder shows on the folder, not on the files inside), which named every holder in the probes (open files, a loaded DLL, read-only and read-write mapped views); the Restart Manager as well, which reports an elevated or other-session holder (as critical, permission denied) but misses read-only mapped views and refuses folders; a folder used as a current directory cannot be moved at all. The rule is conservative: **any method that names a holder makes the item in use**, an elevated or other-session holder included. Processes that cannot be read are counted and reported and never block, as the spec says. Matching is by identity as well as by path.

### 4. What the gate cannot close

Conditions can change between the gate and the move: another process fills the bin, a cloud provider frees up space on the very file, a policy changes. The consumer therefore (a) runs the gate immediately before each single item, never for a batch; (b) aborts the shell's delete whenever any pre-delete callback, **the item's or any descendant's**, does not say "recycle if possible" (this catches removable, network, bin off, policy, long paths and long descendants in the probes, but **not** quota or online-only, where the flag is set); (c) after the move, opens the item the shell reports and compares identity and size, so a loss is detected and stops the session. The remaining risk, accepted: in a window of seconds, another process fills the bin or a provider dehydrates the item; it is detected and reported, not prevented. Whether holding the item open during the move can block dehydration without blocking the move is investigated in the plan.

### 5. Facts for the app's rules, and putting back

- Facts an allowed-root rule may use: the profile folder and the system volume; for an item and each folder above it, the owner SID and whether it is the current user; the volume serial and kind; whether a path is a system location. An owner check is conservative: a folder owned by `Administrators` (created from an elevated prompt or by an installer) is not "owned by the user".
- Putting back: the spec's destination check with Windows identity. The restore move itself stays in the app (the shell's restore blocks on a "Replace or Skip" dialog when the destination is taken).

### 6. Spec, tests, packaging, repository

- `docs/spec/safety.md` gains Windows notes for every row and step above, the decisions as platform rules where they are not Windows-only (the Trash must be recoverable; an item that would not land in it is refused), and "volume + file id" for "device + inode" on Windows. A separate commit, reviewed as HARD.
- **Testing the environment check**: the environment facts (elevation, SYSTEM, session) are read through an internal seam that only the test project can replace (the .NET counterpart of `@_spi(Testing)`; HARD, like the Swift seam). Real-environment tests as well: the standard user over SSH (session 0) is refused; the administrator account over SSH (elevated) is refused; the standard user in the console session passes step 1. Every other gate test runs in the console session through the test machine's job runner.
- A test Cloud Files provider (online-only, partially and fully hydrated, pinned placeholders, a fetch log) lives in the test tree, so the cloud rules are proven without any account.
- The test machine gains a **fixed exFAT volume** (a provisioned disk, an administrator step done before the build), so the FAT/exFAT refusal runs as the standard user.
- The environment seam stays internal to the Kit and its tests; an app tests its own handling of an environment refusal through its own seam around the gate call.
- Packaging like APS-23: `Ambysto.Kit.Safety.<version>.nupkg` from a `windows-v0.2.0` tag on `main`, GitHub Packages, the same workflow extended to pack both libraries.
- `CLAUDE.md` gains the Windows library under "What it is", HARD (the whole library, as for `AmbystoSafety`) and Verify.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | Every refusal in Outcome 1 has a test that fails when its check is removed | MSTest in the VM; **Stryker.NET** on the `Ambysto.Kit.Safety` project, every surviving mutant explained in the PR |
| 2 | The probe rows are reproduced as tests, each refused **before** any move with the spec's reason: removable, network, bin off, `NoRecycleFiles`, over quota, purge needed, a path of 260+ characters in the tree, online-only, partially hydrated, a folder holding an online-only file, FAT/exFAT | integration tests in the VM's console session; every setting a test changes is restored by it |
| 3 | The gate never starts a cloud download: the test provider's fetch log stays empty across every gate call | integration test |
| 4 | Identity: the same file through an 8.3 name, another case, `subst`, a mapped drive and a hard link has one identity; a junction, a symbolic link, a mount point, a UNC path to a local file, an alternate data stream and a trailing-dot name are refused | tests |
| 5 | Elevated, SYSTEM and session-0 callers are refused before any other check (seam tests, plus the real-environment tests of Outcome 6) | tests |
| 6 | In use: a file open by another process with and without delete sharing, a loaded DLL, a mapped view, a folder used as a current directory, an elevated holder → in use whenever any method names the holder, with name and id where readable | tests |
| 7 | Never-touch entries resolve through known folders and environment variables; a redirected known folder is still protected | tests |
| 8 | Allowed-root rules cannot widen protection: a rule that allows everything still cannot pass a never-touch item; the owner facts report `Administrators`-owned folders as not the user's | tests |
| 9 | 0 warnings, nullable on, warnings as errors; no package reference in the library; MSTest only in tests | build in the VM, `dotnet list package` |
| 10 | The Swift Kit is untouched (`Sources/`, `Tests/`, `Package.swift`); the license library is untouched except the shared workflow | PR diff review |
| 11 | After the owner pushes `windows-v0.2.0`, a scratch app in the VM installs `Ambysto.Kit.Safety` from the local feed and refuses an item on the removable test drive with the spec's reason | owner-assisted tag, then the VM |

## Constraints

- **HARD** throughout (`CLAUDE.md`: `AmbystoSafety` and its public API; the same for its Windows counterpart): the main session writes it; hard-reviewer reviews the plan and the code.
- .NET 10 LTS, `net10.0-windows` (it needs Win32 APIs), C#, P/Invoke only; no third-party dependency; MSTest in tests.
- Built from the spec only. Where the spec is unclear, stop and ask; never read the Swift code for the answer.
- Facts this intent cites come from probe outputs saved on the test machine; the plan names each one it relies on, and a fact without a saved output is re-probed before it is used.
- Public repository: nothing private (no host names, account names, paths of the owner's machine, tracker hosts or project ids; ticket keys such as APS-24 are fine).
- English on GitHub; branch `aps-24-kit-windows-safety`; one commit per plan group; a PR per phase; never a push to `main`; no merge without being asked.

## Out of scope

- The move itself (`IFileOperation`), its post-verify, the journal and the restore move: each app's single deletion point (spec line 3). Lean's are in ASP-7.
- Process reading beyond what the in-use check needs (`AmbystoProcess` for Windows gets its own intent), suite bar, theme.
- Other volumes than the system volume as allowed roots; removable and network volumes are refused, not supported.
- Cloud-sync roots as allowed roots (decision 4).
- ARM64 builds; Windows 10.

Approved by the owner on 2026-10-16.

## Open questions

_None for the owner._ Decided on 2026-10-16 (see Problem). Verified 2026-10-16 except the Stryker run (see the plan's proof record), merged on the owner's request. Defaults set by this intent, for review: the check order, the never-touch rows, the conservative long-path and in-use rules, mutation testing in criterion 1. All cited facts have saved probe outputs (re-checked 2026-10-16).

## Review log

**Round 1 (hard-reviewer: Fable 5.1, FAIL, 2026-10-16).** 4 MAJOR, 6 MINOR, 1 NIT; addressed in this revision.
1. MAJOR: no statement of the gate-to-move window (bin filled or item dehydrated after the gate). Addressed: Outcome 4.
2. MAJOR: "a path the shell recycles" was not a rule, and the C: long-path log contradicts the README (the shell reported a bin item, the file was gone). Addressed: step 8 refuses any path of 260+ characters in the tree; the "reported item is proof" claim withdrawn; re-probe running.
3. MAJOR: step 1 refuses session 0, yet tests run over SSH. Addressed: Outcome 6, the test seam and the real-environment tests.
4. MAJOR: the in-use method rested on probe outputs that were not saved, and a saved one shows the Restart Manager naming an elevated holder. Addressed: Outcome 3 is conservative (any method naming a holder blocks); the method is chosen in the plan from re-probed outputs; criterion 6 changed.
5. MINOR: identity before volume gave the wrong reason on FAT. Addressed: volume is step 5.
6. MINOR: the FAT/exFAT "a move changed the id" claim had no output. Addressed: replaced by the recorded ERR 87; exFAT re-probed.
7. MINOR: placeholder signals depend on the process mode; partially hydrated files. Addressed: two signals, either refuses; partial refused.
8. MINOR: who sizes a folder for the quota check; mutation tool unnamed. Addressed: step 7, criterion 1.
9. MINOR: no facts API for allowed-root rules. Addressed: Outcome 5, criterion 8.
10. MINOR: UNC must be refused before the serial comparison; owner check scope. Addressed: steps 4 and 11.
11. NIT: "no tracker ids" vs ticket keys. Addressed: Constraints.

Re-probe after round 1 (2026-10-16, raw outputs saved): the C: long-path "phantom" was a bug in the probe's own bin check; a reported bin item matched the original in every run. New: a folder with a 260+ descendant loses the descendants (Problem; Outcome 4(b) now covers descendants). In use: the per-file process query named every holder; the Restart Manager names an elevated holder. exFAT behaves like FAT32.

**Round 2 (hard-reviewer: Fable 5.1, PASS, 2026-10-16).** All round-1 items verified in the text; the new facts verified against the re-probe outputs. 4 MINOR and 3 NIT, fixed after the PASS:
1. MINOR: a short folder with a 260+ descendant is deleted for good as a whole (short siblings included), not only the long descendants. Fixed: Problem.
2. MINOR: a missing report did not always mean "gone"; it never meant "in the bin". Fixed: Problem.
3. MINOR: the seam was test-project-only while Lean's criterion counted on it. Fixed: the seam stays internal; apps use their own seam (Outcome 6; Lean criterion 1).
4. MINOR: no exFAT fixture in the test bed. Fixed: a provisioned fixed exFAT volume (Outcome 6).
5. NIT: "over 260" vs "260 or more". Fixed.
6. NIT: the spelling the length rule measures, and a walk that sees long names. Fixed: step 8.
7. NIT: the in-use query must cover folders too. Fixed: Outcome 3.
