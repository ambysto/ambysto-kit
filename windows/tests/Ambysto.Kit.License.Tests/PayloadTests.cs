using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>Payload fields, products, days, identity and the reasons' messages.</summary>
[TestClass]
public sealed class PayloadTests
{
    private static LicensePayload ParseVector(string name)
    {
        var bytes = CrockfordText.Decode(Vector.All.First(v => v.Name == name).Text);
        Assert.IsNotNull(bytes);
        var payload = LicensePayload.Parse(bytes, out var reason);
        Assert.IsNull(reason);
        Assert.IsNotNull(payload);
        return payload;
    }

    [TestMethod]
    public void TheMessagesEqualTheSpecTable()
    {
        // Intent criterion 6: read the "Reasons" table of docs/spec/license.md and compare character for character.
        // A Windows checkout may have CRLF line endings; the table is compared on its text, not its line breaks.
        var spec = System.IO.File.ReadAllText(SpecFiles.Path("docs/spec/license.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var section = spec[spec.IndexOf("## Reasons", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 1, StringComparison.Ordinal)];
        var rows = Regex.Matches(section, @"^\| ([a-z ]+) \| (.+) \|$", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.HasCount(5, rows);
        Assert.AreEqual(rows["malformed"], LicenseReason.Malformed.Message());
        Assert.AreEqual(rows["unsupported version"], LicenseReason.UnsupportedVersion.Message());
        Assert.AreEqual(rows["unknown signing key"], LicenseReason.UnknownSigningKey.Message());
        Assert.AreEqual(rows["bad signature"], LicenseReason.BadSignature.Message());
        Assert.AreEqual(rows["revoked"], LicenseReason.Revoked.Message());
    }

    [TestMethod]
    public void ProductIdsAndNamesFollowTheSpec()
    {
        var ids = Enum.GetValues<Product>().Select(p => ((int)p, p.Id(), p.DisplayName())).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            (0, "optimer", "Ambysto Optimer"), (1, "steady", "Ambysto Steady"), (2, "space", "Ambysto Lean"),
            (3, "devbox", "Ambysto DevBox"), (4, "power", "Ambysto Power"), (5, "tune", "Ambysto Tune"),
            (6, "gate", "Ambysto Gate"),
        }, ids);
    }

    [TestMethod]
    public void UnknownProductBitsAreIgnored()
    {
        var products = LicensePayload.ProductsFrom((1u << 2) | (1u << 20) | (1u << 31));
        CollectionAssert.AreEquivalent(new[] { Product.Space }, products.ToArray());
        Assert.IsEmpty(LicensePayload.ProductsFrom(0));
    }

    [TestMethod]
    public void FieldsAndDaysFromTheValidSingleAppVector()
    {
        var license = ParseVector("valid single app").ToLicense();
        Assert.AreEqual("Nguyen Van A", license.Licensee);
        Assert.AreEqual("0102030405060708", license.KeyId);
        Assert.AreEqual(new DateOnly(2026, 1, 1).AddDays(282), license.Issued);
        Assert.IsNull(license.UpdatesUntil);
        Assert.AreEqual(3, license.Seats);
        Assert.AreEqual(0, license.SigningKeyId);
        Assert.IsTrue(license.Covers(Product.Space));
        Assert.IsFalse(license.Covers(Product.Optimer));
        Assert.IsTrue(license.AllowsUpdates(new DateOnly(2205, 1, 1)));   // updates for life
    }

    [TestMethod]
    public void UpdatesUntilIsInclusive()
    {
        var license = ParseVector("valid with updates until").ToLicense();
        Assert.IsNotNull(license.UpdatesUntil);
        var until = license.UpdatesUntil.Value;
        Assert.IsTrue(license.AllowsUpdates(until));
        Assert.IsTrue(license.AllowsUpdates(until.AddDays(-1)));
        Assert.IsFalse(license.AllowsUpdates(until.AddDays(1)));
    }

    [TestMethod]
    public void EqualityIsByKeyIdOnly()
    {
        var a = ParseVector("valid single app").ToLicense();
        var b = ParseVector("lowercase, spaces and line breaks").ToLicense();
        var c = ParseVector("valid suite").ToLicense();
        Assert.AreEqual(a.KeyId == b.KeyId, a.Equals(b));
        Assert.AreEqual(a.KeyId == c.KeyId, a.Equals(c));
        Assert.IsTrue(a.Equals(a));
        Assert.IsFalse(a.Equals(null));
        if (a.Equals(b))
        {
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }
    }

    [TestMethod]
    public void ParsingStepsTwoToFourMatchTheVectors()
    {
        // Steps 2–4 alone: every vector the vectors say is unsupported version or malformed after decoding.
        foreach (var vector in Vector.All)
        {
            var bytes = CrockfordText.Decode(vector.Text);
            if (bytes is null)
            {
                Assert.AreEqual("malformed", vector.ExpectError, vector.Name);
                continue;
            }
            LicensePayload.Parse(bytes, out var reason);
            switch (vector.ExpectError)
            {
                case "unsupportedVersion":
                    Assert.AreEqual(LicenseReason.UnsupportedVersion, reason, vector.Name);
                    break;
                case "malformed":
                    Assert.AreEqual(LicenseReason.Malformed, reason, vector.Name);
                    break;
                default:
                    Assert.IsNull(reason, vector.Name);   // valid, or refused only at steps 5 to 7
                    break;
            }
        }
    }
}
