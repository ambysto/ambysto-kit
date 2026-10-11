# Plan — Ambysto Kit: the merged suite bar (APS-13)

Intent: [`2026-10-11-kit-suite-bar.md`](2026-10-11-kit-suite-bar.md) (approved 2026-10-11 with five owner decisions). Self-contained: an implementing agent reads this file and the intent only. Not yet accepted by the owner; an independent review follows before that.

**Sources** (copied, then generalised here):
- Optimer `ambysto/ambysto-optimer@549da79` `macos/Sources/`: `OptimerCore/Floating/FloatingLayout.swift` (placement, edge-aware resize, clamping) with `Tests/OptimerCoreTests/FloatingAndDockTests.swift`; `AmbystoOptimer/Floating/FloatingMonitorController.swift` (non-activating `NSPanel`, all Spaces, position by top-left, hide drops the content); `AmbystoOptimer/Floating/FloatingSettings.swift` (`FloatingLook` idle alpha); `AmbystoOptimer/Views/Theme.swift` (violet, coral, ink, rating colours, card metrics).
- Steady's floating monitor (`steady/apple/Steady/FloatingMonitor/`) is the origin of Optimer's controller; nothing extra is taken from it in this plan.

## Architecture

```
Sources/
  AmbystoSuite/              the contract; Foundation + Darwin only, no UI, no license
    SuiteCatalog.swift       the seven apps: id, bundle id, display name, suite order, participates (v1), canHost
    Glance.swift             GlanceRecord, Metric, Level; JSON codec; validator (spec limits)
    SuiteFolder.swift        where the shared folder is (Application Support now, App Group later); file names
    GlanceStore.swift        write own record (atomic, only on change); read all; fresh / stale / stopped
    HostLock.swift           flock on host.lock; try, hold, release; the kernel frees it on crash
    SuiteSettings.swift      chosen menu bar values, floating bar visible / expanded / top-left, status item position
    SuiteState.swift         pure rules: is the suite readout active, should this app try to host, does its own icon show
  AmbystoUI/                 brand and views; AppKit + SwiftUI; depends on AmbystoSuite
    Theme.swift              brand tokens (from Optimer), level colours + glyphs
    FloatingLayout.swift     from Optimer, unchanged API
    FloatingPanel.swift      Optimer's controller generalised: any SwiftUI content, a defaults key or a settings sink
    SuiteParticipant.swift   the one object an app creates (see below)
    SuiteHost.swift          NSStatusItem + popover + merged floating bar, alive only while this app holds the lock
    SuiteViews.swift         status item label, popover rows, merged floating bar, "Ambysto apps" menu, settings section
  SuiteBarDemo/              executable: a fake participant for proofs; no app depends on it
Tests/
  AmbystoSuiteTests/, AmbystoUITests/
docs/spec/suite-bar.md, docs/spec/suite-bar-vectors.json
scripts/suite-bar-takeover.sh, scripts/suite-bar-cpu.sh
```

`AmbystoSuite` depends on nothing; `AmbystoUI` depends on `AmbystoSuite`. Neither depends on `AmbystoLicense` (decision 3: Free), `AmbystoProcess` or `AmbystoSafety`. Purely additive for existing users of the Kit.

### The contract (spec `suite-bar.md`, the part Windows reimplements)

**Folder.** `~/Library/Application Support/Ambysto/Suite/` for every app signed outside the Mac App Store (decision 2). When a team id exists, `~/Library/Group Containers/<team id>.com.ambysto.suite/`; `SuiteFolder` picks the group container when the app has the entitlement, else the plain folder. Moving folders is a later minor release with a one-time read of the old folder; the record format does not change. Layout:

```
glances/<app id>.json     one file per app, written only by that app
host.lock                 empty file; the host holds an exclusive flock on it
settings.json             suite choices, written only by the current host
```

**Glance record** (JSON, UTF-8, at most 2 KB):

