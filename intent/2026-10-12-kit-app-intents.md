---
status: approved   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit: App Intents for the suite (Shortcuts, Spotlight, Raycast)

Ticket: APS-14 (`ambysto/ambysto-kit#25`, Plane APS-14), part of APS-2. Follows suite decision 8 (2026-10-11): the suite grows by features, not apps; what people buy Raycast or Alfred for becomes App Intents in each Ambysto app, with the shared pattern in the Kit.

## Problem

Every Ambysto app measures something people want to ask about without opening a window ("how much memory is in use?", "who is on port 3000?", "how healthy is my battery?") and offers a few safe actions ("clean regenerable caches", "keep the Mac awake for 30 minutes"). Today each answer needs the app's own window or popover. macOS runs App Intents from Shortcuts, Spotlight (macOS 26) and Siri, and Raycast runs Shortcuts, so one App Intent per answer or action reaches all of them. Without a shared pattern, seven apps would name, shape and guard their intents differently, and an action could run from a shortcut without the confirmation the app itself would show.

## Outcome

1. **A Kit library `AmbystoIntents`** (or a small part of `AmbystoUI`; the plan decides) with the suite's conventions:
   - naming: "\<verb\> … (Ambysto \<App\>)", one phrase per intent, the app's display name from the suite catalog;
   - result shapes: a short text answer plus, where it helps, a number with its unit (so a shortcut can use the value);
   - refusals as a typed error with a sentence the user can act on;
   - **the safety rule:** an intent may **read**, or start an action that the app itself runs **without** a confirmation sheet. Anything that deletes, stops a process or changes a setting **opens the app on that action's confirmation step** instead of acting (`openAppWhenRun`), so the suite DNA (measure first, change safely, always reversible) holds from every entry point.
2. **Spec `docs/spec/intents.md`**: the conventions and the safety rule, platform-neutral (Windows: the equivalent is a jump list or a command-line verb; noted only).
3. **First adopter: Optimer** (its own ticket), as the reference: "Memory in use", "What uses the most memory" (top three by name — local only, never leaves the Mac), "Clean regenerable caches" (opens Optimer on the cleanup confirmation). Other apps follow in their own tickets with their own candidates (Power: battery health, keep awake for N minutes within its guard rails; DevBox: who is on port N; Space: largest folders; Tune: changed tweaks).

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | An intent built with the Kit's helpers appears in Shortcuts and in Spotlight on macOS 26 under the app's name, and its answer is usable as a value in a shortcut | owner-run screenshots with Optimer's first intents |
| 2 | An intent marked "changes something" never performs the change itself: it opens the app on the confirmation step | unit test on the helper's contract + Optimer's cleanup intent run from Shortcuts (owner-run) |
| 3 | Raycast can run the intents through its Shortcuts integration with no extension | owner-run check |
| 4 | The spec names no Swift types outside its macOS notes; `swift build -c release` 0 warnings; `swift test` green | build, grep |

## Constraints

- macOS 14+ for the library; Spotlight intents need macOS 26 (degrade to Shortcuts only on older systems).
- No network, no telemetry; answers are computed on the Mac when the intent runs.
- Intents run in the app's process (the app launches if needed); menu-bar apps must not show a window for a read-only answer.
- Additive to the Kit; each app adopts in its own ticket and repo.

## Out of scope

- A Raycast extension (TypeScript) — unless criterion 3 fails.
- Siri voice phrases beyond the default App Shortcuts phrases.
- Windows.

## Decisions (owner, 2026-10-12)

1. **Raycast through Shortcuts**, no extension (criterion 3 checks it).
2. **First adopter: Optimer.**
3. **A local-only answer may name apps** ("What uses the most memory"): it goes to Shortcuts or Spotlight on the same Mac, never to the shared suite folder.

## Open questions

_None: the owner approved the recommendations on 2026-10-12._
