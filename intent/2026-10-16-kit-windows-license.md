---
status: verified  # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Kit for Windows 0.1 — offline license keys (.NET)

Ticket: APS-23 (#53), part of APS-2 (#5). Follows APS-10 (#12), which built the format, the vectors and the Swift verifier and left "the Windows (.NET) verifier" out of scope.

## Problem

No Windows app can answer "does this person have Pro for this app?". The answer is fully specified: `docs/spec/license.md` (format 1, platform-neutral, with a .NET note) and `docs/spec/license-test-vectors.json` (35 vectors). Only the Swift verifier exists.

Owner decisions (2026-10-16):
- The Ubuntu machine builds the **Windows** side of the suite only; the Mac side is built elsewhere. Tests run in a Windows 11 virtual machine on that host.
- The **Kit for .NET comes first**, before any Windows app, because every app needs it. Its first release holds **the license module only**.
- **.NET 10 LTS** (supported to November 2028; .NET 8 ends on 2026-11-10).
- Apps consume it as a **NuGet package built from a tag**, the same "depend on a semver tag, never a branch" rule as the Swift Kit.
- **Feed: GitHub Packages.** It needs a token even to install a public package, so every machine that builds an app (the VM, later CI) keeps a `read:packages` token in its user NuGet config, never in a repo.
- **Publishing: a GitHub Actions workflow on `windows-v*` tags only** (a `windows-latest` runner: test, pack, push with the workflow's own token), with a tag protection rule so only the owner can create `windows-v*` tags. No write token exists on any machine. The owner pushes the tag.

What a license is for does not change (APS-10, owner 2026-10-10): an honesty-based deterrent, the WinRAR model. "Registered to <name>", a revocation list, no copy protection, no anti-cracking.

## Outcome

`windows-v0.1.0` of `ambysto/ambysto-kit`: a .NET class library, **`Ambysto.Kit.License`**, built from the spec alone (never from the Swift code), in a new `windows/` folder of this repo (suite decision 7, applied by analogy: it puts `macos/` and `windows/` side by side in each app's repository; the Kit's Swift package stays at the root, and `windows/` sits beside it, sharing `docs/spec/`).

### 1. The library

- **Verify** a key text and an `.ambystokey` file, in the spec's order, to either a license or one of the five reasons (malformed, unsupported version, unknown signing key, bad signature, revoked), each with the spec's exact message.
- **The license:** licensee, products, license key id (16 lowercase hex characters), issued day, updates-until day (none = updates for life), seats, signing key id; "covers this product" and "allows updates for a build made on this day". Days are UTC dates.
- **Identity** (spec, "Signature"): the license key id is the identity. The public license type exposes no key text, signature or hash of them, and two licenses are equal exactly when their key ids are equal. This differs on purpose from the Swift type, which compares every field; the spec makes the key id the identity.
- **Products:** the spec's fixed bit table (bit 2 `space` is shown as "Ambysto Lean", bit 6 is `gate`, "Ambysto Gate"); bits the library does not know are ignored.
- **Trusted keys and revocations** compiled in. The production set is **empty** until the owner's key PR, exactly like the Swift Kit, and a test pins it. The published test key (id 0) is reachable only from the library's own tests; apps cannot construct a trusted set or a test key (the .NET counterpart of Swift's `package` access).
- **One key list, two copies.** The Swift and .NET builds each compile in the production set and the revocations. They must never differ, or every paying Windows user gets "unknown signing key" (or a withdrawn key keeps working on one platform). So: `scripts/check-key-sets.py` compares the ids and public keys of both production sets and both revocation lists and fails on any difference; the owner's key PR and every revocation PR edit **both** copies and both pin tests; no `windows-v*` or `v*` tag is cut while the check fails. The key PR (and a revocation PR) has two commits, one per copy: only the Swift commit is cherry-picked onto `release/0.2` (the release line in `CLAUDE.md`), and the Windows commit stays on `main`, where `windows-v*` tags are cut.
- `System.Security.Cryptography` only (ECDSA P-256, SHA-256, IEEE P1363 signatures), as the spec's Windows note says. Strict UTF-8 decoding. Parsing is **ASCII-only**, as the spec requires (not .NET's Unicode letter and digit tests). The file is read as raw bytes and decoded by the spec's rules, never by a reader that guesses the encoding.
- No network, no file writes, no registry, no device identifier, no telemetry. It reads only the key file it is given (4096 bytes at most).

### 2. Tests and proofs on Windows

- The test project reads `docs/spec/license-test-vectors.json` **in place** (no copy that could drift) and checks every vector's result, never its signature bytes.
- The vectors have no file case and always trust only the test key, so the library's own tests cover the file rules and the default trust set (criteria 4 and 5).
- They run in the Windows 11 VM, so the verifier is proven on Windows' own crypto (CNG), the code path users get. A green run on Linux does not count.

### 3. Packaging and repository

- `dotnet pack` makes `Ambysto.Kit.License.<version>.nupkg`. A tag `windows-vX.Y.Z` is the only source of a published package; the Windows tag line is separate from the Swift `vX.Y.Z` line and never touches `release/0.2`.
- Semver follows the spec's versioning: adding a product or a revoked id is a minor or patch release; a format change is a major release on both lines.
- Tags stay the owner's, and a tag is never moved.
- `CLAUDE.md` and `README.md` gain the Windows library ("What it is", a Verify entry for `dotnet test` in the VM and `scripts/check-key-sets.py`, the two-copies rule, and the Open line that today says "The Windows verifier exists as spec only"). `.gitignore` gains `windows/**/bin/` and `windows/**/obj/`.

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | Every vector in `license-test-vectors.json` verifies to its documented result (fields or reason) | `dotnet test` in the VM |
| 2 | Nothing throws on any input, and nothing forged verifies: at least 100,000 random strings and every single-byte change of each valid vector's decoded key bytes are refused with one of the five reasons; mutated vector texts (which may still verify, `0` and `O` being the same symbol) end in a license or a reason without throwing | fuzz test in the VM |
| 3 | The text form round-trips through lower case, spaces, tabs, line breaks, `O`/`I`/`L`, with or without `AMB`; non-ASCII letters and digits (for example full-width or Arabic-Indic digits) are malformed | tests |
| 4 | File rules: 4096 raw bytes (a UTF-8 BOM included in the count) accepted and 4097 refused, a leading UTF-8 BOM ignored, one trailing newline accepted, invalid UTF-8 and a UTF-16 BOM malformed; oversize, missing and unreadable files malformed | tests |
| 5 | A verifier built with the defaults trusts no key: every vector that reaches step 5 of the verification order is "unknown signing key"; the production set is pinned to no ids | tests |
| 6 | The five messages equal the spec's "Reasons" table character for character | test |
| 7 | Verifying one key takes under 10 ms (median of 1,000, after warm-up) | test in the VM |
| 8 | The library references no networking assembly and no package outside the .NET runtime | a test over the assembly's references, plus `dotnet list package` |
| 9 | `dotnet build -c Release` with nullable on and warnings as errors: 0 warnings | build in the VM |
| 10 | The two key lists are equal: `scripts/check-key-sets.py` passes (both empty today), and fails on a fixture where one side has an extra id or revocation | script, with its fixtures |
| 11 | The Swift Kit is untouched: `git diff` on `Sources/`, `Tests/` and `Package.swift` is empty; `docs/spec/` changes only as the Constraints say | review of the PR diff |
| 12 | After the owner pushes `windows-v0.1.0`, a scratch console app in the VM installs the package from the feed by version, and a valid test-key vector comes back "unknown signing key" with the spec's message (the package loads, parses and verifies, and trusts nothing by default) | owner-assisted, in the VM |

**Waiting for the production key** (like Swift 0.1's proofs 3 and 7–10): a key signed by the production key verifies on Windows; the key PR adds it to both copies and `check-key-sets.py` passes. Until then criterion 12 proves only that the package trusts nothing, and no Windows app may ship to users on a Windows Kit version without the production key.

## Constraints

- **HARD** (`CLAUDE.md`: the verifier and its parsing): the main session writes it; hard-reviewer reviews the plan and the code.
- .NET 10 LTS, C#, a class library targeting `net10.0` (it needs no Windows-only API). No third-party runtime dependency; the test project uses **MSTest** (Microsoft's own, owner decision 2026-10-16), and the library ships with no dependency.
- Built from `docs/spec/license.md` and the vectors only. Where the spec is unclear, stop and ask; do not read the Swift code for the answer. The spec and the vectors do not change (both are HARD and shared), with two exceptions, both in this intent's PR: the "File" section states the reason for an oversize, missing or unreadable file (malformed, what the Swift verifier already does; HARD, reviewed with this intent), and the Windows note names the library and says to read the file as raw bytes.
- No key material beyond the published test key; nothing private in this public repo.
- English on GitHub; branch `aps-23-windows-license`; one commit per plan group; a PR per phase; never a push to `main`; no merge without being asked.

## Out of scope

- Issuing keys on Windows. Keys are issued only on the owner's Mac (`LicenseTool`).
- The other Kit modules for Windows: suite bar, safety, process reading, theme. Each gets its own intent.
- Any app adopting the library, and an app's `.ambystokey` file association (each app registers its own).
- Ambysto Steady's Windows app (a Python backend, `steady/CLAUDE.md`): it does not consume a .NET library; it verifies from the spec in its own ticket if it ever gets a Pro tier.
- Online activation, seat enforcement, device binding, anti-cracking (unchanged from APS-10).
- CI on every PR. Proofs run in the VM.

## Open questions

_None._ Decided by the owner on 2026-10-16: the package feed and the publishing workflow (see Problem); MSTest as the test framework; a file that is larger than 4096 bytes, missing or unreadable is **malformed**. That last answer is what the Swift verifier already does, so `docs/spec/license.md` ("File") now says it for every platform, in this intent's PR as its own commit; no Swift change.

## Review log

**Round 1 (hard-reviewer: Fable 5.1, FAIL, 2026-10-16).** 1 MAJOR, 7 MINOR, 1 NIT; all addressed in this revision.
1. MAJOR: the production set and revocations would live in two copies with no rule keeping them equal; a key PR touching only Swift would lock out every Windows user. Addressed: "One key list, two copies", criterion 10, the production-key paragraph.
2. MINOR: criterion 5 claimed every test-key vector is "unknown signing key", but malformed ones never reach step 5. Addressed.
3. MINOR: the vectors carry reason ids, not messages. Addressed: criterion 6.
4. MINOR: the reason for an oversize, missing or unreadable file is unspecified; UTF-16 BOM and the byte count. Addressed: an open question, criterion 4, raw-byte reading.
5. MINOR: the identity rule was not carried. Addressed: "Identity".
6. MINOR: "the spec does not change" contradicted the Windows-note edit. Addressed: the exception is named.
7. MINOR: the test framework was unnamed. Addressed: an open question.
8. MINOR: CLAUDE.md, README, `.gitignore` and the proofs that wait for the production key were missing. Addressed: section 3 and the production-key paragraph.
9. NIT: a tag-triggered workflow needs tag protection. Addressed: the publishing decision.

**Round 2 (hard-reviewer: Fable 5.1, PASS, 2026-10-16).** All round-1 items verified in the text; the spec change matches the Swift verifier (`verify(fileAt:)`); `scripts/check-vectors.py` 35 of 35. 3 MINOR and 4 NIT, fixed after the PASS:
1. MINOR: how the key PR's cherry-pick onto `release/0.2` handles the C# half. Fixed: two commits, only the Swift one is picked; `windows-v*` tags are cut on `main`.
2. MINOR: criterion 2 did not require that nothing forged verifies. Fixed: random strings and single-byte changes of key bytes must be refused; mutated texts only must not throw.
3. MINOR: Steady's Python backend had no source. Fixed: `steady/CLAUDE.md`.
4. NIT: suite decision 7 is about app repositories. Fixed: "applied by analogy".
5. NIT: equality by key id differs from the Swift type. Fixed: said to be deliberate.
6. NIT: spec wording "larger, missing or cannot be read". Fixed.
7. NIT: the Swift tests pin 5000 bytes, not the 4096/4097 boundary the spec now states. Not in this PR (the Swift side is not built on this machine; criterion 11).
