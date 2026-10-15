---
status: draft   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit: the intents spec and build step catch up with Optimer's adoption

Ticket: APS-18 (`ambysto/ambysto-kit#41`). Source: Optimer's adoption (AO-21, `ambysto-optimer#29`, merged 2026-10-15), with the owner's checks on 2026-10-15 (macOS 27.2, Xcode 27A266a, a build signed with Apple Development). The plan of record is Optimer's `intent/2026-10-14-app-intents.plan.md`, group 5's probe table.

## Problem
The spec's macOS notes (`docs/spec/intents.md`) state four things the adoption showed to be wrong or incomplete, and the build step gives advice that does not work:
1. **Activity Monitor's Quit** is said to be a Quit Apple event. It never reaches the app: neither `applicationShouldTerminate` nor `applicationWillTerminate` runs.
2. **How the system's quit is told apart** is "proven in the first adopter, not here". It is now proven: `aevt/quit` with no quit reason, from `/usr/libexec/linkd` (`com.apple.linkd`). Other senders seen:
   - Shortcuts "Quit App": `BackgroundShortcutRunner`;
   - `osascript`: itself;
   - the app's own Quit: no Apple event;
   - log-out: not from linkd, and it completed with the rule in place.
   A refusal holds: the process was alive two minutes later. The quit came only after the Shortcuts editor was closed, not 30 s after the run.
3. **A cold launch for an intent is background-only.** LaunchServices reports `BackgroundOnly` and the activation policy is `.prohibited`. After a read intent, the app's menu bar item, and the suite item it hosts, show but do not respond until the system's quit. Switching to `.accessory` at launch makes the app a `UIElement` but does not fix the item. After an "opens the app" intent, the popover worked with that switch. The spec's "The app starts as on any launch" does not hold on macOS for a read intent.
4. **Listing in Shortcuts** needed the signed app to be opened once and re-registered (`lsregister -f`), because an earlier ad-hoc copy had the same bundle id and `CFBundleVersion`.
5. **Build step:** `scripts/app-intents-metadata.sh` refuses stale const values and says "Rebuild: swift build …". After an incremental build, the objects are recompiled but `<Module>-primary.swiftconstvalues` is not, so a rebuild does not help. Optimer's fix: remove the app module's release intermediates before a signed build.

## Outcome
1. `docs/spec/intents.md` macOS notes:
   - "Lifetime" states the proven senders, the refusal that holds and the timing, and corrects Activity Monitor;
   - a new "Launch role" note covers the background-only launch, the inert item after a read intent (a known limit, owner decision 2026-10-15), and the `.accessory` switch Optimer keeps;
   - "Build step" or "Signing" covers the listing in Shortcuts.
   The rules (Kinds, Launch and lifetime) are unchanged: the quit rule already says what Optimer does.
2. `scripts/app-intents-metadata.sh`: the stale-build message says to remove `.build/out/Intermediates.noindex/<Module>.build/<Config>` and rebuild. The spec's "Build step" gains one sentence on it.
3. The Kit CLAUDE.md App Intents line and the "Open" item for APS-14 follow the spec.

### Success criteria
| # | Criterion | How it is checked |
|---|---|---|
| 1 | Every item of the Problem is in the spec's macOS notes or the script, worded as observed (dates, versions), with no claim beyond Optimer's probe | independent review against Optimer's plan, group 5 |
| 2 | The platform rules (Kinds, Naming, Answers, Privacy, Launch and lifetime) are unchanged, except that a macOS note may qualify "starts as on any launch" | diff review |
| 3 | `scripts/check-app-intents-lint.sh` still passes; `bash -n` on the metadata script; its stale message is exercised by touching a source file in a scratch copy | script run |
| 4 | `swift build -c release` 0 warnings and `swift test` green (no Swift change expected) | build |

## Constraints
- The spec is HARD (Kit CLAUDE.md: the intents safety rule). The main session writes it, then an independent review. The script is MEDIUM, but its change is small, so the main session does it in the same PR and the same review covers it.
- The spec names no Swift types outside its macOS notes.
- No contract or lint change.

## Out of scope
- Making the item respond after a read intent (owner decision: a known limit).
- Raycast, the `.dmg` with quarantine (AO-6).

## Open questions
_None._
