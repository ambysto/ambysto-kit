# Plan — Ambysto Kit: the Terminal theme in AmbystoUI (APS-16)

Intent: [`2026-10-15-kit-terminal-theme.md`](2026-10-15-kit-terminal-theme.md) (approved 2026-10-15, with the owner's decisions 1 to 3). Self-contained: an implementing agent reads this file and the intent only. Revised after an independent technical review (3 major, 6 minor; folded in, log at the end). Not yet accepted by the owner.

Rating (Kit CLAUDE.md): **MEDIUM**. `AmbystoUI` views and additive API; builder builds, reviewer reviews. Nothing here touches the suite bar contract (`docs/spec/suite-bar.md`, `AmbystoSuite`, vectors): the theme is presentation only.

## Design

### 1. Tokens (`Sources/AmbystoUI/Theme.swift`)
Keep the file's first line (the "From Ambysto Optimer (…)" vendored header) and append "; Terminal tokens: APS-16" to it, so the header does not claim the values still match Optimer; rewrite the doc comment to describe the Terminal frame.

**New, fixed (never adapt to the appearance):** `Theme.Terminal`, a nested enum of `Color`s:

| Name | Hex | Role |
|---|---|---|
| `ground` | `#0B0B0C` | floating bar ground |
| `panel` | `#101011` | one step above the ground |
| `ink` | `#E8E8E6` | text on the ground |
| `mute` | `#8A8A8C` | secondary text on the ground |
| `rule` | `#222224` | hairlines on the ground |
| `dim` | `#3A3A3C` | unlit cells, leaders |

**New, tones:** `Theme.tone(_ app: SuiteApp) -> Color`, from one internal table of RGB hex values (`toneHex`, internal, read by the tests through `@testable import`): optimer `#B0ADFF`, steady `#62C4F5`, space `#F99A8F` (shown as Lean, see the intent), devbox `#4ECFBD`, power `#9FC675`, tune `#DFAE5B`, guard `#E69AD4` (shown as Gate). A `switch` over `SuiteApp`, no `default`, so a new catalog case fails to compile until it has a tone. Doc comment: identity only (a name, a bar, one border); never a fill, never a state.

**New, shape and type:** `controlRadius: CGFloat = 6`, `panelRadius: CGFloat = 8`, `hairlineWidth: CGFloat = 1`, `static func mono(_ style: Font.TextStyle, weight: Font.Weight = .regular) -> Font` returning `.system(style, design: .monospaced, weight:)` (SF Mono, decision 2).

**Existing names, values changed (decision 1: only the floating bar is always dark; everything else follows the system):**

| Name | Was | Becomes |
|---|---|---|
| `accent` | violet `#6B5CE8` | adaptive ink: `#0B0B0C` in light, `#E8E8E6` in dark (`NSColor(name:dynamicProvider:)`) |
| `secondary` | coral `#ED708F` | adaptive mute: `#5F5F62` in light, `#8A8A8C` in dark |
| `ink` | `#1C1A33` | `#0B0B0C` (= `Terminal.ground`; the floating bar's tile) |
| `cardRadius` | 14 | 8 (= `panelRadius`) |
| `page`, `card`, `hairline`, `cardPadding` | — | unchanged |
| `color(_:)`, `nsColor(_:)`, `glyph(_:)` (levels) | — | unchanged (intent outcome 2) |

Nothing is removed or renamed and nothing is deprecated (a deprecation would add warnings to the apps' 0-warning builds).

### 2. Suite views (`Sources/AmbystoUI/SuiteViews.swift`)
What is shown does not change; only how.
- **Floating bar** (`SuiteFloatingBar`, always dark): ground `Terminal.ground`, a `hairlineWidth` stroke in `Terminal.rule`, radius `panelRadius`; text `Terminal.ink`, secondary text (`not running`, metric labels) `Terminal.mute`. Header: a small pixel "A" brand mark (new internal `BrandMark` view: the 5×7 grid `01110 / 10001 / 10001 / 11112 / 10001 / 10001 / 10001`, `1` in ink, `2` in `dim`, crisp squares, `accessibilityHidden`) replaces the `circle.hexagongrid` symbol, then "Ambysto" in `mono(.caption, weight: .semibold)`. Compact lines: the app's short name in `mono(.caption, weight: .medium)` **in its tone**, values in `mono(.caption)` (SF Mono already has fixed-width digits). Keep `.environment(\.colorScheme, .dark)`.
- **Rows** (`SuiteRowsView`, shared by the popover, which follows the system, and the expanded floating bar): before each app's name a tone bar (3 × 14 pt rectangle, `accessibilityHidden`); the name in `mono(.headline)` in the primary text colour, **not** in the tone (the tones are tuned for a dark ground; on a light popover they fall to about 2:1; decision 3 defers a light set). Values in `mono(.callout)`; labels stay the system font in `.secondary`. `LevelGlyph` unchanged.
- **Popover** (`SuitePopover`): rows as above; section titles ("Show in menu bar", "Ambysto apps") in `mono(.subheadline)`; native controls (Picker, Toggle, link buttons) and `Divider`s unchanged.
- **Unchanged:** `SuiteStatusItem` (the menu bar item stays native: template icon, system monospaced-digit font), `SuiteSettingsSection`, `FloatingPanel`, `GlobalHotKey`.

### 3. Snapshot proof (`Sources/SuiteBarDemo`)
New mode `SuiteBarDemo --snapshot <dir>`. It never touches the real suite:
- The participant is built with `folders: [temp]` (a folder the mode creates with `FileManager.url(for: .itemReplacementDirectory, …)` or under `NSTemporaryDirectory()`), fake status items (a small `SuiteStatusItem` conformer in the demo, never `.system`, so no real menu bar item or hot key appears even when Optimer or DevBox run), a scratch `UserDefaults(suiteName:)` removed at the end, `observeSystem: false`, and an injected `clock:`.
- `<dir>` and the temp folder are refused when `isRealSuiteFolderOrInside` says they are (or are inside) the real shared folder. At the end it calls `shutdown()` on the participant, then removes **only the temp folder it created** (the one deletion, and it is in `SuiteBarDemo`, not in `AmbystoUI`).
- Samples, written as glance records into the temp folder: Optimer (two metrics, one at `notice`), DevBox (one metric), and Steady (one metric). To capture a "not running" row (only an app seen fresh earlier in the run becomes one), render once, then move the injected clock past Steady's ttl while Optimer and DevBox are rewritten fresh, and render again.
- Rendering: `ImageRenderer` draws nothing inside a `ScrollView` (checked by the review), and the expanded floating bar holds its rows in one. So render through an `NSHostingView` sized to the view, `layoutSubtreeIfNeeded()`, then `bitmapImageRepForCachingDisplay(in:)` + `cacheDisplay(in:to:)`, written as PNG at the backing scale. Outputs: `floating-compact.png`, `floating-expanded.png`, `rows-light.png`, `rows-dark.png` (rows under `.environment(\.colorScheme, …)`), and `rows-not-running.png`. Exits 0; prints the paths.
- The popover itself is not snapshotted (AppKit-backed Pickers); the owner checks it live (proof 4).

## Files (in build order)
Branch `aps-16-terminal-theme` holds the intent and this plan (one PR); the build goes on `aps-16-build`, one commit per group, "Summary (APS-16, plan group N)".

1. **Tokens.** `Theme.swift` as in §1. New `Tests/AmbystoUITests/ThemeTests.swift`:
   - every `Terminal` hex and every tone hex pinned (the table above);
   - WCAG contrast (relative luminance, sRGB) computed in the test: each tone ≥ 7:1 on `#0B0B0C`; `mute` ≥ 4.5:1 on `#0B0B0C` and on `#101011`; light `secondary` `#5F5F62` ≥ 4.5:1 on `#FFFFFF` and on `#ECECEC` (the light window background); dark `secondary` ≥ 4.5:1 on `#0B0B0C`; `ink` ≥ 12:1 on `#0B0B0C`;
   - the adaptive `accent` and `secondary` resolve to the light and dark values (resolve the `NSColor` under `NSAppearance(named: .aqua)` and `.darkAqua`);
   - `cardRadius == panelRadius == 8`, `controlRadius == 6`;
   - every `SuiteApp.allCases` has a tone, and the seven are distinct.
   The existing glyph test stays as is.
2. **Suite views and snapshot.** `SuiteViews.swift` and the new `BrandMark` as in §2; `SuiteBarDemo` `--snapshot` as in §3 (keep its header comment, add the new mode to its usage line). Tests: `BrandMark`'s grid has 17 lit cells and 1 dim cell. The VoiceOver grouping (`.accessibilityElement(children: .combine)` on each compact line and each row, `LevelGlyph`'s spelled-out level) has no unit test; the reviewer checks the diff keeps every one of them.
3. **Docs.** Kit `CLAUDE.md`: `AmbystoUI` line names the Terminal theme and `Theme.tone`; test count; under Open, one line for the adopters (below). The palette lines of `intent/2026-10-09-ambysto-pro-suite.md` (violet and coral tokens) and `intent/2026-10-11-kit-suite-bar.md` get a one-line note "superseded by APS-16 (Terminal theme), 2026-10-15" (the old text stays). Intent status `building` → `verified` when the proofs pass. EASY.

## Adopter notes (for each app's own PR, not this build)
Optimer, Lean (repo `ambysto-space`), DevBox and Tune each have a local `Theme` that shadows the Kit's. When an app moves its pin past this build and drops its local copy:
- `accent` is no longer a colour you can put white text on. Where it filled a button or a badge under white text, use an ink fill with a `Terminal.ground` label in dark and the reverse in light, or the system's prominent style.
- Its own tone comes from `Theme.tone(.<app>)`: name, bar or one border only; never a status.
- The app's light treatment is its own decision (intent decision 1); a darker tone set is derived only then (decision 3).
- Its build stays at 0 warnings; it gets a screenshot check in light and dark.

## Proof
1. `swift build -c release` 0 warnings (clean scratch path); `swift test` green, UI suite count raised by the new tests (criteria 1, 2, 6).
2. Optimer and DevBox (`origin/main` each, the two apps that use `AmbystoUI`) build unchanged against the branch head, in temporary worktrees with a local path dependency named `ambysto-kit` (criterion 3). Recorded as found by the review: Lean (`ambysto-space`) has no Kit dependency, Tune uses only `AmbystoLicense` at `128a8a5`, and every app's own `Theme` shadows the Kit's, so no app's own windows change; the visible change reaches Optimer and DevBox through the suite views when they next move their pin.
3. `swift run SuiteBarDemo --snapshot /tmp/aps16-snap`: four PNGs; the reviewer and the owner look at them (criteria 4, 5: tones only on names and bars, levels still with glyphs).
4. Owner, live: two `SuiteBarDemo` publishers (as in `scripts/suite-bar-takeover.sh`), the popover opened in light and in dark system appearance, the floating bar shown with ⌃⌥M in both (criterion 4).
5. Grep: the suite bar list at zero hits in `AmbystoSuite` and `AmbystoUI` (the one `NSWorkspace.openApplication` exception unchanged); no file under `docs/spec/` changed; vectors unchanged.
6. Independent review of the diff before it is called done.

## Risks
| Risk | Catch |
|---|---|
| An app adopts `accent` and puts white text on off-white | Adopter notes; each adoption has a light and dark screenshot check |
| Tones unreadable on a light popover | Rows never put text in a tone; the tone is a bar beside the name |
| A new catalog app with no tone | `switch` without `default`: it fails to compile |
| Contrast claims drift from the values | Computed in `ThemeTests`, not copied from the brand file |
| `ImageRenderer` output differs from the live panel | Proof 4 is live, by the owner |
| Brand file and Kit disagree later | The Kit's table is pinned by tests; a brand change is a Kit change with a test update |

## Out of this plan
The catalog renames (Guard → Gate, Space → Lean; suite bar contract, HARD, separate change); each app's restyle and pin move; the menu bar item's own look; Windows; Power (parked).

## Review log (2026-10-15, independent technical review: 0 blocker, 3 major, 6 minor — folded in)
1. (major) `ImageRenderer` draws nothing inside a `ScrollView`, so the expanded bar would be blank → render through `NSHostingView` + `cacheDisplay`. 2. (major) Tune never takes part, and "not running" needs an app seen fresh earlier → Steady sample, injected clock moved past its ttl. 3. (major) The demo's participant was under-specified and could touch the real suite (shared folder, menu bar item, hot key, defaults) → temp folder, fake status items, scratch defaults, `observeSystem: false`, the real-folder check, `shutdown()` before removing only its own temp folder. 4. No test covers the VoiceOver grouping → the claim is dropped; the reviewer checks the diff. 5. The intent put the popover's names in their tone; the plan uses a tone bar (light-ground contrast) → intent outcome 3 amended, owner sign-off asked. 6. The vendored header would mislead once values change → "; Terminal tokens: APS-16" appended. 7. Proof 2 showed little → limited to Optimer and DevBox, with the facts recorded. 8. Older intents still list violet and coral → superseded notes in group 3. 9. `.monospacedDigit()` on SF Mono is a no-op → dropped.
