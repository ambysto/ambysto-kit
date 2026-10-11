# Ambysto Kit: the shared Swift package of the Ambysto Pro Suite (Plane APS, GitHub ambysto/ambysto-kit)

## What it is
Three libraries the suite apps import, one tool only the owner runs, and the suite-level documents (`intent/2026-10-09-ambysto-pro-suite.md`, the market research). This repo is public: nothing private goes into it, its commits, this file or `.claude/settings.json`. No third-party dependencies.
- `AmbystoLicense`: offline license keys (P-256 signatures, CryptoKit): `LicenseVerifier`, `TrustedKeys`, `Revocations`, `Product` bits. Reads a key string or a `.ambystokey` file (4 KB at most); no network.
- `AmbystoProcess`: process reading over libproc and sysctl: pid list, identity by pid + start time, path, cwd, argv, open and mapped files, listening TCP sockets, CPU and memory sampling (`ProcessSampler`, `MachClock`). It never sends a signal.
- `AmbystoSafety`: the file-safety gate: never-touch list, canonical paths and firmlinks, identity of the item and every ancestor by device + inode, in-use check, Full Disk Access. It never deletes or moves anything; which folders an app may change comes from the app's `AllowedRootRule`.
- `ambysto-license` (`LicenseTool`, with the C target `KeychainACL`, which only adds a Keychain item whose ACL trusts no app): issues keys, keeps the ledger, backs the signing key up to an external volume. Owner only.

Dependents: DevBox (pinned by revision, `72ab89a`, until `v0.2.0` exists); Space and Optimer still carry vendored copies of the safety and Pro code and switch later. Apps depend by semver tag (`from:`) once one exists; until then they pin a `main` commit and move it deliberately.
Cost of a bug: a wrong gate deletes user data in every app of the suite; a wrong verifier locks out paying users or lets forged keys through; a mishandled signing key cannot be taken back. The bar: spec vectors, an independent review of every safety change, and no key material within an agent's reach.

