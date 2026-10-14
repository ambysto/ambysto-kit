# Plan — Ambysto Kit: App Intents for the suite (APS-14)

Intent: [`2026-10-12-kit-app-intents.md`](2026-10-12-kit-app-intents.md) (approved 2026-10-12: Raycast through Shortcuts, first adopter Optimer, local answers may name apps). Self-contained. Revised after an independent technical review (2 blockers, 5 major, 3 minor — folded in; log at the end). Plan accepted by the owner on 2026-10-12.

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

## Spike record (2026-10-12, main session, Xcode 27.0 / 27A266a, macOS 26.6, M1)
A minimal SwiftPM executable (`/tmp/aispike`) with one `AppIntent` (literal `title`) and one `AppShortcutsProvider` (phrase with `\(.applicationName)`), not yet Optimer's real build:
1. **`swift build` (Swift 6.4, which now uses the Swift Build system) already emits `.swiftconstvalues`** for the module, with the intent's conformances; no extra compiler flags were needed.
2. **`xcrun appintentsmetadataprocessor` run by hand** (`--source-file-list` with the sources, `--swift-const-vals-list` with the const-values file, `--module-name`, `--sdk-root`, `--toolchain-dir`, `--xcode-version 27A266a`, `--platform-family macOS`, `--deployment-target 14.0`, `--target-triple arm64-apple-macos14.0`) **wrote a valid `Metadata.appintents`**: `extract.actionsdata` lists the action and the auto shortcut with its phrase template "Say hello in ${applicationName}".
3. `xcrun appintentsnltrainingprocessor` copied `root.ssu.yaml` into the metadata but **failed to archive the SSU assets** ("Could not archive SSU artifacts") — needed only for Siri/Spotlight phrase matching; to investigate with Xcode's own invocation.
4. The bundle (`Contents/Resources/Metadata.appintents`, `LSUIElement`, ad-hoc signed) registers with LaunchServices. **Not yet verified: that Shortcuts and Spotlight list the action** — no command-line tool lists App Intents; the owner checks in the Shortcuts app (search "Say hello") with the copy installed at `~/Applications/Ambysto Spike.app` (delete it afterwards).
5. Not yet done: Optimer's real universal build, the `.dmg` with quarantine, cold launch, a "changes something" intent with `openAppWhenRun`, and a protocol-supplied default.

### Owner check (2026-10-14, Xcode 27.0 / 27A266a, macOS 27.2, M1)
The same bundle, run from the Shortcuts app; the main session read the unified log (`linkd`, `BackgroundShortcutRunner`, the app).
1. **Shortcuts lists the action** "Say hello (Ambysto Spike)" after one launch; registration from SwiftPM-built metadata works.
2. **Ad-hoc signed, running the action fails.** The app's `appintents:register process` is refused three times (`NSCocoaErrorDomain 4097`, "connection to service named com.apple.linkd.autoShortcut"), each right after `linkd` logs "Unable to get teamId from com.ambysto.aispike"; Shortcuts waits about 30 s, then shows "LinkDaemon.ProcessRegistry.Errors error 0". The same happens on a cold launch by the intent, when the metadata was indexed long before, so it is not a launch race.
3. **Re-signed with an Apple Development certificate (a free Personal Team; `codesign -dv` shows a `TeamIdentifier`), nothing else changed, it works:** `linkd` cold-launches the app, the process registers, the policy check allows it, `SayHello` runs and Shortcuts shows its answer; no window opens. From Spotlight (run source `spotlight-tool`) it runs too. Raycast was not tried.
4. **The system records each run.** `linkd` writes a transcript entry to a Biome stream named after the bundle id, whatever the app donates. Whether the entry holds the answer text was not checked. The spec's "no donation, no indexing" cannot promise that a local answer (for example app names) leaves no trace on the Mac; it must say what the app does not do, and the owner decides whether name-returning intents stay.

**Status:** feasible from SwiftPM, but **App Intents run only in a build signed with a team identifier**. An ad-hoc build lists the actions and every run fails, so an app must not ship intents in an ad-hoc build. That ties App Intents to Developer ID signing (AO-6) for users; a Personal Team certificate only serves local tests. Owner decision before the spike continues in Optimer: (a) wait for AO-6 and ship intents only in Developer ID builds, or (b) continue the Optimer spike now with a locally signed build (the universal build, the `.dmg` with quarantine, an `openAppWhenRun` intent, a protocol-supplied default) and gate intents on AO-6 at release. Still open: the SSU archive error (step 3 of the record), Raycast, and what the Biome entry contains.


**Owner decision (2026-10-14):** option (b): the Optimer spike continues now with a locally signed build; intents ship only in Developer ID builds (AO-6).

