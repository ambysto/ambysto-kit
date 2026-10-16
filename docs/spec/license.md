# Ambysto license keys — format 1

Platform-neutral. Every Ambysto app on every platform verifies these keys the same way. Each platform implements this document and `license-test-vectors.json`; the macOS implementation lives in this repository (see the macOS note).

**Purpose.** A license is an honesty-based deterrent (the WinRAR model): it shows "Registered to <name>" and can be withdrawn. It is not copy protection, and apps make no attempt to resist cracking.

## Products

A license lists products in a 32-bit mask. Positions never change.

| Bit | Product id | App |
|---|---|---|
| 0 | `optimer` | Ambysto Optimer |
| 1 | `steady` | Ambysto Steady |
| 2 | `space` | Ambysto Lean |
| 3 | `devbox` | Ambysto DevBox |
| 4 | `power` | Ambysto Power |
| 5 | `tune` | Ambysto Tune |
| 6 | `gate` | Ambysto Gate |

The App column is the name users see. Product ids and bits are fixed from the first release and never follow a rename: `space` is shown as Ambysto Lean since 2026-10-15. Bit 6 is shown as Ambysto Gate since 2026-10-12 (was Ambysto Guard); its id was `guard` until 2026-10-15, when it became `gate` before any release. The bit did not move. New products take the next free bit in a minor Kit release. **Verifiers ignore bits they do not know**, so apps built before a product existed still accept newer suite keys and see only the products they know. One key unlocks an app on every platform; `seats` counts machines across platforms and is informational.

## Payload (big-endian)

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | format version = `1` |
| 1 | 1 | signing key id: which trusted public key verifies the key; `0` is the published test key |
| 2 | 8 | license key id (random). **The identity of a license** (revocation, ledger, support). Written as 16 lowercase hex characters |
| 10 | 2 | issued: whole days since 2026-01-01 UTC |
| 12 | 2 | updates until: days since 2026-01-01 UTC, inclusive; `0` = updates for life; otherwise ≥ issued |
| 14 | 1 | seats (informational) |
| 15 | 4 | products mask (see above) |
| 19 | 1 | licensee length *n*, 1–64 |
| 20 | *n* | licensee: UTF-8 in Unicode NFC (normalised by the issuer). Verifiers decode strictly — invalid UTF-8 is malformed — and never re-normalise |

The key is `payload ‖ signature`: exactly **20 + n + 64** bytes. Any other length is malformed.

## Signature

ECDSA over NIST P-256 with SHA-256. The signed message is the 17 ASCII bytes `AMBYSTO-LICENSE-1` followed by the payload. The signature is 64 bytes, raw `r ‖ s` (IEEE P1363), each half a 32-byte big-endian integer left-padded with zeros. High-S signatures are valid. Because several texts (case, separators, `O/I/L`, a high-S twin) can carry the same license, **nothing may use the text, the signature or a hash of them as an identity**; the license key id is the identity.

Public keys are 64 bytes raw `x ‖ y`.

## Text

`AMB-` followed by the Crockford Base32 of the key bytes, upper case, in groups of five joined by `-`, no check symbol. Alphabet: `0123456789ABCDEFGHJKMNPQRSTVWXYZ`.

Parsing uses **ASCII only**:
1. Remove `-`, space, tab, CR and LF.
2. Remove a leading `AMB` in any case. (Format-1 data always starts with `0`, so this is unambiguous.)
3. Every remaining character must be an ASCII letter or digit. Upper-case it; read `O` as `0` and `I`, `L` as `1`; `U` and anything else is malformed.
4. A length whose remainder modulo 8 is 1, 3 or 6 is malformed. The unused bits of the last symbol must be zero, otherwise malformed.

## File

