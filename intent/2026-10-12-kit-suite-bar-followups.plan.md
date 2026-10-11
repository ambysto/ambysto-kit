# Plan — Ambysto Kit: suite bar follow-ups (APS-15)

Intent: [`2026-10-12-kit-suite-bar-followups.md`](2026-10-12-kit-suite-bar-followups.md) (approved 2026-10-12). Self-contained: an implementing agent reads this file and the intent only. Not yet accepted by the owner; an independent review follows before that.

## Design

### 1. Stale data (`SuiteParticipant`)
- New init parameter `staleAfter: TimeInterval? = nil` (stored; also settable). Effective value: `max(staleAfter ?? 3 × ttl, ttl)` seconds, where `ttl` is the last `publish` call's ttl.
- `publish(...)` records `lastPublishAt = clock()`.
- `writeIfDue` builds the record from `last`; when `clock() − lastPublishAt > effectiveStaleAfter`, it uses **`metrics: []`** and keeps `last.level`. The heartbeat schedule sees a content change and writes at once; the next `publish` restores the metrics (another content change, written at once).
- `suspendPublishing()` (proofs) keeps its meaning: no writes at all.
- The state machine and the readout need no change: a fresh record without metrics is already a "running" row with no numbers.

### 2. Reopen
- `public func handleReopen() -> Bool` on `SuiteParticipant`: when `!showsOwnIcon` it calls the `onOpen` action the app gave at init (the app's own "show my main window") and returns `false` (the app handled it); otherwise it does nothing and returns `true` (AppKit's default). An app calls it from `applicationShouldHandleReopen`. Optimer's existing behaviour is exactly this; DevBox, which opens its window always, may keep its own handler.
- Spec: a new section "Adopting the suite bar" lists the adopter's obligations (fixed labels, numbers and states only; own icon bound to `showsOwnIcon`; Open opens the main window; **reopen shows the main window while the own icon is hidden**; own floating bar and shortcut give way while active; a `staleAfter` that fits the app's refresh rhythm).

### 3. The shortcut's status
- `GlobalHotKey` keeps `RegisterEventHotKey`'s status: `public private(set) var isRegistered: Bool` (`status == noErr`). A test seam: an internal initialiser that takes the register function (`(UInt32, UInt32, EventHotKeyID, EventTargetRef?, OptionBits, UnsafeMutablePointer<EventHotKeyRef?>) -> OSStatus`), default Carbon's.
- `SuiteHost`: after creating the key, if `!isRegistered`, log once ("⌃⌥M is in use by another app") and set an observable `shortcutAvailable = false` on the participant; it retries (invalidate + create) each time the suite popover opens, at most once per opening.
- `SuitePopover`: under "Show floating bar", when `!shortcutAvailable`, a caption "⌃⌥M is in use by another app." (accessibility label included).

## Files (in build order)
Branch `aps-15-suite-bar-followups` (this PR holds the intents and plans; the build goes on `aps-15-build`); one commit per group.

1. **Spec** `docs/spec/suite-bar.md` (main session; HARD): "Freshness" gains the stale rule (default 3 × ttl, never below ttl, level kept, metrics dropped); new "Adopting the suite bar" section; "The readout" gains the shortcut note. No vector changes (a record without metrics is already in the vectors).
2. **`AmbystoUI`** (main session; the stale rule is contract behaviour): `SuiteParticipant` (`staleAfter`, `lastPublishAt`, stale branch in `writeIfDue`, `handleReopen`, `shortcutAvailable`), `GlobalHotKey` (`isRegistered`, seam), `SuiteHost` (log once, retry on popover open), `SuiteViews` (caption).
   Tests (`Tests/AmbystoUITests`, injected clock, fake status items, `observeSystem: false`): stale after 3 × ttl with the default and after an explicit value; a value below ttl raised to ttl; level kept, metrics empty, back to metrics on the next publish; `handleReopen` calls `onOpen` only while the own icon is hidden; `GlobalHotKey` with a fake register function returning `eventHotKeyExistsErr` → `isRegistered == false`; the host sets `shortcutAvailable = false` and retries on the next popover opening.
3. **CLAUDE.md** (EASY): the adoption checklist gains reopen and `staleAfter`; test count.

## Proof
1. `swift build -c release` 0 warnings (clean scratch path); `swift test` green (criteria 1, 2, 4 unit parts, 6).
2. Optimer (`origin/main`) and DevBox (`origin/main`) build unchanged against the branch head, in temporary worktrees with a local path dependency named `ambysto-kit` (criterion 3).
3. Manual (owner or main session): run the old Optimer build from `ambysto-optimer/dist` (it registers ⌃⌥M with its own signature) together with two demos; the hosting demo logs the failure once and its popover shows the caption (criterion 4).
4. Grep: the suite bar list at zero hits in `AmbystoSuite` and `AmbystoUI`; spec names no Swift types outside macOS notes; vectors unchanged (criterion 5).
5. Independent review before it is called done.

## Risks
| Risk | Catch |
|---|---|
| A slow-refresh app shows no numbers between refreshes | Default 3 × ttl; DevBox sets 150 s; the adoption section says to fit `staleAfter` to the refresh rhythm |
| Flapping between metrics and none | Only `publish` restores metrics; stale only after a full `staleAfter` without one |
| Carbon status differs across processes vs in-process duplicates | Unit test through the seam; manual check with a real second process |
| Retrying the key every popover opening registers twice | Invalidate before re-creating; at most once per opening |
| Additive API breaks an adopter | Defaulted parameters; proof 2 |

## Out of this plan
Removing DevBox's watchdog and adding Optimer's retry (their follow-up PRs); detecting an item hidden by macOS; Windows.