## Verify
- `swift build -c release` (0 warnings) and `swift test` (73 tests: 36 Safety, 15 Process, 22 License; two tests, in the `SafetyGate` and `PathCanonicalizer` suites, read the real home folder, read-only; the tests start their own helper processes and delete only their temp files).
- `scripts/check-vectors.py` (the spec's test vectors against OpenSSL), `scripts/compare-sockets.sh` (listening sockets equal `lsof`, three runs), `scripts/compare-cpu.sh` (CPU within 5 points of `top`). `ProcessProbe` is the proof helper executable; no app depends on it.
- Specs (`docs/spec/`) name no Swift types outside their macOS notes (proof 6 of both plans): Windows builds from them.
- Proofs on record: Kit 0.1 (license) 1, 2, 4, 5, 6 and 11 done; 3, 7, 8, 9 and 10 need the production key, the tag and the new machine. Kit 0.2 (process and safety) 1 to 6 pass; 7 and 8 are not recorded yet (see Open).
- The plans' review logs record what reviews found and fixed (0.1: 9 major in the plan, 4 in the code; 0.2: 1 blocker, 7 major). Keep that habit: a safety change gets an independent review before it is called done.

## HARD: the main session does it directly, then an independent reviewer
- `AmbystoSafety`, including any new public API in it: the gate's order of checks, `NeverTouch` (a new entry too: add-only by spec, but what it protects is a product decision), identity by device + inode of the item and its ancestors, add-only protection, the `@_spi(Testing)` seam, the `AllowedRootRule` contract (`refusal` and `restoreRefusal` are separate on purpose).
- `AmbystoLicense`'s verifier and its parsing (the 0.1 reviews found their issues there), `LicenseTool` and `KeychainACL`: key custody, the ledger (written and fsynced before a key is printed), the backup image, released-id refusals.
- The license format, `docs/spec/license.md` and its vectors: Windows shares the format, a format change is a major version, `Product` bits never change (a new product is a minor release). The only vector generator is the tool's `test-vectors` command, which an agent never runs.
- `TrustedKeys.production`, `Revocations`, and tags: a tag is never moved.
- The identity and sampling rules of `AmbystoProcess` (pid + start time; `exec` keeps both, so callers re-check the path): DevBox's stop logic relies on them.

## MEDIUM: builder (Sonnet) builds, reviewer (Opus) reviews
- A new reader or field in `AmbystoProcess` and additive API there (public initialisers); the proof scripts; the probe. In `AmbystoLicense`, `TrustedKeys.init` and `TestKey` stay `package`.

## EASY: transcriber (Haiku) or builder, no review
- Intent status lines and review logs after a decision; README and spec wording that does not change meaning.

## Rules
- Building starts only after the owner has approved the intent and accepted the plan (the intent → plan → build → verify chain in `intent/`).
- The libraries never delete, move, spawn or signal. The grep list in the 0.2 plan (`trashItem`, `removeItem`, `unlink`, `kill(`, `killpg`, `Process(`, `posix_spawn`, …) stays at zero hits in `Sources/AmbystoProcess` and `Sources/AmbystoSafety`; `AmbystoLicense` has no network. Deleting and spawning exist only in `LicenseTool`.
- Never run the license tool from an agent: no `swift run`, no `.build/*/ambysto-license`, no `security` or `hdiutil` commands. The owner builds it in an owner-only clone under their own macOS user, whose login Keychain holds the key; agents run under a separate standard user that never sees that Keychain (the deny list in `.claude/settings.json` is a tripwire; that separation is the real protection). The owner creates the signing key with a USB backup, and only the printed public key enters the repo (a PR adds it to `TrustedKeys.production` and updates the test that pins `production.ids`). `*.ambystokey` and `ledger*.csv` are gitignored; a key is never committed and never pasted into chat (if one is, it is rotated).
- The test key (id 0) is published on purpose and is never trusted by release builds. Apps cannot mint keys (`package` access), so they test entitlement through their own verifier seam.
- Protection is add-only: an app may add protected roots, never remove one; `@_spi(Testing)` is for tests.
- Public API serves the apps: records stay constructible (public initialisers, ambysto-kit#16) and `Sendable`; a breaking change bumps the major version and every app's pin.
- Vendored code keeps its "From Ambysto Space (…)" / Optimer headers; the apps are told when the Kit owns the code and switch in their own PRs.
- Never silently change a never-touch entry, a protected suffix, a vector or a revocation. Flag it and ask; owner decisions live in the intents and plans.
- Gift keys are for life and launch is on gifted keys; a key is never tied to writing a review.
- Git and tickets: English only; the `ambysto` account (check with `gh api user --jq .login` before the first PR of a session; if it is not `ambysto`, stop and ask; never `gh auth switch`); repo-local author `Ambysto`; branches `aps-NN-slug`, never a push to `main`; one commit per plan group, one PR per plan (or per fix); commits "Summary (APS-NN, plan group N)" ending with the Co-Authored-By trailer; do not merge without being asked. Plane APS has no MCP in this folder: use its REST API; the host, workspace and project id are in the gitignored `CLAUDE.local.md` on the owner's machine (this repo is public), and the key is read at run time from `~/.claude.json`, never printed. Cross-link GitHub issue and PR with the Plane item.

## Open (waiting for the new machine)
- No tag yet; `TrustedKeys.production` and `Revocations` are empty and `LicenseTests` pins `production.ids == []` (it changes with the key commit). The first tag is `v0.2.0` (owner decision 2026-10-11: 0.2 merged before the key existed, so one tag carries license, process and safety); apps depend on `from: "0.2.0"`.
- Release steps are in the 0.1 plan: owner-only clone, `swift build -c release`, `ambysto-license new-signing-key` with the USB backup, a PR with the public key, tag `v0.2.0` on the merged commit; then Space adopts the Kit and DevBox moves from its revision pin to `from: "0.2.0"`.
- Kit 0.2 proofs 7 and 8 are not recorded in its plan: DevBox is merged and reads processes through the Kit (its QA-only `Diagnostics` reads DevBox's own process), and PR #15 had an independent review; add both to the 0.2 plan.
- The Windows verifier exists as spec only. GitHub #12 (APS-10) and #5 (APS-2) are open.
