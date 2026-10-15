# Plan: Gate's ids become `gate` (APS-20)

Intent: `2026-10-15-kit-gate-ids.md` (approved 2026-10-15). HARD: the main session builds, then an independent reviewer checks the diff against the intent and this plan.

The work is split so the license part is a commit of its own. That commit is the one picked onto `release/0.2`, whose base `88b3b45` has no `AmbystoSuite`.

## Group 1: license (`main`, later picked onto `release/0.2`)

| File | Change |
|---|---|
| `Sources/AmbystoLicense/Product.swift` | `case guard` → `case gate`; `.gate: 6`; display name `.gate: "Ambysto Gate"`. The doc comment says ids are fixed from the first release and that `guard` became `gate` before it (APS-20). |
| `docs/spec/license.md` | Row 6 becomes `` `gate` `` / Ambysto Gate. The rename note says: product ids and bits are fixed from the first release; `space` is shown as Ambysto Lean; `gate` was `guard` until 2026-10-15, before any release. |
| `scripts/check-vectors.py` | `PRODUCTS[6]` = `"gate"`. |
| `Tests/AmbystoLicenseTests/LicenseTests.swift` | Line 86 uses `.gate`. The product-name test expects raw value `gate` at bit 6, and is renamed to say ids are fixed. A new check: `Product.products(in: 1 << 6) == [.gate]`. That is an internal API, reachable because the test target imports `@testable`. |

## Group 2: suite (`main` only)

| File | Change |
|---|---|
| `Sources/AmbystoSuite/SuiteCatalog.swift` | `case guard` → `case gate`; display name, `canHost` and doc comment follow. Bundle id and glance file name follow from the raw value. |
| `Sources/AmbystoUI/Theme.swift` | `.gate: 0xE69AD4`; the "shown as Gate" comment goes. |
| `docs/spec/suite-bar.md` | Catalog row 7 becomes `gate` / `com.ambysto.gate` / Ambysto Gate. The rename paragraph says: ids and bundle ids are fixed from the first release; `space` is shown as Ambysto Lean; `gate` was `guard` until 2026-10-15, before any release, so the contract stays 1. |
| `Tests/AmbystoSuiteTests/GlanceTests.swift` | The catalog tests expect `gate`, `com.ambysto.gate` and `gate.json`. New test `retiredGuardIdIsUnknown`: a record whose `"app"` is `"guard"` decodes to `.invalid`, the same reason as any unknown app, and the same record with `"gate"` (file `gate.json`) decodes. |
| `Tests/AmbystoSuiteTests/StoreAndLockTests.swift` | `.guard` → `.gate`. |
| `Tests/AmbystoUITests/ThemeTests.swift` | `.guard` → `.gate`. |

## Group 3: docs

- `CLAUDE.md`: line 8 (ids: Lean is display-only, Gate's id is `gate` since APS-20) and the test count (+1 Suite). The release line gains: "the license part of APS-17 and APS-20 is picked onto `release/0.2` before `v0.2.0` (owner, 2026-10-15)".
- The intent's status line.

## Later: the pick onto `release/0.2` (not in this PR)

When `release/0.2` exists and the key PR has been picked, one reviewed PR ports two things:
- the license hunks of APS-17: `Product.displayName` as a fixed table, `license.md`'s App column and note, the product-name test;
- group 1 of this plan.

Each pick is a hand port, not `git cherry-pick`. APS-17's commit also touches Suite files, which the base does not have. That PR must pass `swift build -c release` at 0 warnings, `swift test` and `check-vectors.py` on that branch. Criterion 7 is met when `v0.2.0` is tagged after it. Until then, criterion 7 stays open in this intent.

## Risks

| Risk | Caught by |
|---|---|
| A leftover `guard` id (a string literal, a spec cell, the Python list) | the grep in proof 3 |
| Bit 6 moves or another product's raw value changes | the product-name test pins all seven raw values and bits |
| A vector names `guard`, so `check-vectors.py` or `LicenseTests` would fail | checked 2026-10-15: neither vector file contains `guard`; proof 2 re-checks it |
| An app uses `.guard` and stops compiling | The local clones of Optimer, DevBox, Space, Tune and Power were checked on 2026-10-15: no `.guard` and no `"guard"`. The check repeats in each pin-move PR; Gate does not depend on the Kit. |
| The "ids never change" rule reads as broken | both specs say ids are fixed **from the first release**, and record the one change made before it |
| The `release/0.2` port drifts from `main` | the pick PR is reviewed, and its `Product.swift` diff against `main`'s must be empty |

## Proof (definition of done)

1. `swift build -c release` (fresh scratch path): 0 warnings. `swift test`: 165 (36 Safety, 24 Process, 23 License, 41 Suite, 41 UI).
2. `python3 -I scripts/check-vectors.py`: 35 vectors, 0 failed. `git diff origin/main -- docs/spec/*.json` is empty.
3. `git grep -nIw guard -- Sources Tests docs/spec scripts` finds only the Swift keyword, the two spec notes and the two doc comments that record the old id, and the `retiredGuardIdIsUnknown` test.
4. The grep lists of CLAUDE.md have zero hits in `Sources/AmbystoProcess`, `AmbystoSafety`, `AmbystoSuite` and `AmbystoUI`.
5. An independent review of the diff against the intent and this plan.

## Review log (2026-10-15, independent review of the build: PASS, 0 blocker, 0 major, 3 minor, all folded in)

1. The bit-6 check derived products from the same table it pinned, and no key or vector carried bit 6 → the product test now issues a test key for Gate and expects it to verify as `gate` only (mask `1 << 6`). 2. `license.md` dropped the Gate display-rename date → added, matching `suite-bar.md`. 3. Proof 3 was reworded during the build (it also lists the two doc comments and the `retiredGuardIdIsUnknown` test) → recorded here.

Not run: the owner-run suite bar scripts, `SuiteBarDemo`, an installed Gate appearing in the "Ambysto apps" list, and the `release/0.2` port (criterion 7 stays open until `v0.2.0`).
