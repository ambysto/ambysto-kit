using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>The whole verification order against the spec's vectors: intent criteria 1 and 5.</summary>
[TestClass]
public sealed class VerifierTests
{
    private static readonly Dictionary<string, LicenseReason> ReasonIds = new()
    {
        ["malformed"] = LicenseReason.Malformed,
        ["unsupportedVersion"] = LicenseReason.UnsupportedVersion,
        ["unknownSigningKey"] = LicenseReason.UnknownSigningKey,
        ["badSignature"] = LicenseReason.BadSignature,
        ["revoked"] = LicenseReason.Revoked,
    };

    [TestMethod]
    public void AllThirtyFiveVectorsAreRead() => Assert.HasCount(35, Vector.All);

    [TestMethod]
    public void TheTestKeyIsTheOneTheVectorsTrust()
    {
        foreach (var vector in Vector.All)
        {
            Assert.AreEqual(TestKey.PublicHex, vector.TrustedKeys[TestKey.Id], vector.Name);
        }
    }

    [TestMethod]
    public void EveryVectorVerifiesToItsDocumentedResult()
    {
        foreach (var vector in Vector.All)
        {
            var result = TestKey.Verifier(vector).Verify(vector.Text);
            if (vector.ExpectValid)
            {
                Assert.IsTrue(result.IsValid, $"{vector.Name}: {result.Reason}");
                var license = result.License;
                var expect = vector.Expect;
                Assert.AreEqual(expect.GetProperty("licensee").GetString(), license.Licensee, vector.Name);
                Assert.AreEqual(expect.GetProperty("keyID").GetString(), license.KeyId, vector.Name);
                Assert.AreEqual(LicensePayload.DayZero.AddDays(expect.GetProperty("issuedDay").GetInt32()), license.Issued, vector.Name);
                var until = expect.GetProperty("updatesUntilDay").GetInt32();
                Assert.AreEqual(until == 0 ? null : LicensePayload.DayZero.AddDays(until), license.UpdatesUntil, vector.Name);
                Assert.AreEqual(expect.GetProperty("seats").GetInt32(), license.Seats, vector.Name);
                var products = expect.GetProperty("products").EnumerateArray().Select(p => p.GetString()).Order().ToArray();
                CollectionAssert.AreEqual(products, license.Products.Select(p => p.Id()).Order().ToArray(), vector.Name);
                Assert.AreEqual(TestKey.Id, license.SigningKeyId, vector.Name);
            }
            else
            {
                Assert.IsFalse(result.IsValid, vector.Name);
                Assert.IsNotNull(vector.ExpectError, vector.Name);
                Assert.AreEqual(ReasonIds[vector.ExpectError], result.Reason, vector.Name);
            }
        }
    }

    [TestMethod]
    public void TheDefaultVerifierTrustsNoKey()
    {
        // Intent criterion 5: every vector that reaches step 5 is "unknown signing key" with the defaults;
        // the others keep their step 1-4 reason.
        var verifier = new LicenseVerifier();
        var reachedStepFive = 0;
        foreach (var vector in Vector.All)
        {
            var result = verifier.Verify(vector.Text);
            Assert.IsFalse(result.IsValid, vector.Name);
            var bytes = CrockfordText.Decode(vector.Text);
            var reaches = bytes is not null && LicensePayload.Parse(bytes, out _) is not null;
            if (reaches)
            {
                reachedStepFive++;
                Assert.AreEqual(LicenseReason.UnknownSigningKey, result.Reason, vector.Name);
            }
            else
            {
                Assert.AreEqual(ReasonIds[vector.ExpectError!], result.Reason, vector.Name);
            }
        }
        // Valid vectors and those refused at steps 5 to 7 reach step 5: 20 of the 35.
        var expected = Vector.All.Count(v => v.ExpectValid || v.ExpectError is "unknownSigningKey" or "badSignature" or "revoked");
        Assert.AreEqual(20, expected);
        Assert.AreEqual(expected, reachedStepFive);
    }

    [TestMethod]
    public void TheProductionSetIsPinnedToNoIds()
    {
        // Changes only with the owner's key PR, together with the Swift pin (scripts/check-key-sets.py).
        Assert.IsEmpty(TrustedKeys.Production.Ids);
        Assert.IsEmpty(Revocations.All);
    }

    [TestMethod]
    public void ABrokenTrustedKeyIsABuildDefect()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new TrustedKeys(new Dictionary<byte, string> { [1] = new string('0', 128) }));
        Assert.ThrowsExactly<ArgumentException>(() => new TrustedKeys(new Dictionary<byte, string> { [1] = TestKey.PublicHex[..126] }));
        Assert.ThrowsExactly<ArgumentException>(() => new TrustedKeys(new Dictionary<byte, string> { [1] = "zz" + TestKey.PublicHex[2..] }));
        Assert.ThrowsExactly<ArgumentException>(() => new TrustedKeys(new Dictionary<byte, string> { [1] = TestKey.PublicHex[..127] }));
        // A point off the curve: the test key's x twice.
        Assert.ThrowsExactly<ArgumentException>(() => new TrustedKeys(new Dictionary<byte, string> { [1] = TestKey.PublicHex[..64] + TestKey.PublicHex[..64] }));
        Assert.HasCount(1, new TrustedKeys(new Dictionary<byte, string> { [7] = TestKey.PublicHex }).Ids);
    }

    [TestMethod]
    public void RevocationIsCheckedAfterTheSignature()
    {
        var vector = Vector.All.First(v => v.Name == "valid single app");
        var license = TestKey.Verifier(vector).Verify(vector.Text).License!;
        var withRevocation = new LicenseVerifier(new TrustedKeys(vector.TrustedKeys), [license.KeyId.ToUpperInvariant()]);
        Assert.AreEqual(LicenseReason.Revoked, withRevocation.Verify(vector.Text).Reason);
    }

    [TestMethod]
    public void NullIsAProgrammingError() => Assert.ThrowsExactly<ArgumentNullException>(() => new LicenseVerifier().Verify(null!));
}