An `.ambystokey` file holds the text in UTF-8, optionally followed by a newline, at most **4096 bytes**; a leading UTF-8 byte order mark (`EF BB BF`) is ignored; a file that is not valid UTF-8 is malformed. The 4096 bytes are counted on the raw file, the byte order mark included. A file that is larger or missing, or that cannot be read, is malformed too; so is a UTF-16 byte order mark, which is not valid UTF-8. Uniform type identifier `com.ambysto.license-key`, conforming to `public.data`. Apps declare it as an imported type and as a document type. With several Ambysto apps installed, the system opens the file with one of them; that app explains which apps the key covers if it does not cover itself. A suite key activates only the app it was opened in; each app is activated separately.

## Verification order

1. Text → bytes (rules above) — else **malformed**.
2. Version byte is known — else **unsupported version**.
3. Exact length and licensee length 1–64 — else **malformed**.
4. Licensee is valid UTF-8 — else **malformed**.
5. Signing key id is trusted by this build — else **unknown signing key**.
6. Signature verifies — else **bad signature**.
7. License key id is not revoked — else **revoked**.

## Issuer rules (verifiers do not enforce them)

The issuer refuses to create a license whose licensee is empty, longer than 64 bytes, or has control or text-direction characters; that lists no product; that has fewer than 1 seat; or whose updates-until day is before its issued day. Verifiers **do not** re-check these rules: a correctly signed key that breaks one still verifies (vectors "issuer rule only"). This keeps every platform's result identical.

## Reasons (shown to the user)

| Reason | Message |
|---|---|
| malformed | This key was mistyped or damaged. Paste it again, or open the .ambystokey file. |
| unsupported version | This key needs a newer version of the app. |
| unknown signing key | This key was not issued by Ambysto, or needs a newer version of the app. |
| bad signature | This key was changed after it was issued. |
| revoked | This key has been withdrawn. Contact Ambysto support. |

## Versioning

The issuer always writes the lowest format version that can express the license. A verifier refuses a version it does not know. A new field that old verifiers must not ignore requires a new format version and a major Kit release. Adding a product or a revoked key id is a minor or patch release.

## Rotation and revocation

- Each release lists trusted public keys by signing key id. A new signing key gets the next id and is added in a Kit release; keys signed by an older trusted id keep working.
- Each release lists revoked license key ids.
- **A compromised signing key:** remove its id from the trusted set in the next release and re-issue every license in the issuer's ledger under a new signing key.

## The published test key (id 0)

Published on purpose so other implementations can test against the vectors. **Release builds of apps never trust it.**

- Private key (32 bytes, hex): `f8de81f5c79f55a23fb098212cd946c8655dba4139b180d7b2d9a2d65c683d8e`
- Public key (`x ‖ y`, hex): `3ccd24def13196dc0f90a3f2b94f180276d17fc24a5fa5388ecfa9508c87e5830aa31040704d5454ffd188c08994a46d0e41235cc249b181199492882ae479be`

## Test vectors

`license-test-vectors.json`: each vector has the text, the payload, the signed message and the signature in hex, the trusted keys and revoked ids to use, and the expected result (the license fields, or the reason). ECDSA signatures are randomised, so regenerating the file produces different signatures for the same cases; implementations compare results, never signatures. `scripts/check-vectors.py` checks every valid vector with OpenSSL.

## Windows note

.NET: `ECDsa.Create()`, then `ImportParameters` with `Curve = ECCurve.NamedCurves.nistP256` and `Q.X`, `Q.Y` = the two halves of the raw public key; verify with `VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`. Decode the licensee with `new UTF8Encoding(false, true)` so invalid bytes throw instead of being replaced. Read an `.ambystokey` file as raw bytes, never through a reader that detects the encoding. Days count in UTC. Library `Ambysto.Kit.License` (NuGet package of the same name, from `windows-v*` tags).

## macOS note

Swift: CryptoKit `P256.Signing.PublicKey(rawRepresentation:)` and `ECDSASignature(rawRepresentation:)`; library `AmbystoLicense` (`LicenseVerifier`, `License`, `Product`).
