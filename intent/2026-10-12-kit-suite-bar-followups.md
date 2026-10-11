---
status: approved   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit: suite bar follow-ups — stale data, reopen, the shortcut's status

Ticket: APS-15 (`ambysto/ambysto-kit#26`, Plane APS-15), part of APS-2. Found by the independent reviews of the first two adoptions, Optimer (AO-19, `ambysto-optimer#24`) and DevBox (ADB-3, `ambysto-devbox#10`), both merged 2026-10-12. Each app worked around them in its own code; every later adopter (Power, Guard, Steady) would have to do the same, so the fix belongs in the Kit.

## Problem

1. **A frozen number behind a live heartbeat.** `SuiteParticipant` republishes the last metrics on its own timer for as long as the app's main thread runs, even when the app's data pipeline has stalled (an awaited call in a refresh loop that never returns). The record stays fresh with old numbers, which the contract forbids ("never a frozen number"). DevBox added its own watchdog (`SuiteLink`, 150 s, keeps the level, drops the numbers); Optimer has none.
2. **An app can become unreachable.** The contract says an own icon removed by hand stays away for the rest of the run. A menu-bar app (`LSUIElement`) that is already running shows nothing when it is opened again, unless it handles reopen. Both apps added `applicationShouldHandleReopen`; nothing in the Kit says an adopter must, and nothing helps it do so.
3. **A shortcut that silently does not work.** `GlobalHotKey` ignores the status of `RegisterEventHotKey`. When ⌃⌥M is taken (by another app, or by a frozen host that still holds it), the suite's floating-bar shortcut does nothing and nobody is told; Optimer cannot retry because it cannot see the failure.

## Outcome

1. **Stale-data rule in the contract and the participant.** A participant whose app has not called `publish` for **`staleAfter`** seconds keeps writing its heartbeat but **without metrics**, keeping the last level, until the next `publish`. `staleAfter` is a participant setting with a default of **3 × ttl** (30 s for ttl 10); an app with a slow refresh sets it explicitly (DevBox: 150 s). Spec "Freshness" gains the rule; DevBox can drop its own watchdog in a later adoption PR.
2. **Reopen is an adoption requirement.** The spec's adoption rules say: an adopter that lives in the menu bar must show its main window when it is opened again while running (`applicationShouldHandleReopen` on macOS), so it is reachable with its own icon hidden. The Kit offers a one-line helper for SwiftUI apps (`SuiteReopen.openMainWindow(id:)` or similar, the plan decides) and the Kit CLAUDE.md adoption checklist gains the item.
3. **The shortcut reports whether it holds the key.** `GlobalHotKey` exposes `isRegistered`; the host logs a failure once and the suite popover shows "⌃⌥M is in use by another app" next to "Show floating bar"; a non-host app (Optimer) can retry when the readout turns inactive.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | A participant whose app stops calling `publish` writes, after `staleAfter`, a record with no metrics and the last level, and returns to metrics on the next `publish` | unit test with an injected clock |
| 2 | Default `staleAfter` is 3 × ttl; an explicit value overrides it; a value below ttl is raised to ttl | unit test |
| 3 | Optimer and DevBox build unchanged against the new Kit commit (the new parameter is defaulted) | build both apps against the commit |
| 4 | With ⌃⌥M already registered by another process, the Kit's `GlobalHotKey.isRegistered` is false, one log line is written, and the popover shows the note | test with a second registration in a helper process (or a fake registrar) + manual check |
| 5 | The spec states the stale rule and the reopen requirement; the spec's vectors are unchanged (no record format change) | review, `swift test` |
| 6 | `swift build -c release` 0 warnings, `swift test` green, the suite bar grep list at zero hits | build |

## Constraints

- No change to the glance record format or the contract version (a record without metrics is already valid).
- Additive API only: existing `SuiteParticipant(...)` and `publish(...)` calls compile unchanged.
- The suite bar contract is HARD (Kit CLAUDE.md): main session, then an independent review.
- Lands on `main` as part of the Kit 0.4 line (no tag yet); the adopters move their pin in their own PRs.

## Out of scope

- Detecting an item hidden by macOS's own menu bar settings or the notch (no reliable API; stays recorded as untested).
- Removing DevBox's own watchdog and adding Optimer's retry (each app's own follow-up PR).
- Windows.

## Decisions (owner, 2026-10-12)

1. **Default `staleAfter` = 3 × ttl** (raised to ttl if set lower); an app with a slow refresh sets its own.
2. **A taken shortcut shows a one-line note** next to "Show floating bar" in the suite popover, besides the log line.

## Open questions

_None: the owner approved the recommendations on 2026-10-12._
