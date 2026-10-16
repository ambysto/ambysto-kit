using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>The <c>.ambystokey</c> file rules (docs/spec/license.md, "File"): intent criterion 4. Temp files only.</summary>
[TestClass]
public sealed class FileTests
{
    private static readonly Vector ValidVector = Vector.All.First(v => v.Name == "valid single app");
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private string folder = "";

    [TestInitialize]
    public void CreateFolder()
    {
        folder = Path.Combine(Path.GetTempPath(), "ambysto-kit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(folder, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static LicenseResult VerifyFile(string path) => TestKey.Verifier(ValidVector).VerifyFile(path);

    /// <summary>The valid key, padded with trailing spaces (which parsing drops) to exactly <paramref name="total"/> bytes.</summary>
    private static byte[] Padded(int total, bool bom)
    {
        var key = Encoding.ASCII.GetBytes(ValidVector.Text);
        var prefix = bom ? Bom : [];
        var padding = Enumerable.Repeat((byte)' ', total - prefix.Length - key.Length);
        return [.. prefix, .. key, .. padding];
    }

    [TestMethod]
    public void ExactlyFourKilobytesAreAcceptedAndOneMoreIsNot()
    {
        foreach (var bom in new[] { false, true })
        {
            var at = Padded(LicenseVerifier.MaxFileBytes, bom);
            var over = Padded(LicenseVerifier.MaxFileBytes + 1, bom);
            Assert.HasCount(4096, at);
            Assert.IsTrue(VerifyFile(Write($"at-{bom}.ambystokey", at)).IsValid, $"bom {bom}");
            Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write($"over-{bom}.ambystokey", over)).Reason, $"bom {bom}");
        }
    }

    [TestMethod]
    public void AKeyWithABomOrATrailingNewlineVerifies()
    {
        var key = Encoding.ASCII.GetBytes(ValidVector.Text);
        Assert.IsTrue(VerifyFile(Write("plain.ambystokey", key)).IsValid);
        Assert.IsTrue(VerifyFile(Write("bom.ambystokey", [.. Bom, .. key])).IsValid);
        Assert.IsTrue(VerifyFile(Write("lf.ambystokey", [.. key, (byte)'\n'])).IsValid);
        Assert.IsTrue(VerifyFile(Write("crlf.ambystokey", [.. Bom, .. key, (byte)'\r', (byte)'\n'])).IsValid);
    }

    [TestMethod]
    public void NotUtf8IsMalformed()
    {
        var key = Encoding.ASCII.GetBytes(ValidVector.Text);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write("ff.ambystokey", [.. key, 0xFF])).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write("utf16le.ambystokey", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(ValidVector.Text)])).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write("utf16be.ambystokey", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(ValidVector.Text)])).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write("twobom.ambystokey", [.. Bom, .. Bom, .. key])).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Write("empty.ambystokey", [])).Reason);
    }

    [TestMethod]
    public void MissingOrUnreadableIsMalformed()
    {
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(Path.Combine(folder, "missing.ambystokey")).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile(folder).Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile("").Reason);
        Assert.AreEqual(LicenseReason.Malformed, VerifyFile("NUL").Reason);
        var locked = Write("locked.ambystokey", Encoding.ASCII.GetBytes(ValidVector.Text));
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(LicenseReason.Malformed, VerifyFile(locked).Reason);
        }
        Assert.IsTrue(VerifyFile(locked).IsValid);
    }

    [TestMethod]
    public void NullPathIsAProgrammingError() => Assert.ThrowsExactly<ArgumentNullException>(() => new LicenseVerifier().VerifyFile(null!));
}
