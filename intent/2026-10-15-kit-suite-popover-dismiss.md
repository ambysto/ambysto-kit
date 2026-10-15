---
status: draft   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit: the suite popover closes like a menu bar popover should

Ticket: APS-19 (`ambysto/ambysto-kit#42`). Seen by the owner on 2026-10-15 during Optimer's App Intents checks (AO-21): DevBox hosted the suite item (DevBox `main` `d97ecb6`, Kit `9e8c470`), Optimer participated.

## Problem
The suite popover stays open after a click outside, after Esc, and after a click on the suite item. The host app is not hung (its main thread waits in the run loop). The popover went away only when the suite turned inactive.

Cause, from the code (`SystemSuiteStatusItem` in `Sources/AmbystoUI/SuiteStatusItem.swift`): the popover is an `NSPopover` with `behavior = .transient`, shown from the status item's button **without activating the app and without making the popover's window key**. Every host is a menu bar app (`LSUIElement`) and usually not the active app:
- a transient popover closes on outside clicks only while its app is active;
- Esc reaches only a key window;
- a click on the item closes the popover on mouse-down, and the button's action then reopens it on mouse-up, so the item never closes it.

## Outcome
1. A click outside the popover closes it, whichever app is active.
2. Esc closes it.
3. A click on the suite item while the popover is shown closes it and does not reopen it.
4. Opening the popover still works the same: one click on the item.

### Success criteria
| # | Criterion | How it is checked |
|---|---|---|
| 1 | With DevBox hosting and another app frontmost, one click outside closes the popover | owner-run, both hosts (DevBox and Optimer) |
| 2 | Esc closes it | owner-run |
| 3 | A click on the item closes it; the next click opens it again | owner-run |
| 4 | The toggle rule (a close followed within 0.3 s by the same click's action does not reopen) is unit-tested with an injected clock | `swift test` |
| 5 | `swift build -c release` 0 warnings, `swift test` green, the suite bar grep list at zero hits | build |

## Constraints
- `AmbystoUI` only; no contract change (`docs/spec/suite-bar.md` unchanged except, at most, a macOS note), no record change, no new public API.
- Activating the app when the popover opens is allowed (it is the user's click on the app's item); nothing else activates it.
- No event monitor that needs Accessibility or Input Monitoring permission.
- MEDIUM (Kit CLAUDE.md: `AmbystoUI` views, `SuiteParticipant` wiring): builder, then reviewer. The adopters pick it up with their next pin move.

## Out of scope
- An app's own MenuBarExtra popovers (SwiftUI handles them).
- Windows.

## Open questions
_None._
