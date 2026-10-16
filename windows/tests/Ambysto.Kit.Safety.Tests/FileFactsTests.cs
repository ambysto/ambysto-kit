using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>FileFacts.Read: the canonical path and stamp an app records at scan time (intent criterion 4: one identity
/// through every spelling; refused input forms), compared with the system's own answers.</summary>
[TestClass]
public sealed class FileFactsTests
{
    private TempFolder folder = null!;
    private string file = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("facts");
        file = folder.File("a long file name for facts.txt", 100);
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    private static FileFactsResult ReadOk(string path)
    {
        var facts = FileFacts.Read(path);
        Assert.IsTrue(facts.Succeeded, $"FileFacts.Read({path}): {facts.FailureReason} {facts.Detail}");
        Assert.IsNull(facts.FailureReason);
        Assert.IsFalse(facts.IsCloudNotOpened);
        return facts;
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APlainFileHasItsCanonicalPathAndStamp()
    {
        var facts = ReadOk(file);
        Assert.AreEqual(Win32.FinalPath(file), facts.CanonicalPath);
        var stamp = facts.Stamp!.Value;
        Assert.AreEqual(100, stamp.Length);
        Assert.AreEqual(File.GetLastWriteTimeUtc(file).ToFileTimeUtc(), stamp.LastWriteTimeUtcTicks, "a Windows FILETIME");
        var (serial, idBytes) = Win32.FileIdInfo(file);
        Assert.AreEqual(serial, stamp.Identity.VolumeSerial);
        var littleEndian = new UInt128(BitConverter.ToUInt64(idBytes, 8), BitConverter.ToUInt64(idBytes, 0));
        var bigEndian = new UInt128(BitConverter.ToUInt64(idBytes.Reverse().ToArray(), 8), BitConverter.ToUInt64(idBytes.Reverse().ToArray(), 0));
        Assert.IsTrue(stamp.Identity.FileId == littleEndian || stamp.Identity.FileId == bigEndian, $"file id {stamp.Identity.FileId} is not FILE_ID_INFO's");
        Assert.AreNotEqual(UInt128.Zero, stamp.Identity.FileId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHasNoLength()
    {
        var dir = folder.Dir("sub");
        var facts = ReadOk(dir);
        Assert.AreEqual(Win32.FinalPath(dir), facts.CanonicalPath);
        Assert.IsNull(facts.Stamp!.Value.Length);
        Assert.AreNotEqual(ReadOk(file).Stamp!.Value.Identity, facts.Stamp.Value.Identity);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheStampFollowsTheLastWriteTimeIn100NanosecondTicks()
    {
        var before = ReadOk(file).Stamp!.Value;
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(2));
        var after = ReadOk(file).Stamp!.Value;
        Assert.AreEqual(20_000_000, after.LastWriteTimeUtcTicks - before.LastWriteTimeUtcTicks);
        Assert.AreEqual(before.Identity, after.Identity);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSameFileThroughEverySpellingHasOneIdentityAndOneCanonicalPath()
    {
        var plain = ReadOk(file);
        var identity = plain.Stamp!.Value.Identity;
        var shortPath = Win32.ShortPath(file);
        Assert.AreNotEqual(file, shortPath, "no 8.3 names on the system volume");
        using var subst = new SubstDrive(folder.Path);
        foreach (var (spelling, what) in new[]
        {
            (shortPath, "8.3 name"),
            (file.ToUpperInvariant(), "upper case"),
            (@"\\?\" + file, "extended prefix"),
            (subst.Combine(Path.GetFileName(file)), "subst drive"),
        })
        {
            var facts = ReadOk(spelling);
            Assert.AreEqual(plain.CanonicalPath, facts.CanonicalPath, what);
            Expect.SameIdentity(identity, facts.Stamp!.Value.Identity, what);
            Assert.AreEqual(plain.Stamp, facts.Stamp, what);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AHardLinkHasTheSameIdentityUnderItsOwnPath()
    {
        var link = folder.Combine("hard-link.txt");
        Win32.CreateHardLink(link, file);
        var original = ReadOk(file);
        var linked = ReadOk(link);
        Expect.SameIdentity(original.Stamp!.Value.Identity, linked.Stamp!.Value.Identity, "hard link");
        Assert.AreEqual(Win32.FinalPath(link), linked.CanonicalPath);
        Assert.AreNotEqual(original.CanonicalPath, linked.CanonicalPath);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TwoFilesHaveDifferentIdentitiesOnTheSameVolume()
    {
        var other = folder.File("other.txt", 100);
        var a = ReadOk(file).Stamp!.Value.Identity;
        var b = ReadOk(other).Stamp!.Value.Identity;
        Assert.AreEqual(a.VolumeSerial, b.VolumeSerial);
        Assert.AreNotEqual(a.FileId, b.FileId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMappedDriveGivesTheNetworkCanonicalFormAndTheSharesIdentity()
    {
        using var share = new ShareFolder();
        var name = "mapped-facts.txt";
        share.File(name, 100);
        using var mapped = new MappedDrive();
        var viaLetter = mapped.Combine(share.Relative + "\\" + name);
        var facts = ReadOk(viaLetter);
        Assert.IsTrue(facts.CanonicalPath!.StartsWith(@"\\?\UNC\", StringComparison.Ordinal), facts.CanonicalPath);
        Assert.AreEqual(Win32.FinalPath(viaLetter), facts.CanonicalPath);
        Assert.AreEqual(100, facts.Stamp!.Value.Length);
        var (serial, _) = Win32.FileIdInfo(viaLetter);
        Assert.AreEqual(serial, facts.Stamp.Value.Identity.VolumeSerial, "the serial the share reports");
        var local = ShareLocalPath();
        if (local is not null)
        {
            var localFacts = ReadOk(Path.Combine(local, share.Relative, name));
            Expect.SameIdentity(localFacts.Stamp!.Value.Identity, facts.Stamp.Value.Identity, "loopback share against the local file");
        }
    }

    /// <summary>The share's local folder, when the server's share list is readable; otherwise null.</summary>
    private static string? ShareLocalPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares");
            if (key?.GetValue("ambynet") is string[] lines)
            {
                return lines.FirstOrDefault(l => l.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))?["Path=".Length..];
            }
        }
        catch (System.Security.SecurityException)
        {
            // Not readable by this user.
        }
        return null;
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void NetworkAndDeviceSpellingsAreRefusedAsInput()
    {
        using var share = new ShareFolder();
        var unc = share.File("unc-facts.txt");
        foreach (var (path, what) in new[]
        {
            (unc, "UNC"),
            (@"\\?\UNC\" + unc[2..], "extended UNC"),
            (@"\\.\" + file, "device"),
            (@"\\?\GLOBALROOT\Device\HarddiskVolume1\x.txt", "GLOBALROOT"),
        })
        {
            var facts = FileFacts.Read(path);
            Assert.IsFalse(facts.Succeeded, what);
            Assert.AreEqual(RefusalReason.UnsupportedPathForm, facts.FailureReason, what);
            Assert.IsNull(facts.Stamp, what);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void StreamsAndTrailingDotsAreRefusedAsInput()
    {
        File.WriteAllText(file + ":alt", "stream");
        var dotted = folder.Combine("dotted.");
        File.WriteAllBytes(@"\\?\" + dotted, new byte[8]);
        try
        {
            foreach (var (path, what) in new[] { (file + ":alt", "stream"), (dotted, "trailing dot"), (@"\\?\" + dotted, "extended trailing dot") })
            {
                var facts = FileFacts.Read(path);
                Assert.IsFalse(facts.Succeeded, what);
                Assert.AreEqual(RefusalReason.UnsupportedPathForm, facts.FailureReason, what);
            }
        }
        finally
        {
            File.Delete(@"\\?\" + dotted);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMissingFileIsAlreadyGone()
    {
        var facts = FileFacts.Read(folder.Combine("missing.txt"));
        Assert.IsFalse(facts.Succeeded);
        Assert.AreEqual(RefusalReason.AlreadyGone, facts.FailureReason);
    }

    /// <summary>A link is refused and never followed (API remarks): no stamp is read through it.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AJunctionAndAnUnknownReparsePointAreRefusedAsLinks()
    {
        var target = folder.Dir("target");
        var junction = folder.Dir("junction");
        Win32.SetJunction(junction, target);
        var facts = FileFacts.Read(junction);
        Assert.IsFalse(facts.Succeeded, "a stamp was read through a junction");
        Assert.AreEqual(RefusalReason.LinkOrSpecialFile, facts.FailureReason);
        Assert.IsNull(facts.Stamp);
        var tagged = folder.File("tagged.bin", 10);
        Win32.SetUnknownReparsePoint(tagged);
        Assert.AreEqual(RefusalReason.LinkOrSpecialFile, FileFacts.Read(tagged).FailureReason);
        ReadOk(target);
    }

    /// <summary>FAT and exFAT have no FILE_ID_INFO: refused as an unsupported volume (API remarks).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void FatAndExFatFilesAreRefusedAsUnsupportedVolumes()
    {
        Volumes.RequireRemovableFat32();
        Volumes.RequireFixedExFat();
        using var removable = TempFolder.OnDrive(Volumes.RemovableFat32, "facts");
        using var exfat = TempFolder.OnDrive(Volumes.FixedExFat, "facts");
        foreach (var path in new[] { removable.File("on-usb.bin", 100), exfat.File("on-exfat.bin", 100) })
        {
            Assert.IsFalse(Win32.HasFileIdInfo(path), path + " reported FILE_ID_INFO: the volume is not what the brief says");
            var facts = FileFacts.Read(path);
            Assert.IsFalse(facts.Succeeded, path);
            Assert.AreEqual(RefusalReason.UnsupportedVolume, facts.FailureReason, path);
            Assert.IsNull(facts.Stamp, path);
        }
    }
}
