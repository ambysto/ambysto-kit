# Plan — Ambysto Kit: App Intents for the suite (APS-14)

Intent: [`2026-10-12-kit-app-intents.md`](2026-10-12-kit-app-intents.md) (approved 2026-10-12: Raycast through Shortcuts, first adopter Optimer, local answers may name apps). Self-contained. Revised after an independent technical review (2 blockers, 5 major, 3 minor — folded in; log at the end). Not yet accepted by the owner.

## Order: prove it in Optimer, then move what is proven into the Kit
App Intents appear in Shortcuts and Spotlight only if the app bundle carries the **App Intents metadata**, which Xcode produces with `appintentsmetadataprocessor` (and `appintentsnltrainingprocessor` for phrases) from the compiler's *const values*. The Ambysto apps are SwiftPM packages built by `swift build` and a `build-app.sh` that assembles a universal, ad-hoc-signed, `LSUIElement` app and a `.dmg`. The metadata extraction reads **literals in the app's own source** (titles, phrases, `openAppWhenRun`), so naming helpers and protocol-supplied defaults from a Kit library would likely be invisible to it. Therefore:

1. **Spike in Optimer** (its own repo, a branch that is not merged) answers feasibility with Optimer's real build.
2. **Kit part** keeps only what does not depend on extraction: the spec (conventions, safety rule), the reusable **build step**, a **metadata lint** that enforces the safety rule at build time, and an in-process **confirmation router** pattern.
3. **Optimer's intents** (its own ticket) are the reference adopter.

If the spike fails, the work stops and the owner chooses between an Xcode app shell per app and dropping App Intents.

## 1. Spike — `ambysto-optimer`, branch `ao-app-intents-spike` (main session, ≤ 3 h; not merged; result recorded here)
Optimer's real build (`origin/main`, which already depends on the Kit at `9e8c470`): universal (`--arch arm64 --arch x86_64`), its Info.plist (`LSUIElement`), ad-hoc signing, the `.dmg` installed with quarantine (App Translocation), launched once.
- One read intent with a literal `static let title` and an `AppShortcutsProvider` with a phrase containing `\(.applicationName)`; one "changes something" intent with a literal `openAppWhenRun = true` (and, on macOS 26, `supportedModes` if that is the non-deprecated form; the build gate is 0 warnings).
- Flags: `-Xswiftc -emit-const-values-path …` and the const-gather protocols file passed on the `swift build` command line (not `unsafeFlags` in a dependency); try `--build-system swiftbuild` too. Copy the exact invocations from a throwaway Xcode project with the same intent (its build log).
- Metadata: run `appintentsmetadataprocessor` **per architecture or once** — record which works with one `--target-triple` for a universal binary; run `appintentsnltrainingprocessor`; place outputs in `Contents/Resources/Metadata.appintents`.
- Also test: a default `openAppWhenRun` supplied by a protocol extension (expected not to be seen; record).
- Checks: the intents appear in Shortcuts after the first launch; the read intent answers without showing a window; the "changes something" intent opens Optimer; Spotlight on macOS 26; Raycast runs the shortcut; a cold launch by an intent (Optimer not running) — what the user sees (menu-bar icon appears, the suite readout starts, the app stays running).
Result: **works** (exact flags and commands) or **fails** (stop; owner decision).