| Field | Rule |
|---|---|
| `contract` | integer, `1` |
| `app` | an id from the catalog (`optimer`, `steady`, `space`, `devbox`, `power`, `tune`, `guard`) |
| `hosting` | `true` while this app holds the host lock and shows the readout |
| `state` | `running` or `stopped` (written on a clean quit; no file is ever deleted) |
| `at` | seconds since 1970, when written |
| `ttl` | seconds, 3 to 60 |
| `level` | `ok`, `notice` or `alert`: the app's overall state |
| `metrics` | 0 to 4 entries: `id` (`[a-z0-9-]{1,24}`), `label` (1–16 printable characters, no `/` or `\`), `value` (finite number or a state word of 1–12 letters), `unit` (0–6 characters), `level` |

The validator rejects a record that breaks any rule (the host skips it; the publisher refuses to write it and logs once). No pid is published: nothing in the suite ever signals another app. Vectors in `suite-bar-vectors.json`: valid records, and one invalid record per rule (path in a label, five metrics, `ttl` 0, unknown app, NaN, over 2 KB).

**Freshness.** A `running` record is fresh while `now − at ≤ ttl`. Publishers rewrite at least every `ttl / 2` even when nothing changed (a heartbeat), and otherwise only when a value changes. A stale or `stopped` record means that app is not running for the suite; its row disappears from the popover after one more refresh and is never shown with its last numbers.

**Participants (v1).** `optimer`, `devbox`, `power`, `guard` participate and can host. `steady` participates when it has the App Group. `space` and `tune` run only while open and do not participate in v1 (intent §1).

**Active.** The suite readout is active when at least **two** participants have a fresh `running` record. One app alone behaves exactly as before (criterion 4).

**Host.** While the readout is active, every participant tries `flock(LOCK_EX | LOCK_NB)` on `host.lock` once a second until one holds it; the holder is the host and keeps the descriptor open for as long as it hosts. A clean quit unlocks first; a crash frees the lock in the kernel. When the readout becomes inactive the host unlocks and removes its status item. Takeover is bounded by the one-second retry (criterion 1: within 2 s).

**Own icon** (decision 4). A participant shows its own menu bar icon when the readout is not active, or when the user turned on "Show my own menu bar icon" in that app, or when the readout is active but no fresh record has said `hosting: true` for 5 s (the lock is held by something that is not a participant). The host's own icon follows the same rule.

**Open.** A row's **Open** posts the distributed notification `com.ambysto.suite.open` with the target app id as its `object` and no `userInfo` (allowed from a sandbox). The target opens its main window; for popover-only apps that window is added in their adoption.

**Changes.** The host watches `glances/` with a vnode source (atomic writes are renames into the directory) and also re-reads every 5 s to expire stale records. It renders only when a record changes.

### Swift API (what an app writes)

```swift
@MainActor let suite = SuiteParticipant(app: .optimer, onOpen: { openDashboard() })
suite.publish(level: .ok, metrics: [.init(id: "mem", label: "Memory", value: 62, unit: "%", level: .ok),
                                     .init(id: "cpu", label: "CPU", value: 18, unit: "%", level: .ok)])
MenuBarExtra(isInserted: $suite.showsOwnIcon) { … } label: { … }   // existing scene, one binding added
SuiteSettingsSection(suite: suite)                                 // in the app's Settings
```

`SuiteParticipant` owns the store, the lock attempts, the host (created and torn down as the lock comes and goes), the open observer and `showsOwnIcon`. `publish` coalesces to one write per change. On `NSApplication.willTerminateNotification` it writes `stopped` and unlocks.

### Views

- **Status item:** autosave name `AmbystoSuite`; the Ambysto mark plus up to two chosen values (default: Optimer's first metric when Optimer runs, else the first metric of the first participant in suite order, else the mark alone). Tint by the worst fresh level; `notice` and `alert` also add a glyph, so colour is never the only signal (criterion 8).
- **Popover:** one row per fresh participant in suite order: name, metrics, Open; a "Show in menu bar" picker for the two values; "Show floating bar"; the "Ambysto apps" menu (installed apps open through Launch Services; others listed only when a link exists, never in the readout).
- **Merged floating bar:** `FloatingPanel` with the same rows compact (one line per app, collapsed) or full (expanded); off by default like both existing bars; one suite hotkey is out of this plan (a global hotkey needs a registration API the Kit does not have yet; listed under Out).
- **Positions across hosts:** the floating bar's top-left lives in `settings.json`. The status item's position is stored by macOS in the host's own defaults (`NSStatusItem Preferred Position AmbystoSuite`); the host copies it from `settings.json` into its defaults before creating the item and copies it back when it changes. This relies on an undocumented key; if a spike shows it does not hold, the item may move once per host change and criterion 7 records that.

## Files (in build order)

Branch `aps-13-suite-bar` (this PR carries the intent and plan; the build goes on `aps-13-suite-bar-build` → its own PR); one commit per group; Co-Authored-By trailer.

### 1. Spec — `docs/spec/suite-bar.md`, `docs/spec/suite-bar-vectors.json` (main session)
The contract above, platform-neutral, macOS notes at the end. Windows notes: `%LOCALAPPDATA%\Ambysto\Suite\`, `LockFileEx` for the lock, `ReadDirectoryChangesW` for the watch, a named event for Open, one notification-area icon (the same clutter problem). Vectors as listed.

### 2. Package — `Package.swift` (builder)
Products `AmbystoSuite`, `AmbystoUI`; their targets and test targets; executable target `SuiteBarDemo` (depends on `AmbystoUI`). No new dependencies.

### 3. `AmbystoSuite` (main session; HARD: it is the cross-app contract)
- `Glance` codec and validator driven by the vectors file (a test loads `suite-bar-vectors.json` from the spec folder through `#filePath`).
- `GlanceStore`: atomic write (`Data.write(options: .atomic)`), skip identical, heartbeat; read all, classify fresh / stale / stopped / invalid with an injected clock.
- `HostLock`: `open(O_RDWR | O_CREAT, 0600)` then `flock`; a test holds it in one descriptor and fails to take it in a second; closing the first frees it.
- `SuiteState`: pure functions with table tests: active with 0, 1, 2, 3 fresh participants; stale and stopped do not count; Space and Tune never count in v1; own icon for host and non-host, with and without the user's opt-in.
- `SuiteSettings` codec; unknown keys kept, so an older host does not drop a newer host's settings.

### 4. `AmbystoUI` (builder, then reviewer)
- `Theme` from Optimer, plus level colours and glyphs. `FloatingLayout` copied with its tests. `FloatingPanel` from Optimer's controller with the content and the position sink injected.
- `SuiteParticipant`, `SuiteHost`, `SuiteViews` as above; all `@MainActor`; `SuiteParticipant` testable with an injected folder (temp directory) and clock.
- Tests: two participants in one test process with separate folders' handles sharing one temp suite folder → exactly one hosts; the host's lock released → the other hosts within 2 s; `showsOwnIcon` flips with activity and with the opt-in; a stale record removes the row from the popover model.

### 5. Demo and proof scripts (builder, then reviewer)
- `SuiteBarDemo --app devbox --metric "Servers=3" --level ok [--folder <path>]`: an accessory app that publishes, hosts when elected, logs one JSON line per transition (`hosting`, `not hosting`, `own icon shown/hidden`) with a timestamp. The catalog accepts demo runs only with `--folder` pointing outside the real suite folder, so demos never touch a user's suite.
- `scripts/suite-bar-takeover.sh`: three demos on a temp folder; quit the host, then `kill -9` the next host (the script, not the library, sends signals); from the logs, at every moment at most one host and never zero for more than 2 s; three runs (criteria 1, 3).
- `scripts/suite-bar-cpu.sh`: host with three demos for 60 s; `top -l` samples of the host pid; average below 0.5 % (criterion 6).

### 6. `CLAUDE.md` (main session)
New libraries in "What it is"; the contract under HARD; `AmbystoUI` views under MEDIUM; the new grep rule; new test count.

## Risks

| Risk | Catch |
|---|---|
| Two hosts at once (two status items) | One kernel lock; hosting is tied to holding the descriptor; takeover test with quit and `kill -9`. |
| No host for a while after a crash | One-second retry; criterion 1 measured by the script. |
| A frozen number shown as live | TTL + heartbeat; stale rows removed; test with a stopped publisher. |
| Private data leaks through the shared folder (any local process can read it) | Numbers and states only, enforced by the validator on both sides; vectors with a path and a name. |
| A local process writes fake records or holds the lock | Only catalog ids are shown, records are validated, file owner must be the current user; a squatter on the lock only hides the readout: with no fresh `hosting: true` record for 5 s, every app shows its own icon again. |
| An app's whole UI disappears with its icon | Open notification + the app's window entry, required in every adoption ticket (intent §3). |
| Status item jumps when the host changes | Copy of the preferred-position key; spike first; criterion 7 records the result. |
| Wakeups from polling | Vnode watch, writes only on change, 5 s expiry tick; criterion 6. |
| The libraries gain deletion, spawning or signals | Grep on `Sources/AmbystoSuite` and `Sources/AmbystoUI`: no `removeItem`, `trashItem`, `unlink`, `rmdir`, `rename(`, `kill(`, `killpg`, `Process(`, `posix_spawn`, `NSTask`. Atomic writes go through `Data.write(options: .atomic)`. |
| Contract drift between macOS and Windows | Vectors file shared; spec names no Swift types. |
| Steady never joins (no team id for a long time) | Decision 2 accepts that; the folder switch is designed now (`SuiteFolder`). |

## Proof

1. `swift build -c release`: 0 warnings, whole Kit (criterion 10).
2. `swift test`: all pass, old and new (criteria 3, 5, 10).
3. `scripts/suite-bar-takeover.sh`: one host at a time, never none for more than 2 s, three runs (criteria 1, 3).
4. `scripts/suite-bar-cpu.sh` below 0.5 % (criterion 6; wakeups added when Kit 0.3 lands).
5. Owner-run with demos: screenshots of one suite item, the popover, the floating bar; restart and host change keep choices and positions (criteria 2 with demo values, 7); Accessibility Inspector pass (criterion 8).
6. Grep: the list above is empty in the two new libraries; specs name no Swift types outside macOS notes (criterion 9).
7. Criteria 2 and 4 with real apps: recorded when the first two apps (Optimer, DevBox) adopt, as Kit 0.2's proof 7 was.
8. Independent review of the contract and the code before it is called done.

## Out of this plan

Each app's adoption (its own ticket and repo: what it publishes, the `isInserted` binding, its window entry); Steady's App Group; the suite global hotkey; App Intents; the Windows build; tags (they follow the license release, `v0.2.0` first).
