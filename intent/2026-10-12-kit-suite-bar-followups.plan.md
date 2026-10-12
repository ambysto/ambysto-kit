# Plan — Ambysto Kit: suite bar follow-ups (APS-15)

Intent: [`2026-10-12-kit-suite-bar-followups.md`](2026-10-12-kit-suite-bar-followups.md) (approved 2026-10-12). Self-contained: an implementing agent reads this file and the intent only. Revised after an independent technical review (1 blocker, 3 major, 5 minor — folded in; log at the end). Plan accepted by the owner on 2026-10-12 ("Merge"), with **option A** for the shortcut (the recommendation; the owner did not pick another).

## Design

### 1. Stale data (`SuiteParticipant`)
- New init parameter `staleAfter: TimeInterval? = nil` (stored; also settable). Effective value: `max(staleAfter ?? 3 × ttl, ttl)` seconds, where `ttl` is the last `publish` call's ttl.
- `publish(...)` records `lastPublishAt = clock()`.
- `writeIfDue` builds the record from the **last valid** publish (a publish the validator refused never becomes the source of a later record); when `clock() − lastPublishAt > effectiveStaleAfter`, its numbers have **expired**: it uses **`metrics: []`** and keeps the level. (The spec calls this "expired numbers", since "stale" already means a record past its ttl.)
- When an app's publish interval is longer than its `staleAfter` (numbers would blink every cycle), the participant logs it once.
- After a sleep longer than `staleAfter`, the first heartbeat on wake writes the record without numbers until the app publishes again; the spec says so and a test covers it. The heartbeat schedule sees a content change and writes at once; the next `publish` restores the metrics (another content change, written at once).
- `suspendPublishing()` (proofs) keeps its meaning: no writes at all.
- The state machine and the readout need no change: a fresh record without metrics is already a "running" row with no numbers.

### 2. Reopen
- `public func handleReopen() -> Bool` on `SuiteParticipant`, for `applicationShouldHandleReopen`: when `!showsOwnIcon` it calls the `onOpen` action the app gave at init (the app's own "show my main window") and returns `false`, so AppKit and SwiftUI do not also reopen a window; otherwise it does nothing and returns `true`, letting the system's default reopen happen. Today Optimer's handler always returns `true` and DevBox's always `false`; adopting the helper changes Optimer's return value, so its adoption PR includes a manual reopen check. DevBox, which shows its window on every reopen, may keep its own handler.
- Spec: a new section "Adopting the suite bar" lists the adopter's obligations (fixed labels, numbers and states only; own icon bound to `showsOwnIcon`; Open opens the main window; **reopen shows the main window while the own icon is hidden**; own floating bar and shortcut give way while active; a `staleAfter` that fits the app's refresh rhythm).

### 3. The shortcut — owner decision needed (the review found the premise wrong)
Carbon hot keys are **not exclusive across processes** (`CarbonEvents.h`: "Hot keys are normally non-exclusive: when the key is pressed, … the same hot key can be registered by multiple applications"; `eventHotKeyExistsErr` only for a duplicate in the same process or against a `kEventHotKeyExclusive` registrant). So when another app also uses ⌃⌥M, registration **succeeds** and one press triggers both apps; the approved "⌃⌥M is in use" note would almost never show.
- **Option A (recommended):** drop the popover note and criterion 4; record the Carbon semantics in the spec's macOS notes ("another app using ⌃⌥M receives the same press"); keep only `GlobalHotKey.isRegistered` with a one-time log for the rare exclusive case. Cheap and honest.
- **Option B:** register the suite key with `kEventHotKeyExclusive`, so a press reaches only the suite. A frozen host holding the exclusive key would then block the new host (worse than today), the hand-over with Optimer's own key needs a retry on the tick, and a spike with two processes comes first.
- **Option C:** drop outcome 3 entirely (no `isRegistered`).
**Chosen: option A** (2026-10-12). The popover note and criterion 4 are dropped; `isRegistered` with a one-time log stays; the spec's macOS notes state the Carbon semantics.

## Files (in build order)
Branch `aps-15-suite-bar-followups` (this PR holds the intents and plans; the build goes on `aps-15-build`); one commit per group.

