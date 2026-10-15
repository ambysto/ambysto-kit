---
status: building   # draft → approved → planned → building → verified → shipped
---
# Ambysto Kit: the suite catalog shows Lean and Gate

Ticket: APS-17 (Plane), part of APS-2. Owner renames: Ambysto Guard → **Ambysto Gate** (2026-10-12), Ambysto Space → **Ambysto Lean** (2026-10-15). The landings already use the new names (ambysto-kit#36, ambysto-space#10); the suite bar still shows the old ones.

## Problem

The suite catalog (`docs/spec/suite-bar.md`, "The suite catalog"; `SuiteApp.displayName` in `AmbystoSuite`) names the apps "Ambysto Space" and "Ambysto Guard". Those names reach users in the suite popover's rows, the floating bar, the "Ambysto apps" list and VoiceOver labels. Every app and the Windows build read the catalog from the spec. A second source of names is the license's product table (`docs/spec/license.md`, `Product.displayName` in `AmbystoLicense`), which DevBox shows in its license messages; it built names from the id, so it also printed "Ambysto Devbox".

## Outcome

**Display names only.** The catalog's display name for `space` becomes "Ambysto Lean" and for `guard` "Ambysto Gate"; the short names follow ("Lean", "Gate").

Unchanged, on purpose:
- App ids `space` and `guard`, bundle ids `com.ambysto.space` and `com.ambysto.guard`, glance file names `space.json` and `guard.json`, the open notification's object, catalog order, `participates` and `canHost`. These are wire and system identifiers: renaming them would orphan running apps' records, break Full Disk Access and preferences tied to the bundle id, and change the license `Product` bits' meaning (which never change).
- The glance record format and its vectors (no record carries a display name).

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | `SuiteApp.space.displayName == "Ambysto Lean"`, `.guard.displayName == "Ambysto Gate"`, short names "Lean" and "Gate" | unit test |
| 2 | Ids, bundle ids, glance file names, order, `participates`, `canHost` unchanged | the existing catalog test, unchanged, stays green |
| 3 | The spec's catalog table shows the new names, with a note that ids stay; vectors unchanged | review; `git diff` on the vectors is empty |
| 4 | Optimer and DevBox build and pass against the commit | build both apps against it (run in their pin-move PRs, not before this merge) |
| 5 | `swift build -c release` 0 warnings, `swift test` green, grep lists at zero | build |

## Constraints

- HARD (suite bar contract): the main session does it, then an independent review.
- The wire format does not change, so the contract version does not change. The spec's catalog section records the rename (the spec has no change log); it ships in the Kit 0.4 line (no tag yet).
- Doc comments that say "Space and Tune" read "Lean and Tune".

## Out of scope

- Renaming app ids, bundle ids, repos (`ambysto-space`, `ambysto-guard`), Vercel projects, or license product ids.
- Each app's own rename (Lean's is its own ticket; Gate has no build yet).

## Decision (owner, 2026-10-15)

**Display names only** ("chỉ đổi tên hiển thị"); ids unchanged. The change is small enough that this intent is its own plan: the files are `SuiteCatalog.swift` (two names, two doc comments), the catalog test (a new test for criteria 1 and 2), the spec's catalog table with a note on renames, the Kit README and CLAUDE.md.

## Decision 2 (owner, 2026-10-15, after the review)

The license's display names follow too ("Đổi luôn"): `Product.displayName` becomes a fixed table (Lean, Gate, and DevBox spelled right); `license.md`'s App column and a note that ids and bits never follow a rename. Verifier, parser, bits, format and vectors are untouched.

## Proof

1. `swift build -c release` (fresh scratch path): 0 warnings. `swift test`: 164 (40 Suite, 23 License; +1 each). `scripts/check-vectors.py`: 35 vectors, 0 failed.
2. Both vector files unchanged (`git diff` empty); the existing catalog test unchanged and green.
3. Grep lists at zero hits.
4. Optimer and DevBox against the commit: **not run here.** It runs in each app's pin-move PR; the rename changes no API, only display strings. The second review found one dependent test: DevBox's `ProTests.swift` expects "Ambysto Space" in a license refusal (built from `Product.displayName`), so DevBox's pin-move PR updates it to "Ambysto Lean"; the names it lists are sorted, so a suite-key refusal now lists them in another order (cosmetic). Optimer uses neither name.
5. Independent review before merge.
