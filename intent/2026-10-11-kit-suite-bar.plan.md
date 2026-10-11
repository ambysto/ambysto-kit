# Plan — Ambysto Kit: the merged suite bar (APS-13)

Intent: [`2026-10-11-kit-suite-bar.md`](2026-10-11-kit-suite-bar.md) (approved 2026-10-11 with five owner decisions). Self-contained: an implementing agent reads this file and the intent only. Revised after an independent technical review (1 blocker, 7 major, 4 minor — all folded in; log at the end). Not yet accepted by the owner.

**Sources** (copied, then generalised here):
- Optimer `ambysto/ambysto-optimer@549da79` `macos/Sources/`: `OptimerCore/Floating/FloatingLayout.swift` (placement, edge-aware resize, clamping) with `Tests/OptimerCoreTests/FloatingAndDockTests.swift`; `AmbystoOptimer/Floating/FloatingMonitorController.swift` (non-activating `NSPanel`, all Spaces, position by top-left, hide drops the content); `AmbystoOptimer/Floating/FloatingSettings.swift` (`FloatingLook` idle alpha); `AmbystoOptimer/Floating/GlobalHotKey.swift` (24-line Carbon `RegisterEventHotKey` wrapper); `AmbystoOptimer/Views/Theme.swift` (violet, coral, ink, rating colours, card metrics).
- Steady's floating monitor (`steady/apple/Steady/FloatingMonitor/`) is the origin of Optimer's controller; nothing extra is taken from it in this plan.

## Architecture

```
Sources/
  AmbystoSuite/              the contract; Foundation + Darwin only, no UI, no license
    SuiteCatalog.swift       the seven apps: id, bundle id, display name, suite order, participates (v1), canHost
    Glance.swift             GlanceRecord, Metric, Level, state words; JSON codec; validator (spec limits)
    SuiteFolder.swift        the shared folders this app can reach (Application Support now, App Group later); file names
    GlanceStore.swift        write own record (atomic, only on change + heartbeat); safe read of all; fresh / stale / stopped
    HostLock.swift           blocking flock on host.lock in a dedicated thread; inode check; O_CLOEXEC
    SuiteSettings.swift      chosen menu bar values, floating bar visible / expanded / top-left, status item position
    SuiteState.swift         pure rules with hysteresis: active, own icon, own floating bar
  AmbystoUI/                 brand and views; AppKit + SwiftUI; depends on AmbystoSuite
    Theme.swift              brand tokens (from Optimer), level colours + glyphs
    FloatingLayout.swift     from Optimer, unchanged API
    FloatingPanel.swift      Optimer's controller generalised: any SwiftUI content, a position sink
    GlobalHotKey.swift       from Optimer
    SuiteParticipant.swift   the one object an app creates (see below)
    SuiteHost.swift          status item + popover + merged floating bar + suite hotkey, only while this app hosts
    SuiteViews.swift         status item label, popover rows, merged floating bar, "Ambysto apps" menu, settings section
  SuiteBarDemo/              executable: a fake participant for proofs; no app depends on it
Tests/
  AmbystoSuiteTests/, AmbystoUITests/
docs/spec/suite-bar.md, docs/spec/suite-bar-vectors.json
scripts/suite-bar-takeover.sh, scripts/suite-bar-cpu.sh
```

`AmbystoSuite` depends on nothing; `AmbystoUI` depends on `AmbystoSuite`. Neither depends on `AmbystoLicense` (decision 3: Free), `AmbystoProcess` or `AmbystoSafety`. Purely additive for existing users of the Kit.

### The contract (spec `suite-bar.md`, the part Windows reimplements)

**Folders.** `~/Library/Application Support/Ambysto/Suite/` for every app signed outside the Mac App Store (decision 2). When a team id exists, `~/Library/Group Containers/<team id>.com.ambysto.suite/` is added. Rules that keep one host across the switch: an app **publishes into every suite folder it can reach and hosts only while it holds `host.lock` in every one of them**; Steady (sandboxed, group folder only) never hosts (`canHost = false`: it is a window app). Layout of each folder:

```
glances/<app id>.json     one file per app, written only by that app
host.lock                 empty file; the host-elect holds an exclusive flock on it
settings.json             suite choices, written only by the current host
```

**Glance record** (JSON, UTF-8, at most 2,048 bytes):