1. **Spec** `docs/spec/suite-bar.md` (main session; HARD): "Freshness" gains the stale rule (default 3 × ttl, never below ttl, level kept, metrics dropped); new "Adopting the suite bar" section; "The readout" gains the shortcut note. No vector changes (a record without metrics is already in the vectors).
2. **`AmbystoUI`** (main session; the stale rule is contract behaviour): `SuiteParticipant` (`staleAfter`, `lastPublishAt`, the expiry branch in `writeIfDue`, `handleReopen`, the blink log), `GlobalHotKey` (`isRegistered`), `SuiteHost` (one log line per run when registration fails). *(Option A: no `shortcutAvailable`, no retry, no popover caption.)*
   Tests (`Tests/AmbystoUITests`, injected clock, fake status items, `observeSystem: false`): numbers expire after 3 × ttl with the default and after an explicit value; a value below ttl raised to ttl; level kept, metrics empty, back to metrics on the next publish; a refused publish never becomes the source of a later record; a wake after a long sleep writes no numbers until the next publish; the one-time log when the publish interval exceeds `staleAfter`; `handleReopen` calls `onOpen` only while the own icon is hidden and returns `false` then, `true` otherwise. The existing `heartbeatsAreNeverFurtherApartThanHalfTheTtl` loop is bounded below the expiry point. (Shortcut tests depend on the §3 decision.)
3. **CLAUDE.md** (EASY): the adoption checklist gains reopen and `staleAfter`; test count.

## Proof
1. `swift build -c release` 0 warnings (clean scratch path); `swift test` green (criteria 1, 2, 4 unit parts, 6).
2. Optimer (`origin/main`) and DevBox (`origin/main`) build unchanged against the branch head, in temporary worktrees with a local path dependency named `ambysto-kit` (criterion 3).
3. *(Dropped with option A: criterion 4 and its manual check.)*
4. Grep: the suite bar list at zero hits in `AmbystoSuite` and `AmbystoUI`; spec names no Swift types outside macOS notes; vectors unchanged (criterion 5).
5. Independent review before it is called done.

## Risks
| Risk | Catch |
|---|---|
| A slow-refresh app shows no numbers between refreshes | Default 3 × ttl; DevBox sets 150 s; the adoption section says to fit `staleAfter` to the refresh rhythm |
| Flapping between metrics and none | Only `publish` restores metrics; stale only after a full `staleAfter` without one |
| Carbon semantics misread | Stated from `CarbonEvents.h` in the spec's macOS notes (non-exclusive; an exclusive holder silences others undetectably; failure only for an in-process duplicate) |
| Additive API breaks an adopter | Defaulted parameters; proof 2 |

## Out of this plan
Removing DevBox's watchdog — **the DevBox PR that moves its Kit pin past this change must pass `staleAfter: 150`** (its loop publishes every 60 s plus the refresh time, which App Nap can stretch; the 90 s default would make its numbers blink) — and Optimer adopting `handleReopen` (their follow-up PRs, each with a behaviour check, not only a build); detecting an item hidden by macOS; Windows.

## Review log (2026-10-12, independent technical review: 1 blocker, 3 major, 5 minor — folded in)
1. (blocker) Carbon hot keys are non-exclusive across processes, so "⌃⌥M is in use" almost never happens → §3 rewritten as an owner decision (A recommended). 2. DevBox's 60 s loop would blink with the 90 s default → its pin-move PR must pass `staleAfter: 150`, with a behaviour check. 3. Reopen return values differ between the apps → semantics stated; Optimer's adoption includes a manual check. 4. Host test seams for the shortcut → depend on §3. 5. A refused publish could become the source of an expired record → only the last valid publish. 6. Wake after a long sleep → in the spec, tested. 7. Blinking when the publish interval exceeds `staleAfter` → one-time log. 8. "Stale" already meant past ttl → "expired numbers". 9. An existing test crossed the expiry point → bounded.

## Build review (2026-10-12, independent: FAIL → fixed; 0 blocker, 2 major, 6 minor)
1. Exclusive hot keys do not make registration fail (the non-exclusive registration is accepted and silently receives nothing) → spec, comment and log text corrected; failure only for an in-process duplicate. 2. "Logs it once" was once per host activation → once per run through the participant. 3. The blink log also fired after a sleep or a stall → only for the interval between two publishes, not after a wake; tested. 4. `publish`'s doc and the spec now say to publish at least every `staleAfter`, even unchanged values. 5. `staleAfter` is `@ObservationIgnored`. 6. Missing tests → explicit `staleAfter` expiry and the blink log. 7. CLAUDE.md: the DevBox `staleAfter: 150` requirement and the adoption status. 8. The plan's Files, Proof and Risks updated for option A. Also noted by the review: after a refused publish the last valid level stays on screen without numbers while the app runs (intended).