## 2. Spec — `docs/spec/intents.md` (Kit, main session)
- Naming: titles as literals "\<Verb\> … (Ambysto \<App\>)"; phrases with `\(.applicationName)`.
- Result shapes: a short text answer; a value with a unit as `Measurement` where supported (the output type comes from `perform()`'s signature, no wrapper types).
- Refusals with a sentence the user can act on.
- **Safety rule:** an intent may read, or start what the app itself runs without a confirmation sheet. Anything that deletes, stops a process or changes a setting must be an intent that **opens the app** (`openAppWhenRun` literal) and only routes to the confirmation step; the destructive API in the app takes a token that only the confirmation UI creates.
- Local answers may name apps; **no donation, no indexing** of such answers; never written to the suite folder.
- Cold launch: an intent may launch the app; the app behaves as on any launch (own icon or suite readout) and stays running.
- Windows note.

## 3. Kit tooling (builder, then reviewer)
- `scripts/app-intents-metadata.sh`: the spike's proven build step, parameterised (module, build dir, bundle, archs); each app's `build-app.sh` calls it; fails loudly on an unknown Xcode version.
- `scripts/app-intents-lint.py`: reads the produced metadata and fails the build when an intent **not** on the app's allow-list of read-only intents lacks `openAppWhenRun` (the allow-list lives in each app's repo, reviewed like code). This is the enforcement of the safety rule.
- **Confirmation router pattern** (documented in the spec, a small helper in `AmbystoUI` only if the spike shows it is reusable): an in-process router registered in `App.init` (or `AppDependencyManager`) that holds a confirmation request until the app's window exists, so a cold launch works; no URL scheme, no distributed notification (both spoofable by other processes).
- `AmbystoIntents` as a library is **not** created unless the spike shows extraction sees types from a dependency; the Kit defines no `AppIntent`, `AppEntity` or `AppEnum` types.

## 4. Optimer adoption — its own ticket (after 1–3)
"Memory in use" (`Measurement` percent), "What uses the most memory" (top three app names, local only), "Clean regenerable caches" (opens Optimer on the cleanup confirmation via the router; the cleaner takes the confirmation token). Its own intent and plan.

## 5. CLAUDE.md (EASY)
The build step, the lint, the safety rule under HARD (a new intent that changes something is HARD and must pass the lint); test count.

## Proof
1. Spike result recorded, with exact commands, or the stop.
2. Kit: `swift build -c release` 0 warnings; `swift test` green; the lint fails on a fixture metadata file with a destructive intent missing `openAppWhenRun`, and passes on the allowed one.
3. Intent criteria 1–3 owner-run with Optimer's intents (its ticket).
4. Independent review.

## Risks
| Risk | Catch |
|---|---|
| SwiftPM cannot produce the metadata, or not for a universal binary | Spike with Optimer's real build; stop and ask |
| Extraction does not see helpers or protocol defaults | Literals in each app; the spike records it; no Kit intent types |
| A destructive intent runs from Shortcuts | `openAppWhenRun` literal + build-time lint + token-gated destructive API; HARD rule |
| Ad-hoc signing, quarantine or translocation blocks registration | Tested in the spike with the `.dmg`; Developer ID (AO-6) noted if needed |
| Cold launch surprises the user (icon appears, app stays) | Spec states it; owner check |
| Xcode tool flags change | The build step checks the Xcode version |

## Out of this plan
Each app's intents (own tickets); a Raycast extension; Siri phrases beyond defaults; Windows.

## Review log (2026-10-12, independent technical review: 2 blockers, 5 major, 3 minor — folded in)
1. (blocker) Naming helpers invisible to extraction → literals in each app, a lint instead of helpers. 2. (blocker) `ConfirmInApp` defaults likely invisible and its test vacuous → `openAppWhenRun` literal, build-time metadata lint with an allow-list, token-gated destructive APIs. 3. Spike scope → Optimer's real universal build, LSUIElement, ad-hoc signing, `.dmg` with quarantine, NL training processor, `--build-system swiftbuild`. 4. Flags → `-Xswiftc` on the command line, not `unsafeFlags` in a dependency; no intent types in the Kit. 5. Routing → in-process router, no URL scheme or notification. 6. Result types → `Measurement` in `perform()`'s signature; deprecation checked against the 0-warning gate. 7. Cold launch and local-answer privacy → in the spec. 8. "Optimer must first add the Kit" → already done on Optimer's `main` (AO-19 merged). 9. Order → spike in Optimer first. 10. Branch → the plan lives on `aps-15-suite-bar-followups` with APS-15's (one docs PR, #28); CLAUDE.md test count in group 5.