| Field | Rule |
|---|---|
| `contract` | integer major version, `1`. A reader skips a record with a higher major and ignores fields it does not know (later minors add fields only). |
| `app` | an id from the catalog (`optimer`, `steady`, `space`, `devbox`, `power`, `tune`, `guard`); must match the file name |
| `hosting` | `true` while this app holds the lock and its suite item is shown |
| `state` | `running` or `stopped` (written on a clean quit; no file is ever deleted) |
| `at` | seconds since 1970 as a JSON number, when written |
| `ttl` | integer seconds, 3 to 60 |
| `level` | `ok`, `notice` or `alert`: the app's overall state |
| `metrics` | 0 to 4 entries, **in display order**, unique `id`s: `id` (`[a-z0-9-]{1,24}`), `label` (1–16 Unicode scalars, no control characters, no bidi format characters, no `/` or `\`), `value` (a finite JSON number with at most 2 decimals, or a **state word** from the fixed list `on, off, active, idle, paused, blocked, allowed, ok, warning, error, none`), `unit` (0–6 scalars, same character rules), `level` |

The validator rejects a record that breaks any rule (the host skips it; the publisher refuses to write it and logs once). No pid is published: nothing in the suite ever signals another app. **Labels are fixed strings in each app's code, in the app's language; the host formats numbers with the user's locale.** No validator can recognise an app or process name in a label, so the rule "numbers and states only, never names" is enforced by the closed state-word list plus the review of each app's adoption (a checklist item in every adoption ticket). Vectors in `suite-bar-vectors.json`: valid records (0 and 4 metrics, a state word, a minor-version extra field); one invalid record per rule (path in a label, a label of 17 scalars, five metrics, duplicate metric ids, a free state word such as `Xcode`, `ttl` 0, unknown app, app not matching the file name, NaN, a bidi override, over 2,048 bytes, `contract` 2 skipped).

**Reading safely.** A reader accepts only files named `<catalog id>.json` (temp files from atomic writes and anything else are ignored), opens them with `O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC`, requires a regular file owned by the current user, and reads at most 2,049 bytes (more = invalid).

**Freshness.** A `running` record is fresh while `at − 2 ≤ now ≤ at + ttl`; a record from the future (clock set back, more than 2 s) is stale. Publishers rewrite at least every `ttl / 2` even when nothing changed (a heartbeat), only otherwise when a value changes, and immediately on wake from sleep (`NSWorkspace.didWakeNotification`). A stale or `stopped` record means that app is not running for the suite: its row shows **"not running"** with no numbers (intent §1) for 60 s, then disappears.

**Participants (v1).** `optimer`, `devbox`, `power`, `guard` participate and can host. `steady` participates when it has the App Group and never hosts. `space` and `tune` run only while open and do not participate in v1 (intent §1).

**Active, with hysteresis.** The readout becomes active when at least **two** participants have a fresh `running` record, and becomes inactive only after fewer than two have been fresh for **10 s** continuously; for 10 s after wake nothing turns inactive. This keeps the TTL boundary, sleep and App Nap from making icons flicker, and gives every app the same answer within that margin.

**Host election.** Every participant that can host keeps one dedicated thread blocked in `flock(fd, LOCK_EX)` on `host.lock` (opened `O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC`, mode 0600) for as long as it runs. After it acquires the lock it compares the descriptor's device + inode with the path's; if the file was replaced it closes and starts over. The holder is the **host-elect**; it shows the suite item only while the readout is active. Takeover is immediate when the holder quits or crashes (the kernel releases the lock), with no polling in the other apps. A clean quit removes the status item, publishes `stopped`, then closes the descriptor, in that order, so two items never show.

**Own icon** (decision 4). A participant shows its own menu bar icon when the readout is not active, or when the user turned on "Show my own menu bar icon" in that app, or when the readout is active but no fresh record has said `hosting: true` for 5 s (something that is not a participant holds the lock, or the host's item cannot be shown). The host's own icon follows the same rule. **Own floating bar:** while the readout is active, `SuiteParticipant.isActive` is `true` and the app hides its own floating bar and releases its own floating-bar hotkey (part of each adoption); the merged bar and the suite hotkey replace them.

**Open.** A row's **Open** posts the distributed notification `com.ambysto.suite.open` with the target app id as its `object`, no `userInfo`, `deliverImmediately: true`; participants observe it with `suspensionBehavior: .deliverImmediately` (a menu bar app is never frontmost, so the default behaviour would hold the notification). The target opens its main window; for popover-only apps that window is added in their adoption. If the first spike (group 1) shows the notification is not delivered reliably, Open uses Launch Services instead (`openApplication` on the running app's URL, which sends a reopen event) and the spec changes before any code depends on it.

**Changes.** The host watches `glances/` with a vnode source (atomic writes are renames into the directory) and re-reads every 5 s to expire stale records. It renders only when a record changes.

### Swift API (what an app writes)

```swift
@MainActor let suite = SuiteParticipant(app: .optimer, onOpen: { openDashboard() })   // reads the folder synchronously
suite.publish(level: .ok, metrics: [.init(id: "mem", label: "Memory", value: 62, unit: "%", level: .ok),
                                     .init(id: "cpu", label: "CPU", value: 18, unit: "%", level: .ok)])
MenuBarExtra(isInserted: $suite.showsOwnIcon) { … } label: { … }   // existing scene, one binding added
SuiteSettingsSection(suite: suite)                                 // in the app's Settings
```

`SuiteParticipant` owns the store, the lock thread, the host (created and torn down as the lock and activity change), the open observer, `isActive` and `showsOwnIcon`. Its first `showsOwnIcon` is computed in `init` from the folder, before the first scene, so an icon never flashes. **When the system writes `false` into the binding** (the user dragged the icon out with ⌘), that is stored as the user turning "Show my own menu bar icon" off. `publish` coalesces to one write per change. On `NSApplication.willTerminateNotification` it runs the clean-quit order above.

### Views

- **Status item:** autosave name `AmbystoSuite`; the Ambysto mark plus up to two chosen values (default: Optimer's first metric when Optimer runs, else the first metric of the first participant in suite order, else the mark alone). Tint by the worst fresh level; `notice` and `alert` also add a glyph, so colour is never the only signal (criterion 8). Created through an injectable factory, so unit tests never need a menu bar.
- **Popover:** one row per participant in suite order (fresh rows with metrics, "not running" rows without); Open; a "Show in menu bar" picker for the two values; "Show floating bar"; the "Ambysto apps" menu (installed apps open through Launch Services; others listed only when a link exists, never in the readout).
- **Merged floating bar:** `FloatingPanel` with the same rows, compact (one line per app) or expanded; off by default like both existing bars. **Suite hotkey ⌃⌥M** (Optimer's) toggles it, registered by the host only, through `GlobalHotKey` (intent §3).
- **Positions across hosts:** the floating bar's top-left lives in `settings.json`. The status item's position is stored by macOS in the host's own defaults (`NSStatusItem Preferred Position AmbystoSuite`, undocumented); the host copies it from `settings.json` into its defaults before creating the item and copies it back when it changes. A spike in group 4 checks it; if it does not hold, the item may move once per host change and criterion 7 records that.

## Files (in build order)

Branch `aps-13-suite-bar` carries the intent and plan; the build goes on `aps-13-suite-bar-build` → its own PR; one commit per group; Co-Authored-By trailer.

### 1. Spikes, then spec — `docs/spec/suite-bar.md`, `docs/spec/suite-bar-vectors.json` (main session)
Spikes first, results recorded in the spec's macOS notes: (a) distributed notification with `deliverImmediately` reaches a menu bar app that is not frontmost; (b) `MenuBarExtra(isInserted:)` removes and re-inserts a `.window`-style item, and what the binding receives when the user removes it; (c) blocking `flock` in a thread, released on `kill -9` of the holder. Then the contract above, platform-neutral, macOS notes at the end. Windows notes: `%LOCALAPPDATA%\Ambysto\Suite\`, `LockFileEx` for the lock, `ReadDirectoryChangesW` for the watch, a named event for Open, one notification-area icon (the same clutter problem). Vectors as listed.

### 2. Package — `Package.swift` (builder)
Products `AmbystoSuite`, `AmbystoUI`; their targets and test targets; executable target `SuiteBarDemo` (depends on `AmbystoUI`). No new dependencies.

### 3. `AmbystoSuite` (main session; HARD: it is the cross-app contract)
- `Glance` codec and validator driven by the vectors file (a test loads `suite-bar-vectors.json` from the spec folder through `#filePath`).
- `GlanceStore`: atomic write (`Data.write(options: .atomic)`), skip identical, heartbeat; the safe read above; classify fresh / stale (including from the future) / stopped / invalid with an injected clock. Tests: a FIFO, a symlink, an oversized file and a temp-file name in `glances/` are skipped without blocking.
- `HostLock`: tests that a second descriptor in the same process blocks until the first closes; a replaced `host.lock` is detected by the inode check; the descriptor is not inherited (`FD_CLOEXEC` set).
- `SuiteState`: pure functions with table tests: active with 0, 1, 2, 3 fresh participants; stale and stopped do not count; hysteresis (on at once, off after 10 s, nothing off for 10 s after wake); Space and Tune never count in v1; own icon for host and non-host, with and without opt-in, and the 5 s no-`hosting` fallback.
- `SuiteSettings` codec; unknown keys kept, so an older host does not drop a newer host's settings.

### 4. `AmbystoUI` (builder, then reviewer)
- `Theme` from Optimer, plus level colours and glyphs. `FloatingLayout` copied with its tests. `FloatingPanel` from Optimer's controller with the content and the position sink injected. `GlobalHotKey` from Optimer.
- `SuiteParticipant`, `SuiteHost`, `SuiteViews` as above; all `@MainActor`; testable with an injected folder (temp directory), clock and status item factory. The status-item position spike.
- Tests: two participants on one temp suite folder → exactly one hosts once both are fresh; the first closes → the second hosts at once; `showsOwnIcon` and `isActive` follow activity, hysteresis and the opt-in; a system write of `false` is stored as the opt-in turned off; a stale record turns its row into "not running", then removes it.

### 5. Demo and proof scripts (builder, then reviewer; the owner runs them in a GUI session)
- `SuiteBarDemo --folder <path> --app devbox --metric "Servers=3" --level ok`: `--folder` is **required** and must not be the real suite folder (the demo refuses it). An accessory app that publishes, hosts when elected and logs one JSON line per transition. It reads commands on stdin: `quit` (a clean `NSApp.terminate`, so `willTerminate` runs) and `freeze` (stop publishing, to test staleness).
- `scripts/suite-bar-takeover.sh`: copies the demo binary under three names (so each has its own defaults domain), runs them on a temp folder, then `quit`s the host, `kill -9`s the next host and `freeze`s a third. It **counts status-item windows of the demo pids with `CGWindowListCopyWindowInfo`** (layer of the status bar) every 100 ms: never two, never zero for more than 2 s while two or more demos are fresh; the frozen demo's row turns "not running" within `ttl` + 5 s (host log). It signals only pids it started, after checking their command name. Three runs (criteria 1, 3).
- `scripts/suite-bar-cpu.sh`: host with three demos for 60 s; `top -l` samples of the host pid and of each non-host (they no longer poll); host average below 0.5 % (criterion 6).

### 6. `CLAUDE.md` (main session)
New libraries in "What it is"; the contract and `SuiteState` under HARD; `AmbystoUI` views under MEDIUM; the new grep rule and its one exception (Launch Services opens an installed app from the "Ambysto apps" menu); new test count; proofs 3–5 are owner-run.

## Risks

| Risk | Catch |
|---|---|
| Two hosts at once (two status items) | One kernel lock per folder, held in every reachable folder to host; inode check; `O_CLOEXEC`; clean-quit order; window count in the takeover script. |
| No host for a while after a crash | Blocking `flock`: the kernel hands the lock over at once; criterion 1 measured from window counts. |
| Icons flicker at the TTL boundary, after sleep or under App Nap | 10 s hysteresis, nothing inactive for 10 s after wake, publish on wake. |
| A frozen number shown as live | TTL + heartbeat; records from the future are stale; "not running" rows have no numbers; `freeze` test. |
| Private data in the shared folder (any local process of the user can read it) | Closed state-word list, character and length rules in the validator; labels are fixed strings; names kept out by the adoption checklist (the validator cannot catch them, and the spec says so). |
| A local process writes fake records or holds the lock | Only catalog ids, matching file names, files owned by the user; a squatter only hides the readout: with no fresh `hosting: true` for 5 s every app shows its own icon again. A forged Open only brings an app's window forward. |
| Reading `glances/` blocks or reads huge files | `O_NOFOLLOW | O_NONBLOCK`, regular files only, 2,049-byte cap, name filter; tests with a FIFO and a symlink. |
| Open does nothing for a menu bar app | `deliverImmediately` on both sides; spike (a) first; Launch Services fallback decided before code. |
| An app's whole UI disappears with its icon | Open + the app's window entry, required in every adoption ticket (intent §3). |
| The user hides the suite item through macOS's own menu bar settings | The host publishes `hosting: true` only while its item reports visible; otherwise the 5 s fallback brings the apps' own icons back. Checked in spike (b) on macOS 26. |
| Status item jumps when the host changes | Copy of the preferred-position key; spike in group 4; criterion 7 records the result. |
| Wakeups | No polling in non-hosts; vnode watch; writes only on change and heartbeat; 5 s expiry tick in the host; criterion 6. |
| The libraries gain deletion, spawning or signals | Grep on `Sources/AmbystoSuite` and `Sources/AmbystoUI`, the 0.2 list plus additions: `trashItem`, `removeItem`, `moveItem`, `replaceItem`, `unlink`, `rmdir`, `remove(`, `rename(`, `recycle`, `kill(`, `killpg`, `pthread_kill`, `sigqueue`, `proc_terminate`, `terminate(`, `forceTerminate`, `Process(`, `NSTask`, `posix_spawn`, `system(`, `popen`, `NSAppleScript`. Atomic writes go through `Data.write(options: .atomic)`. The one allowed exception, `NSWorkspace.openApplication`, is recorded in CLAUDE.md. |
| Contract drift between macOS and Windows | Shared vectors file; spec names no Swift types; major/minor rule for `contract`. |
| Steady never joins (no team id for a long time) | Decision 2 accepts that; the multi-folder rule is designed now. |

## Proof

1. `swift build -c release`: 0 warnings, whole Kit (criterion 10).
2. `swift test`: all pass, old and new, with no GUI session needed (criteria 3, 5, 10).
3. `scripts/suite-bar-takeover.sh` (owner-run): window counts show one suite item at a time, never none for more than 2 s, the clean quit and the `kill -9` both covered, the frozen demo turns "not running"; three runs (criteria 1, 3).
4. `scripts/suite-bar-cpu.sh` (owner-run): host below 0.5 % (criterion 6; wakeups added when Kit 0.3 lands).
5. Owner-run with demos: screenshots of one suite item, the popover, the floating bar and the suite hotkey; restart and host change keep choices and positions (criterion 7); Accessibility Inspector pass (criterion 8).
6. Grep: the list above is empty in the two new libraries apart from the recorded exception; specs name no Swift types outside macOS notes (criterion 9).
7. **Criteria 2 and 4 need real apps** and are recorded when the first two apps (Optimer, DevBox) adopt, as Kit 0.2's proof 7 was; demos cannot prove them.
8. Independent review of the contract and the code before it is called done.

## Out of this plan

Each app's adoption (its own ticket and repo: what it publishes and the adoption checklist on names, the `isInserted` binding, its window entry, hiding its own floating bar and hotkey while `isActive`); Steady's App Group; App Intents; the Windows build; tags (they follow the license release, `v0.2.0` first).

## Review log (2026-10-11, independent technical review: 1 blocker, 7 major, 4 minor — all folded in)

1. Open could be held for a menu bar app → `deliverImmediately` on both sides, spike (a), Launch Services fallback. 2. Two hosts possible across the App Group switch, a replaced lock file, an inherited descriptor → host only while holding the lock in every reachable folder, Steady never hosts, inode check, `O_CLOEXEC`. 3. Wall-clock freshness and flapping → future records stale, 10 s hysteresis, wake grace and publish on wake, status item removed before unlock. 4. Privacy claim stronger than the validator → closed state-word list, fixed labels, adoption checklist, the spec says what the validator cannot catch. 5. Suite hotkey and own floating bars dropped without a decision → suite hotkey ⌃⌥M from Optimer's `GlobalHotKey`, `isActive` for apps to hide their own bars. 6. Proofs over-stated → status-item windows counted with `CGWindowListCopyWindowInfo`, demo `quit` and `freeze` commands, one defaults domain per demo, criteria 2 and 4 moved to real apps, over-long-label vector, injected status item, owner-run proofs named. 7. Weaker grep → 0.2 list plus additions, Launch Services exception in CLAUDE.md. 8. Unsafe reads → name filter, `O_NOFOLLOW | O_NONBLOCK`, regular files, 2,049-byte cap. 9. Spec gaps → major/minor rule, unknown fields, display order, unique ids, character rules, number format, localisation, "not running" rows as in the intent. 10. Polling election → one blocking `flock` thread per app, no polling. 11. `isInserted` semantics, first-frame flash, macOS 26 hiding → defined, computed in `init`, visibility check and spike (b). 12. Demo safety → `--folder` required and never the real folder; the script signals only its own pids.
