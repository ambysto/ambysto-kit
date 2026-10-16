using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>The text form (docs/spec/license.md, "Text"): intent criterion 3.</summary>
[TestClass]
public sealed class TextTests
{
    private static string ValidText => Vector.All.First(v => v.Name == "valid single app").Text;

    [TestMethod]
    public void EquivalentSpellingsDecodeToTheSameBytes()
    {
        var expected = CrockfordText.Decode(ValidText);
        Assert.IsNotNull(expected);
        var body = ValidText["AMB-".Length..];
        var variants = new[]
        {
            ValidText.ToLowerInvariant(),
            ValidText.Replace("-", " ", StringComparison.Ordinal),
            ValidText.Replace("-", "\r\n", StringComparison.Ordinal),
            ValidText.Replace("-", "\t", StringComparison.Ordinal),
            "  amb-" + body + "\n",
            body,
            "Amb" + body.Replace("-", "", StringComparison.Ordinal),
            body.Replace('0', 'O').Replace('1', 'I'),
            body.Replace('1', 'L').ToLowerInvariant(),
        };
        foreach (var variant in variants)
        {
            CollectionAssert.AreEqual(expected, CrockfordText.Decode(variant), variant);
        }
    }

    [TestMethod]
    [DataRow("U")]
    [DataRow("u")]
    [DataRow("#")]
    [DataRow("\uFF10")]   // full-width digit zero
    [DataRow("\u0660")]   // Arabic-Indic digit zero
    [DataRow("\u0131")]   // dotless i
    [DataRow("\u212A")]   // Kelvin sign
    [DataRow("_")]
    [DataRow("\u00A0")]   // no-break space is not a separator
    public void ANonAlphabetCharacterIsMalformed(string bad)
    {
        // Replace one symbol in the middle, keeping the length.
        var body = ValidText["AMB-".Length..].Replace("-", "", StringComparison.Ordinal);
        var text = body[..10] + bad + body[11..];
        Assert.IsNull(CrockfordText.Decode(text), bad);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("AMB")]
    [DataRow("amb-")]
    [DataRow("-- --")]
    [DataRow(" \t\r\n")]
    public void NoSymbolsLeftIsMalformed(string text) => Assert.IsNull(CrockfordText.Decode(text));

    [TestMethod]
    public void SymbolCountsWithRemainderOneThreeOrSixAreMalformed()
    {
        for (var count = 1; count <= 24; count++)
        {
            var decoded = CrockfordText.Decode(new string('0', count));
            if ((count % 8) is 1 or 3 or 6)
            {
                Assert.IsNull(decoded, $"{count} symbols");
            }
            else
            {
                Assert.IsNotNull(decoded, $"{count} symbols");
                Assert.AreEqual(count * 5 / 8, decoded.Length);
            }
        }
    }

    [TestMethod]
    public void NonZeroUnusedBitsAreMalformed()
    {
        // Two symbols carry 10 bits: one byte and two unused bits.
        CollectionAssert.AreEqual(new byte[] { 0x09 }, CrockfordText.Decode("14"));   // 00001 00100
        Assert.IsNull(CrockfordText.Decode("15"));                                   // 00001 00101
        Assert.IsNull(CrockfordText.Decode("16"));                                   // 00001 00110
    }

    [TestMethod]
    public void TheAlphabetDecodesInOrder()
    {
        // 8 symbols = 40 bits = 5 bytes. Expected bytes computed by an independent decoder.
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x44, 0x32, 0x14, 0xC7 }, CrockfordText.Decode("01234567"));
        CollectionAssert.AreEqual(new byte[] { 0x42, 0x54, 0xB6, 0x35, 0xCF }, CrockfordText.Decode("89ABCDEF"));
        CollectionAssert.AreEqual(new byte[] { 0x84, 0x65, 0x3A, 0x56, 0xD7 }, CrockfordText.Decode("GHJKMNPQ"));
        CollectionAssert.AreEqual(new byte[] { 0xC6, 0x75, 0xBE, 0x77, 0xDF }, CrockfordText.Decode("RSTVWXYZ"));
    }
}
