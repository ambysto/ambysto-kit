---
status: building   # draft → approved → planned → building → verified → shipped
---
# Ambysto Kit: the Terminal theme in AmbystoUI

Ticket: APS-16 (Plane), part of APS-2. Asked by the owner on 2026-10-15: after the suite landing moved to the Terminal design (ambysto-kit#36, AO-12), "the other tools in the kit follow this vibe too".

## Problem

The brand moved on 2026-10-12 and the native code did not follow.

- The brand record (owner's local brand folder, `PRODUCT.md` and `DESIGN.md`) sets the suite frame as **Terminal**: near-black `#0B0B0C`, panel `#101011`, off-white `#E8E8E6`, grey `#8A8A8C`, hairline `#222224`, dim `#3A3A3C`, mono type, hairlines instead of shadows, radius 6 to 8. Each app has **one tone**, used as an accent only, never as status: Optimer `#B0ADFF`, Steady `#62C4F5`, Lean (was Space) `#F99A8F`, DevBox `#4ECFBD`, Power `#9FC675`, Tune `#DFAE5B`, Guard/Gate `#E69AD4`. PRODUCT.md itself records that "the intent and the Kit's `AmbystoUI` tokens have not been updated to match; that is a repo change for the owner to approve."
- `Sources/AmbystoUI/Theme.swift` still carries the 2026-10-09 tokens: violet `#6B5CE8` as accent, coral `#ED708F` as secondary, ink `#1C1A33`, radius 14. The suite bar's floating bar (`SuiteViews.swift`) draws on `Theme.ink` with radius 14.
- Each app keeps its own copy of the old theme: Optimer, Lean (Space), DevBox and Tune each have a `Theme.swift` (used from 37 source files in total). Steady and Guard have none in this workspace. Power is parked (owner, 2026-10-12) and is not touched.

So the web pages and the apps now speak two visual languages, and every app would pick tones on its own.

## Outcome

One source of the brand's native tokens, in the Kit, that every app reads; the suite bar's own views wear it first.

1. **`Theme` holds the Terminal tokens.** The neutrals above, the hairline width, radius 6 (controls) and 8 (panels), and SF Mono for readouts (decision 2). Existing names (`accent`, `secondary`, `ink`, `page`, `card`, `hairline`, `cardRadius`, `cardPadding`) keep compiling: their values change, they are not removed (removal would be a major version).
2. **One tone per app, from the catalog.** `Theme.tone(_ app: SuiteApp) -> Color` (name decided in the plan) returns the seven hexes above. The tone is identity only: an app name, a bar, one border. The level colours (`ok`, `notice`, `alert`) and `glyph(_:)` stay as they are: a tone never stands for a state, and a state never relies on colour alone.
3. **The suite bar views use it.** The floating bar (always dark): Terminal ground, hairlines, each app's short name in its tone, readouts in mono, radius 8. The popover (follows the system appearance): each app's name in the normal text colour with a tone bar beside it, since the tones fall to about 2:1 on a light ground (amended 2026-10-15 after the plan review). No change to what is shown, only how.
4. **Apps switch in their own PRs.** Optimer, Lean (Space), DevBox and Tune drop their local `Theme.swift` for the Kit's (or alias to it) when they next move their pin, the same way they adopt any Kit change. Each app's restyle of its own windows is that app's ticket, not this one.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | `Theme` exposes the neutrals, radii and the seven tones; a test pins every hex | unit test |
| 2 | Every tone has contrast ≥ 7:1 on `#0B0B0C`, and grey `#8A8A8C` ≥ 4.5:1 | unit test computing WCAG contrast |
| 3 | Optimer, Lean (Space), DevBox and Tune build unchanged against the new Kit commit (no removed or renamed API) | build each app against the commit |
| 4 | The floating bar and popover render in the Terminal tokens: the floating bar dark in both system appearances, the popover readable in light and dark (decision 1) | `SuiteBarDemo` screenshots, owner looks |
| 5 | A level is still shown with its glyph, and no tone is used for a level | review + screenshot |
| 6 | `swift build -c release` 0 warnings, `swift test` green, the suite bar grep list at zero hits | build |

## Constraints

- Additive or value-only changes to public API; no removal, no rename (Kit CLAUDE.md: a breaking change bumps the major version and every app's pin).
- `AmbystoUI` views are MEDIUM (builder, then reviewer). The suite bar contract (`docs/spec/suite-bar.md`, catalog, glance records) is HARD and is not changed here: the theme is presentation, not contract.
- No third-party dependency. A bundled font, if chosen, ships as a resource with its OFL text.
- Lands on `main` in the Kit 0.4 line (no tag yet).

## Out of scope

- The renames Guard → Gate (owner, 2026-10-12) and Space → Lean (owner, 2026-10-15). The catalog's `displayName` values ("Ambysto Guard", "Ambysto Space") and the cases `guard` and `space` live in the suite bar contract and its vectors, so the renames are their own HARD change (and a contract or Kit version bump). This theme uses the same tones either way.
- Restyling each app's own windows (each app's ticket).
- Power (parked until the owner's laptop).
- Web pages (AO-12 and each app's site).

## Decisions (owner, 2026-10-15)

1. **Light appearance: (b).** The suite bar's floating bar is always dark (Terminal). Everything else follows the system appearance; each app decides its own light treatment later, in its own ticket.
2. **Mono font: (a).** Native code uses SF Mono (`design: .monospaced`); no bundled font. The web keeps JetBrains Mono.
3. **Tones on a light ground.** Not derived now. A darker set at the same hues is derived (and recorded next to the brand file) only when an app chooses a real light Terminal frame.
4. **Ticket.** APS-16, under APS-2.

## Open questions

None.
