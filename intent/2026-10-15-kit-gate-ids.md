---
status: shipped (PR #49); criterion 7 waits for the release/0.2 port   # draft → approved → planned → building → verified → shipped
---
# Ambysto Kit: Gate's ids become `gate`

Ticket: APS-20 (Plane), follows APS-17 (ambysto-kit#43) and AGD-9 (ambysto-gate). Owner decision 2026-10-15 (AGD-9): Ambysto Gate drops "guard" everywhere, including its bundle id, which is now `com.ambysto.gate`. Lean is not part of this: it keeps every id (ASP-4).

## Problem

APS-17 changed only the names users see. It kept the app id `guard`, so the suite catalog still derives the bundle id `com.ambysto.guard`. That bundle id no longer belongs to any app. The suite popover's "Ambysto apps" list finds installed apps by bundle id (`SuiteViews.swift`, `urlForApplication`), so an installed Gate would never appear there and could not be opened from it. Gate would also have to publish its glance under the app id `guard`, a name the rest of the app no longer uses. The license still lists product `guard`, so the license tool would need `--products guard` for a Gate key and the ledger would record `guard`. APS-17 also wrote into both specs that ids "never follow a rename". That was true for Lean, but AGD-9 overrides it for Gate.

Nothing is released yet: the Kit has no tag, no production key exists, no key has been issued, and Gate does not publish a glance. So this is the last point at which an id can change without breaking anyone.

## Outcome

**App id and product id `guard` become `gate`.** That changes:
- `SuiteApp.guard` → `SuiteApp.gate`: raw value `gate`, bundle id `com.ambysto.gate`, glance file `gate.json`, and the `app` value in its glance, the open notification and `settings.json`.
- `Product.guard` → `Product.gate`: product id `gate`. **Bit 6 does not change**, so the license format, the payload and every key stay valid.
- Both spec tables (`suite-bar.md` catalog, `license.md` products) and their rename notes. The new rule: **ids are fixed from the first release. `guard` became `gate` on 2026-10-15, before any release.** Lean's ids stay as they are.
- Everything that names the old id: the Theme tone key, tests, `scripts/check-vectors.py`, README and CLAUDE.md.

Unchanged, on purpose: bit positions, the license format, both vector files (neither names `guard`, checked 2026-10-15), the catalog order, `participates` and `canHost`, Gate's display name and tone, and **the suite bar contract version (stays 1)**.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | `SuiteApp.gate`: raw value `gate`, bundle id `com.ambysto.gate`, glance file `gate.json`, name "Ambysto Gate", same order and roles | catalog test |
| 2 | `Product.gate`: raw value `gate`, bit 6; a mask with bit 6 decodes to `.gate` | license test |
| 3 | A glance with `"app": "guard"` is rejected as an unknown app; one with `"app": "gate"` is accepted | validator test |
| 4 | No `guard` id is left in `Sources`, `Tests`, `docs/spec` or `scripts` (the Swift keyword and "safeguard" stay) | grep, in the plan's proof |
| 5 | Vector files unchanged; `scripts/check-vectors.py` passes | `git diff`, the script |
| 6 | `swift build -c release` gives 0 warnings, `swift test` is green, and the grep lists of CLAUDE.md have zero hits | build |
| 7 | `release/0.2` gets the license part before `v0.2.0` is tagged, so no tag ever carries `Product.guard` | a reviewed pick PR, at release time |

## Constraints

- HARD: the main session does the work, then an independent review. Both the suite bar contract and the license registry are involved.
- **The contract version is not bumped** (owner, 2026-10-15). There are no published records, so there is nothing to stay compatible with.
- **There are no compatibility aliases** (no deprecated `.guard`). No app uses the case: Optimer and DevBox name only their own, and Gate does not depend on the Kit yet. A parser that accepted `guard` would keep the old id alive in the contract and on Windows.
- `Product` bits never change. This renames a case and its raw value; it moves no bit.
- **Release line (owner, 2026-10-15):** the license part (`Product`, `license.md`, `check-vectors.py`, the license tests) is cherry-picked onto `release/0.2` in a reviewed PR before `v0.2.0` is tagged, after the key PR. The suite part stays on `main` (Kit 0.4). CLAUDE.md's release line lists this pick.

## Out of scope

- Lean's ids (`space`, `com.ambysto.space`): kept by ASP-4.
- Gate's own code and docs (ambysto-gate: `CLAUDE.md:16`, the SPEC "Name" line). They follow in Gate's repo once this merges.
- Optimer and DevBox: they move their pins in their own PRs. Neither names `.guard`.
- Windows: it implements from the spec tables, which this change updates.

## Decisions (owner, 2026-10-15)

1. No suite bar contract bump; no compatibility aliases.
2. The license part goes onto `release/0.2` before `v0.2.0`.
3. **APS-17's license display names go onto `release/0.2` too** ("có pick"), so `v0.2.0` shows Lean, DevBox and Gate like `main`.

Plan: `2026-10-15-kit-gate-ids.plan.md`.
