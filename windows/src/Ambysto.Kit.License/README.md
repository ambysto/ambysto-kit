# Ambysto.Kit.License

Offline verification of Ambysto license keys for Windows apps of the Ambysto Pro Suite. It implements `docs/spec/license.md` (format 1) of [ambysto/ambysto-kit](https://github.com/ambysto/ambysto-kit): `AMB-` key texts and `.ambystokey` files, ECDSA P-256 signatures, a compiled-in list of trusted signing keys and revoked licenses. No network, no device identifier, no writes.

A license is an honesty-based deterrent ("Registered to <name>"), not copy protection.

```csharp
using Ambysto.Kit.Licensing;

var result = new LicenseVerifier().Verify(pastedText);   // or .VerifyFile(path)
if (result.IsValid && result.License.Covers(Product.Space))
{
    // Pro is on. Show "Registered to " + result.License.Licensee.
}
else if (!result.IsValid)
{
    // Show result.Reason.Value.Message().
}
```

`License.KeyId` is the license's identity; two licenses are equal when their key ids are equal. `AllowsUpdates(builtOn)` tells whether a build made on that UTC day is covered.
