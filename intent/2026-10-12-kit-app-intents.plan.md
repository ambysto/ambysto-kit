# Plan — Ambysto Kit: App Intents for the suite (APS-14)

Intent: [`2026-10-12-kit-app-intents.md`](2026-10-12-kit-app-intents.md) (approved 2026-10-12: Raycast through Shortcuts, first adopter Optimer, local answers may name apps). Self-contained. Not yet accepted by the owner; an independent review follows before that.

## The feasibility question first
App Intents appear in Shortcuts and Spotlight only if the app bundle carries the **App Intents metadata** (`Metadata.appintents`), which Xcode produces at build time with `appintentsmetadataprocessor` from the compiler's *const values* output. The Ambysto apps are built with SwiftPM (`swift build` + each repo's `scripts/build-app.sh`), which does not run that step. The tool exists on this Mac (`xcrun appintentsmetadataprocessor`, options include `--source-file-list` and `--swift-const-vals-list`), so it can probably be run from the build script, but this is unproven. **Group 1 is a spike; if it fails, the work stops and the owner chooses** between an Xcode app shell per app (a build-system change in every repo) and dropping App Intents.

## Files (in build order)
Branch `aps-14-app-intents` (intent and plan in PR #28; build on `aps-14-build`).

### 1. Spike — `spikes/app-intents/` (main session, ≤ 2 h; deleted after, result recorded here)
A minimal SwiftPM executable with one `AppIntent` ("Say hello (Ambysto Spike)") and one `AppShortcutsProvider`, built with `swift build` plus:
- compiler flags for const values (`-Xfrontend -emit-const-values` / `-const-gather-protocols-file` as Xcode passes them; read Xcode's build log of a throwaway Xcode project with the same intent to copy the exact flags and the processor invocation);
- `appintentsmetadataprocessor` run with the produced const-values and source lists, output `Metadata.appintents` copied into `Contents/Resources` of a hand-made bundle, ad-hoc signed;
- check: the intent appears in Shortcuts after the app's first launch (`lsregister` refresh if needed), runs, returns its text; it appears in Spotlight on macOS 26; Raycast can run the shortcut.
Result: **works** (record the exact flags and commands) or **fails** (stop; owner decision).

### 2. Spec — `docs/spec/intents.md` (main session)
Naming ("\<verb\> … (Ambysto \<App\>)"), result shapes (text + optional number with unit), refusals, the safety rule (read, or what the app runs without a confirmation sheet; anything that deletes, stops or changes settings opens the app on its confirmation step), local-only answers may name apps (never written to the suite folder), Windows note.

### 3. Kit helpers — `Sources/AmbystoIntents/` (new library, depends on `AmbystoSuite` for the catalog's display names only) (builder, then reviewer)
- `SuiteIntentName.title(_ verb: String, app: SuiteApp)` and phrase helpers for `AppShortcut`.
- `SuiteIntentAnswer` (text, optional value + unit) → `ReturnsValue<…>` + `ProvidesDialog` shapes.
- `SuiteIntentRefusal: Error, CustomLocalizedStringResourceConvertible`.
- `ConfirmInApp` protocol: an intent that changes something conforms; its `perform()` is provided by the Kit and only opens the app with a URL/notification the app maps to its confirmation step (`openAppWhenRun = true`); a unit test checks that such an intent's `perform()` never calls the app's action closure.
- `scripts/app-intents-metadata.sh`: the spike's build step, reusable by every app's `build-app.sh` (inputs: module name, build dir, bundle path).

### 4. Optimer adoption — its own ticket and repo (not in this plan's PR)
Opened as an Optimer ticket after this merges: "Memory in use", "What uses the most memory" (top three app names, local only), "Clean regenerable caches" (`ConfirmInApp` → the cleanup confirmation). Its own intent and plan.

### 5. CLAUDE.md (EASY)
The new library, the build step, the safety rule under HARD (an intent that changes something must go through `ConfirmInApp`).

## Proof
1. Spike recorded (works / fails with the reason).
2. `swift build -c release` 0 warnings; `swift test` green including the `ConfirmInApp` test.
3. Criteria 1–3 of the intent are owner-run with Optimer's first intents (its own ticket).
4. Grep: `AmbystoIntents` holds no deletion, spawning or signal calls (the suite bar list).
5. Independent review.

## Risks
| Risk | Catch |
|---|---|
| SwiftPM cannot produce the metadata | Spike first; stop and ask |
| Metadata processor flags change between Xcode versions | The script reads the Xcode version and fails loudly; recorded in CLAUDE.md |
| An intent performs a destructive action from Shortcuts | `ConfirmInApp` + test; HARD rule in CLAUDE.md; each app's review |
| Ad-hoc signing blocks intent registration | Checked in the spike; Developer ID (AO-6) noted if needed |
| Spotlight intents need macOS 26 | Degrade to Shortcuts on 14–15 |

## Out of this plan
Each app's intents (own tickets); a Raycast extension; Siri phrases beyond defaults; Windows.
