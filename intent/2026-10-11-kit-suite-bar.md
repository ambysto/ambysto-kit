---
status: building   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit: the merged suite bar (one readout for the whole suite)

Ticket: APS-13 (`ambysto/ambysto-kit#19`, Plane APS-13), part of APS-2 (#5). Follows suite decision 8 (2026-10-11): the suite grows by features, not apps; a Stats-style readout and Bartender's problem become this Kit feature.

## Problem

The suite intent promises a suite-only perk: "when two or more Ambysto apps are installed, their floating mini bars merge into one machine-health bar". Nothing of it exists, and the apps are drifting the other way (survey of the seven repos, 2026-10-11):

- **Four menu bar icons already** (Optimer, DevBox, Power, Guard), each its own `MenuBarExtra`. Only Optimer lets the user shrink its item; no app lets the user hide it. With the full suite installed the menu bar is crowded by us, which is the problem Bartender and Hidden Bar are bought to fix.
- **Only Optimer shows live numbers** in the menu bar (memory %, every 2 s). Steady's ping and throughput live only in its floating bar; Power's and DevBox's icons show a state, not a value.
- **Two near-identical floating panels** (Optimer's `FloatingMonitorController` says "Pattern from Ambysto Steady's"), each with its own position and hotkey (Optimer ⌃⌥M global, Steady ⌥⌘M in its menu, Guard ⌃⌥G).
- **No app knows its siblings** except Space, which looks up Optimer's bundle id. Brand tokens are copied into five apps.

## Outcome

One Ambysto readout for the whole machine, fed by every running Ambysto app, shown in one place. With one app installed nothing changes.

### 1. The glance contract (platform-neutral, `docs/spec/suite-bar.md`)

- Each running app publishes one small **glance record**: app id, version of the contract, a timestamp, a time-to-live, an overall level (`ok`, `notice`, `alert`) and **at most four metrics** (short label, value, unit, level).
- **Glances carry numbers and states only**: never a path, a process or app name, a host, a port owner or anything else a user would consider private. The publisher validates every record against the limits and drops one that breaks them.
- A record older than its time-to-live is **stale**: its row says "not running" or "no data", never a frozen number.
- What each app publishes is decided in that app's own adoption ticket. The suite intent's first examples: memory and CPU from Optimer; ping and loss from Steady; keep-awake state and battery from Power; listening servers count from DevBox; blocked-connection count from Guard. Space and Tune run only while open and publish nothing in this version (free space stays Optimer's).

### 2. One host, elected

- When **two or more** Ambysto apps are running, exactly one of them **hosts** the suite readout; the others publish only. The host is chosen by a lock in the shared suite folder; when the host quits or crashes, the lock frees and another running app takes over.
- The menu bar position, the floating bar position and the user's choices live in the shared suite folder, not in the host's own defaults, so nothing jumps when the host changes.

### 3. Two views of the same readout

- **Menu bar item:** one Ambysto icon plus at most two live values the user picks (default: the first metric of the first app in suite order), tinted by the worst level among fresh glances. Its popover has one row per running app with its metrics and an **Open** button.
- **Merged floating mini bar:** the same rows, compact, in the floating panel framework extracted from Optimer and Steady (non-activating, all Spaces, edge-aware expand). One suite hotkey replaces the apps' own floating-bar hotkeys while a host exists.
- **Each app's own menu bar icon becomes optional:** while a suite readout is shown, the other apps hide their icon by default and offer "Show my own menu bar icon" in Settings. An app whose whole UI lives in its menu bar popover (Optimer, DevBox, Power, Guard) opens as a window from **Open** when its icon is hidden; that change is part of each app's adoption.

### 4. Pieces the bar needs from the rest of APS-2

- **Suite discovery:** which Ambysto apps are installed (bundle ids in the spec), which are running, open one. Apps that are not installed never appear in the readout; they appear only in the "Ambysto apps" menu (the suite intent: cross-sell at the right moment, never an ad).
- **Brand tokens** (violet `#6B5CE8`, coral `#ED708F`, ink `#1C1A33`, rounded numerals) and the **floating panel framework**, moved into the Kit so the bar looks the same from every host.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | With two or more apps running, exactly one Ambysto suite item is in the menu bar; quitting or killing the host leaves exactly one item again within 2 s, never zero for longer and never two | demo publishers, then Optimer + DevBox; scripted quit and `kill` of the host, three runs |
| 2 | A value in the suite readout equals the app's own readout within one refresh interval | side-by-side screenshots, three samples |
| 3 | An app that stops publishing shows "not running" or "no data" once its time-to-live passes; no frozen number is shown | test with a stopped publisher |
| 4 | With one Ambysto app installed, that app looks and behaves exactly as before | screenshots before and after adopting the Kit |
| 5 | A record with a path, more than four metrics or an over-long label is rejected by the validator; the spec lists the limits | unit tests from spec vectors |
| 6 | The host adds less than 0.5 % CPU averaged over 60 s on Apple Silicon, and no wakeups while the readout is hidden beyond its refresh timer | Activity Monitor / `top` sample, with Power's idle-wakeup reading once Kit 0.3 lands |
| 7 | "Show my own menu bar icon", the chosen values and the positions persist across restarts and host changes | manual proof, recorded |
| 8 | Every element has a VoiceOver label; levels are not shown by colour alone | Accessibility Inspector pass |
| 9 | The spec names no Swift types outside its macOS notes; Windows notes cover the notification area (the same clutter problem) | grep |
| 10 | `swift build -c release` with 0 warnings, Swift 6 strict concurrency, `swift test` green | build |

## Constraints

- Swift 6, macOS 14+, public Apple APIs only, no third-party dependencies, no network, no telemetry.
- **Signing reality:** every app except Steady is ad-hoc signed today, and Steady ships sandboxed on the Mac App Store. There is no shared team id yet, so App Groups and team-checked XPC are not available until the Developer ID (AO-6). The transport must work without them now and switch without changing the contract.
- Sandboxed apps cannot attach a payload to a distributed notification and cannot write outside their container without an App Group. A "changed" notification without payload is fine; the data goes through the shared folder.
- Anything another local process can read must be harmless: hence glances carry numbers and states only.
- The Kit's existing rules hold: the libraries never delete user files, never spawn processes and never send signals. Opening a sibling app goes through Launch Services, the same as clicking it in Finder.
- The contract is a cross-app contract (suite decision 7): it lives in the Kit spec and changes only with a Kit version bump. Kit 0.3 is taken by APS-12; this lands as the next minor after it (0.4 unless the order changes). Apps adopt it by semver tag once tags exist.
- Everything on GitHub in English; feature branch and PR; tickets synced on GitHub and Plane; this repo is public.

## Out of scope

- Adopting the bar in each app: one ticket per app, in that app's repo, each deciding what it publishes and adding a window entry where its UI is popover-only.
- The Windows version (notification area), built later from the spec.
- App Intents and the Raycast extension (suite decision 8; a separate Kit ticket).
- Shared Settings sections beyond the suite bar's own.
- Showing apps that are not installed anywhere in the readout.

## Decisions (owner, 2026-10-11)

1. **Surface: both views** of one readout, the menu bar item and the merged floating bar.
2. **Build now, before the Developer ID,** on a shared folder in the user's Application Support, for the six apps signed outside the Mac App Store. Steady joins when the team id gives it an App Group; the contract does not change then, only the folder.
3. **Free:** the merged readout works for any two installed Ambysto apps, Free or Pro.
4. **The other apps' icons are hidden by default** while the suite readout is shown; each app offers "Show my own menu bar icon".
5. **Guard follows the suite rule** like every other app; its blocking prompts keep working without an icon.

## Open questions

_None: the owner decided all five on 2026-10-11._
