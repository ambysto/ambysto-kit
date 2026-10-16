# Plan: Ambysto Kit for Windows 0.1 — the .NET license verifier (APS-23)

Intent: [2026-10-16-kit-windows-license.md](2026-10-16-kit-windows-license.md) (approved 2026-10-16). Plan accepted by the owner 2026-10-16. Branch `aps-23-windows-license`. Contract: the intent, `docs/spec/license.md` and `docs/spec/license-test-vectors.json`. Never the Swift code.

This plan is self-contained with the intent and the spec. HARD (verifier, parsing, and the key-set check that guards `TrustedKeys.production` and `Revocations`): the main session writes the code; hard-reviewer reviews this plan, then the code of groups 2–6.

## Design

**Names.** Package and assembly `Ambysto.Kit.License` (the spec's Windows note). Namespace **`Ambysto.Kit.Licensing`**, so the type `License` never collides with its own namespace (`Ambysto.Kit.License.License` would force every app to write aliases).

**Public API** (everything else `internal`; `InternalsVisibleTo` the test assembly only):

| Type | Shape |
|---|---|
| `Product` | `enum : int` with the spec's bit as value (`Optimer = 0` … `Gate = 6`); extension `Id` (`"optimer"`, …, `"space"`, `"gate"`) and `DisplayName` (fixed table: `space` → "Ambysto Lean", `gate` → "Ambysto Gate") |
| `LicenseReason` | `enum`: `Malformed`, `UnsupportedVersion`, `UnknownSigningKey`, `BadSignature`, `Revoked`; extension `Message` (the spec's table, verbatim) |
| `License` | `sealed class`, immutable: `Licensee` (string), `Products` (an immutable `FrozenSet<Product>` exposed as `IReadOnlySet<Product>`, unknown bits dropped), `KeyId` (16 lowercase hex), `Issued` (`DateOnly`), `UpdatesUntil` (`DateOnly?`, null = for life), `Seats` (int), `SigningKeyId` (int); `Covers(Product)`, `AllowsUpdates(DateOnly builtOn)` (`UpdatesUntil is null || builtOn <= UpdatesUntil`). `Equals`/`GetHashCode` by `KeyId` only (`IEquatable<License>`). No text, signature or hash is exposed |
| `LicenseResult` | `sealed class`: `License?` and `LicenseReason?`, exactly one set; `IsValid` |
| `LicenseVerifier` | `public LicenseVerifier()` = production trusted set + `Revocations`; `internal LicenseVerifier(TrustedKeys, IReadOnlySet<string>)`; `const int MaxFileBytes = 4096`; `LicenseResult Verify(string text)`; `LicenseResult VerifyFile(string path)`. Thread-safe and stateless after construction |

`internal` plus `InternalsVisibleTo` is a name-based compile-time boundary, not a security one: anyone can ship code that reflects into the library. That fits the honesty model (APS-10: a deterrent, not protection); the point is that no app can trust the test key by accident.

`TrustedKeys` (internal): `Production` (empty dictionary today), `Test` (id 0, the published key), `internal TrustedKeys(IReadOnlyDictionary<byte, string> rawHex)`, which validates each key once (64 bytes, a point on P-256) and throws if a compiled-in key is broken (a build defect, caught by the pin test). `Revocations.All` (internal, empty). The production data is written in one fixed literal form so `scripts/check-key-sets.py` can read it (below).

**Days.** Day *d* is `new DateOnly(2026, 1, 1).AddDays(d)`; `updatesUntil` 0 → `null`.

**Verification order** (`Verify`), each step returning at once:
1. **Text → bytes** (`CrockfordText.Decode`, ASCII only, no `char.IsLetterOrDigit`, no culture or Unicode casing): drop `-`, space, tab, CR, LF; drop a leading `AMB` in any ASCII case; every remaining char must be in `0-9`, `A-Z`, `a-z` (compared as code units); upper-case by arithmetic; `O`→`0`, `I`/`L`→`1`; `U` → malformed; length mod 8 ∈ {1, 3, 6} → malformed; the unused low bits of the last symbol must be zero. A text with no symbols left after the separators and the `AMB` prefix are dropped (`""`, `AMB`, `-- --`, `AMB-`) → malformed; see "Spec gap" below.
2. **Version**: `bytes[0] == 1`, else unsupported version.
3. **Length**: `bytes.Length >= 20`, `n = bytes[19]` in 1…64, `bytes.Length == 20 + n + 64`; else malformed. (Steps 2 and 3 in this order: a one-byte key with version 2 is "unsupported version", as the spec orders it.)
4. **Licensee**: `new UTF8Encoding(false, true).GetString(bytes, 20, n)`; `DecoderFallbackException` → malformed. Never normalised.
5. **Signing key id** `bytes[1]` in the trusted set, else unknown signing key.
6. **Signature**: message = ASCII `AMBYSTO-LICENSE-1` ‖ payload (the first `20 + n` bytes); `ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = { X, Y } })` **per call** (no shared instance across threads), `VerifyData(message, signature, SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`; `false` or a `CryptographicException` → bad signature.
7. **Revocation**: key id (bytes 2–9 as lowercase hex) in the revoked set → revoked.
Then build the `License` (products from the mask, unknown bits ignored).

`Verify` catches nothing broadly: every step is written not to throw, and the fuzz test proves it. `null` text or path throws `ArgumentNullException` (a programming error, not an input).

**`VerifyFile`**: open read-only, shared read; if `Length > 4096` → malformed; read at most 4097 bytes (a file that grows while read is still refused); `IOException`, `UnauthorizedAccessException`, `NotSupportedException`, `ArgumentException` (a bad path) → malformed; the whole file must be valid UTF-8 (strict decoder), so a UTF-16 BOM (`FF FE`, `FE FF`) is malformed; strip a leading `EF BB BF`; then `Verify(text)`. Never `File.ReadAllText` (it guesses encodings).

## Spec gap (decided by the owner, 2026-10-16: malformed)

The spec's steps 1 and 2 do not say what a text that decodes to **zero bytes** but is not empty gets (`AMB`, `-- --`, `AMB-`): there is no version byte to check. The vectors have only the empty text (malformed). Owner decision 2026-10-16: **malformed**, which is what the Swift verifier does; `docs/spec/license.md` ("Text", step 4) says so for every platform, in its own commit in this PR. Tests cover all four texts.

## Repository layout

```
windows/
  global.json                       SDK 10.0.402 (installed in the VM, checked 2026-10-16), rollForward latestFeature
  Directory.Build.props             net10.0, Nullable enable, TreatWarningsAsErrors, AnalysisLevel latest-recommended, Deterministic
  Ambysto.Kit.slnx
  src/Ambysto.Kit.License/          the library (no PackageReference)
  tests/Ambysto.Kit.License.Tests/  MSTest (MSTest + Microsoft.NET.Test.Sdk, versions pinned in group 1)
scripts/check-key-sets.py
scripts/fixtures/key-sets/          pass and fail fixtures for the script
.github/workflows/windows-release.yml
```

## Groups, in order (one commit each)

1. **Scaffold.** `windows/` as above with an empty library and one smoke test; `.gitignore` gains `windows/**/bin/`, `windows/**/obj/`, `windows/**/TestResults/`. Proof: `dotnet build -c Release` 0 warnings and `dotnet test` green in the VM; the test project finds `docs/spec/license-test-vectors.json` by walking up from the test binary to the repo root (a missing file **fails** the test, never skips it).
2. **Text and payload.** `CrockfordText`, `Product`, `LicenseReason` (messages), `License`, `LicenseResult`, payload parsing (steps 1–4, 7's hex). Tests: text rules (criterion 3: lower case, separators, `O`/`I`/`L`, with and without `AMB`; `U`, `#`, full-width and Arabic-Indic digits, `ı`, each mod-8 remainder, non-zero unused bits); the messages read from the spec's "Reasons" table in `docs/spec/license.md` and compared character for character (criterion 6); products mask with an unknown bit; days and `AllowsUpdates`; equality by key id.
3. **Trust and signature.** `TrustedKeys`, `Revocations`, `LicenseVerifier.Verify` with steps 5–7. Tests: every vector with its own trusted keys and revoked ids, comparing the documented fields or reason id (`badSignature` → `BadSignature`, …) (criterion 1); defaults trust nothing: every vector that reaches step 5 is `UnknownSigningKey`, and `TrustedKeys.Production` has no ids (criterion 5); fuzz (criterion 2): 100,000 random strings from a fixed seed over a mixed alphabet are never valid; every single-byte change (XOR with 0x01, 0x80, 0xFF) of each valid vector's decoded bytes, re-encoded with a test-only encoder, is refused; 10,000 mutated texts never throw; timing (criterion 7): median of 1,000 verifications after 100 warm-ups under 10 ms; references (criterion 8): the assembly's referenced assemblies contain no `System.Net.*` and nothing outside the runtime.
4. **File.** `VerifyFile`. Tests (criterion 4) on temp files: exactly 4096 bytes accepted (a valid key text padded with spaces between its groups, which parsing drops, to 4096 bytes; once without and once with the 3-byte BOM inside the count), the same padded to 4097 refused; a BOM-prefixed key verifies; one trailing newline verifies; invalid UTF-8, UTF-16 LE and BE BOMs, a missing path, a directory path and a file locked exclusively by the test → malformed.
5. **Key-set check (HARD).** `scripts/check-key-sets.py [swift-trusted swift-revocations cs-trusted cs-revocations]`: with no arguments it reads the four real source files; the fixtures pass their own paths as arguments (like `check-app-intents-lint.sh`). It **fails closed**: it finds each declaration, takes the **whole** literal body between its brackets, consumes it entry by entry with a strict grammar (`<id>: "<128 hex>"` in Swift, `[<id>] = "<128 hex>"` in C#, and `"<16 hex>"` for revocations, with commas, whitespace and `//` comments), and exits 1 if anything is left over that the grammar did not consume, if a declaration is missing or found twice, or if any id, key (case-insensitive hex) or revoked id differs. Fixtures: equal empty sets and equal one-entry sets (pass); an extra id on either side; a different key for the same id; an extra revocation on either side; an entry in an unrecognised form on each side (for example `1: Key1.publicHex`, or a key split by `+`); a missing declaration (each fails with a line naming it). Runs on the host with `python3 -I` (it reads text; no platform matters) and in the release workflow with `python -I` (criterion 10).
6. **Package, release workflow, docs.** The csproj gains package metadata (id, authors `Ambysto`, a package README, repository URL, `DebugType` embedded so the one `.nupkg` carries its symbols: GitHub's NuGet registry does not take `.snupkg` symbol packages; no version in source: `-p:Version` from the tag). No licence field: the repository has no software licence, and choosing one is the owner's decision (owner steps). `.github/workflows/windows-release.yml`: `on: push: tags: ['windows-v*']`; `permissions: contents: read, packages: write`; `windows-latest`; actions pinned by commit SHA (looked up when written); steps: checkout with full history, a check that the tagged commit is reachable from `origin/main` (`git merge-base --is-ancestor`; the intent cuts `windows-v*` tags on `main` only), setup-dotnet from `global.json`, `python scripts/check-key-sets.py`, `dotnet test -c Release`, `dotnet pack -c Release -p:Version=<tag without windows-v>`, `dotnet nuget push *.nupkg --no-symbols` to `https://nuget.pkg.github.com/ambysto/index.json` with `GITHUB_TOKEN`; it refuses a tag whose version is not `X.Y.Z`. Docs: `README.md` and `CLAUDE.md` (What it is, Verify, the two-copies rule in Rules, HARD list gains `windows/src/Ambysto.Kit.License`, the Open line for the Windows verifier), `docs/spec` untouched. Rehearsal of criterion 12 in the VM: `dotnet pack` to a local folder, a scratch console app outside the repo installs `Ambysto.Kit.License` 0.0.0-local from that folder and gets `UnknownSigningKey` with the spec's message for a valid vector; the rehearsal also checks that the `.nupkg` holds the embedded symbols and that no `.snupkg` is produced.

The intent's `status` moves to `building` with group 1's commit, `verified` after the proofs and the code review.

## Risks, and what catches them

| Risk | Caught by |
|---|---|
| Windows CNG refuses high-S signatures, so vector "high S" fails | criterion 1 in the VM. If it happens: stop and ask; the spec says high-S is valid and cannot change here |
| Unicode traps in parsing (`IsLetterOrDigit`, culture casing, full-width digits) accept a non-ASCII key | group 2 tests, plus the mutation check below |
| `Encoding.UTF8` replaces invalid bytes instead of failing | the vectors' invalid-UTF-8 licensee case; the strict decoder |
| `File.ReadAllText` decodes a UTF-16 file and verifies it | group 4 UTF-16 BOM tests |
| A shared `ECDsa` used from two threads | per-call instances; a test verifies 1,000 keys on 8 threads in parallel and compares the results |
| The two key copies drift (the intent's MAJOR) | `check-key-sets.py` in the workflow and in `CLAUDE.md`'s Verify list |
| The vectors file moves or the test cannot find it, and the tests pass without vectors | group 1: missing file fails; group 3 asserts 35 vectors were read |
| A tag pushed by someone else publishes a package | tag protection (owner step), the workflow runs only on `windows-v*` tags |
| A test-key trust leaks into a release | `TrustedKeys.Test` is `internal`; criterion 5's default-trust test; review |
| `dotnet` in the VM resolves to the machine-wide .NET 8 | `global.json` pins 10.0.402; the VM calls the user-installed `dotnet.exe` by full path |

**Mutation checks** (break on purpose, see a test fail, restore; recorded in the review log): (a) accept any `char.IsLetterOrDigit` in step 1 → the Arabic-Indic digit test fails; (b) check revocation before the signature → the "tampered and revoked" vector fails; (c) skip the unused-bits check → its test fails; (d) read the file with `File.ReadAllText` → the UTF-16 BOM test fails; (e) build `LicenseVerifier()` on `TrustedKeys.Test` → the criterion 5 default-trust test fails; (f) change `Length > 4096` to `>= 4096` → the exactly-4096-bytes test fails; (g) in `check-key-sets.py`, skip the leftover check → the "unrecognised form" fixtures pass and their test fails.

## Proof (what "done" means)

| Criterion | Proof |
|---|---|
| 1–8 | `dotnet test -c Release` in the VM: all green, with the counts in the review log (tests, vectors read: 35) |
| 8 (also) | `dotnet list windows/src/Ambysto.Kit.License package` in the VM: no package |
| 9 | `dotnet build -c Release` in the VM: 0 warnings, 0 errors |
| 10 | `python3 -I scripts/check-key-sets.py` passes; each fail fixture fails with its line (a wrapper, `scripts/check-key-sets-fixtures.sh`, runs them all) |
| 11 | `git diff main -- Sources Tests Package.swift` empty; `git diff main -- docs/spec` only the spec commit already in this PR |
| 12 | owner-assisted after the tag; rehearsed in group 6 with a local feed |
| Code review | hard-reviewer on groups 2–6: PASS |

Not run here: anything on macOS (out of scope on this machine). Waiting for the production key: the intent's list.

## Owner steps

- ~~A tag ruleset so only the owner can create `windows-v*` tags.~~ Done 2026-10-16: ruleset `windows-release-tags` (creation, update and deletion restricted; bypass: repository admin).
- A `read:packages` token in the VM's user NuGet config for criterion 12 (never in a repo).
- Push `windows-v0.1.0` on `main` after this PR merges.
- Optional: a software licence for the repository (none today); until then the package carries none.

## Review log

**Round 1 (hard-reviewer: Fable 5.1, PASS with 1 MAJOR, 2026-10-16).** All fixed in this revision.
1. MAJOR: `check-key-sets.py` could fail open on an entry in a form its pattern missed, and group 5 was outside the HARD review. Fixed: whole-body strict grammar, leftover = fail, "unrecognised form" and "missing declaration" fixtures, fixture paths as arguments, `python -I` in the workflow, groups 2–6 reviewed, mutation check (g).
2. MINOR: a text that decodes to zero bytes but is not empty was settled silently. Fixed: "Spec gap", decided by the owner (malformed) and written into the spec.
3. MINOR: the mutation checks missed the costliest defects. Fixed: (e) test-key default, (f) the 4096 boundary; the `InternalsVisibleTo` note.
4. MINOR: `.snupkg` is not accepted by GitHub's registry; the tag was not checked to be on `main`. Fixed: embedded symbols, `--no-symbols`, the `merge-base` check.
5. MINOR: criterion 8 lacked `dotnet list package`. Fixed.
6. NIT: confirm the SDK version (10.0.402 is the one installed); `Products` as a `FrozenSet`. Fixed.