### Optimer spike (2026-10-14, `ambysto-optimer` branch `ao-app-intents-spike` at `0b00cfa`, never merged; Xcode 27.0 / 27A266a, macOS 27.2, M1)
Optimer's real build through `scripts/spike-app-intents.sh` (universal `--arch arm64 --arch x86_64`, release, Optimer's Info.plist with `LSUIElement`, bundle id `com.ambysto.optimer.intentspike`), then re-signed by the owner with an Apple Development certificate and run from `~/Applications`. Three intents: "Memory in use" (read, `ReturnsValue<Int> & ProvidesDialog`, with an App Shortcut), "Clean regenerable caches" (literal `openAppWhenRun = true`, only routes to the Cleanup window through an in-process router), and a probe whose `openAppWhenRun` comes from a protocol extension.
1. **Metadata from the universal build works.** `appintentsmetadataprocessor` runs once with the arm64 triple (the arm64 and x86_64 outputs were byte-identical). **The SSU archive error is gone** when `appintentsnltrainingprocessor` gets all its flags (`--infoplist-path`, `--temp-dir-path`, `--bundle-id`, `--product-path`, `--extracted-metadata-path`, `--metadata-file-list`, `--archive-ssu-assets`); `Metadata.appintents/nlu/nlu.lzfse` is produced. The script holds the exact commands.
2. **Read intent:** answers "Memory in use: NN%" from Shortcuts and from Spotlight, both on a cold launch; no window opens.
3. **"Changes something" intent:** the literal `openAppWhenRun` is `true` in the metadata; a cold launch by the intent opens Optimer on the Cleanup window through the router; nothing is deleted.
4. **A protocol-supplied `openAppWhenRun` is not seen and breaks the intent.** The metadata says `false`, and every run fails ("an internal error occurred"): the app logs "Failed to instantiate type SpikeProtocolDefaultIntent by name … does not exist in the process's memory space" (`LNPerformActionErrorCodeActionNotFound`), and the type's name is absent from the release binary while the two direct conformances are present. Rule for the spec and the lint: every intent conforms to `AppIntent` directly, with literal `title` and `openAppWhenRun`; no helper protocols over `AppIntent`.
5. **Cold launch: macOS quits the app 30 s after the last intent.** `linkd` tracks an app it launched; 30 s after the last intent it logs "Idle grace period elapsed … asking launcher to terminate if appropriate" and sends a Quit Apple event, and Optimer's `applicationShouldTerminate` answers `NSTerminateNow`. This happened after the Cleanup intent had opened a window. The plan's assumption that the app "stays running" is wrong for a cold launch by an intent (an app launched by Spotlight was reused, not tracked). The spec must choose: the app refuses that quit once it shows a window or its own icon, or it accepts it for read-only runs; the confirmation router must not lose a pending confirmation to it.
6. **Spotlight donations fail:** each run logs `CSIndexErrorDomain -1000` "Failed to request donation" from the app process. The app donates nothing itself; the framework tries. Not investigated.
7. Not done: the `.dmg` with quarantine and App Translocation (agents do not run `hdiutil`, and a quarantined app signed only for development is blocked by Gatekeeper, so this waits for a Developer ID and notarized build, AO-6); Raycast. Unrelated to the spike: Optimer's `main` universal build now warns "The x86_64 architecture is deprecated for your deployment target (macOS 27.0)", which breaks its 0-warning gate under Xcode 27 (its own ticket).

**Status:** the spike answers feasibility: **works** with a team-signed build, under rules 4 and 5. Next per this plan: the spec (`docs/spec/intents.md`) with rules 4 and 5 and the Biome note, then the Kit tooling (group 3). The `.dmg` check repeats with AO-6.

**Owner decision (2026-10-14), rule 5:** the app accepts the system's quit after a cold launch only when no window of it is open and no confirmation is pending; otherwise it refuses and keeps running as if the user had launched it. It never refuses a log-out, restart, shut-down or the user's own Quit. Its own menu bar icon does not count as "open" (a menu bar app always shows one, so counting it would keep every read-only run alive). Written into `docs/spec/intents.md`; how the app tells the system's quit apart is proven in Optimer's adoption.

## Review log (2026-10-14, independent review of `docs/spec/intents.md`: 4 major, 4 minor, folded in)
1. (major) Confirmation not bound to what it shows → token bound to the exact items, single use; a new request never changes a confirmation on screen; parameters only pre-select and the app re-measures. 2. (major) A "sticky" rule beyond the owner decision → removed; the own-icon wording added. 3. (major) Log-out trap while the quit's origin is unproven → "when the app cannot tell, it accepts". 4. (major) Name-returning answers vs the system's own record → marked open for the owner. 5–8. (minor) Read kind "starts something" removed (a spec change adds one); over-stated claims (Raycast, Kit tooling, macOS 14, `lsregister`, `.dmg`) corrected; title rule matches its examples; a system dialog never replaces the app's step, and the allow-list review covers each intent's code.
