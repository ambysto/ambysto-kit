using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 8 ("Windows: recoverable moves"): the bin off or disallowed, too large, full, and the order
/// of the checks inside the step. Settings are this user's own (restored by each test); the policy a standard user
/// cannot set goes through the seam here and through the real machine in AdminSetupTests.</summary>
[TestClass]
public sealed class RecycleBinTests
{
    private const long MiB = 1 << 20;
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("bin");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void NukeOnDeleteRefusesAsRecycleBinOff()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var file = folder.File("item.bin", MiB);
        settings.SetNukeOnDelete(true);
        Expect.Refused(Gates.Check(file), RefusalReason.RecycleBinOff);
        settings.SetNukeOnDelete(false);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheBinSettingIsPerVolume()
    {
        Volumes.RequireSecondNtfs();
        using var second = TempFolder.OnDrive(Volumes.SecondNtfs, "bin");
        using var settings = new RecycleBinVolumeSettings(Volumes.SecondNtfs);
        var onE = second.File("item.bin", MiB);
        var onC = folder.File("item.bin", MiB);
        settings.SetNukeOnDelete(true);
        Expect.Refused(Gates.Check(onE), RefusalReason.RecycleBinOff);
        Expect.Approved(Gates.Check(onC), Win32.FinalPath(onC));
    }

    /// <summary>Spec (measured): "When the size setting is absent, the shell writes its default on its first query of
    /// that volume's Recycle Bin; the gate lets it do so and reads the value". The default leaves room for 1 MiB, and
    /// after the gate's check the value exists again, both with the value deleted and with the whole key deleted.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAbsentSizeSettingIsTheShellsDefaultWrittenOnTheGatesQuery()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var file = folder.File("item.bin", MiB);
        settings.DeleteValue(RecycleBinVolumeSettings.MaxCapacityName);
        settings.DeleteValue(RecycleBinVolumeSettings.NukeOnDeleteName);
        Assert.IsFalse(settings.ValueExists(RecycleBinVolumeSettings.MaxCapacityName), "precondition: the size setting is gone");
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Assert.IsTrue(settings.ValueExists(RecycleBinVolumeSettings.MaxCapacityName), "the shell's default was not written by the gate's query");

        settings.DeleteKey();
        Assert.IsFalse(settings.KeyExists, "precondition: the volume's key is gone");
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Assert.IsTrue(settings.ValueExists(RecycleBinVolumeSettings.MaxCapacityName), "the shell's default was not written when the key was absent");
    }

    /// <summary>"A setting that cannot be read refuses."</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnUnreadableSettingRefusesAsCouldNotVerify()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var file = folder.File("item.bin", MiB);
        settings.SetMaxCapacityMegabytes(1024);
        settings.DenyRead();
        Expect.CouldNotVerify(Gates.Check(file));
        settings.AllowRead();
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APolicyThatDisallowsTheRecycleBinRefusesAsOff()
    {
        var file = folder.File("item.bin", MiB);
        Expect.Refused(Gates.Check(file, policy: FakeRecycleBinPolicy.Disallowed), RefusalReason.RecycleBinOff);
        Expect.Approved(Gates.Check(file, policy: FakeRecycleBinPolicy.Allowed), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnUnreadablePolicyRefusesAsCouldNotVerify()
    {
        var file = folder.File("item.bin", MiB);
        Expect.CouldNotVerify(Gates.Check(file, policy: FakeRecycleBinPolicy.Unreadable));
    }

    /// <summary>The real policy reader on a machine without the policy: false (absent means allowed), never null.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void WithoutAPolicyTheRealReaderReportsAllowed()
        => Assert.AreEqual(false, SystemRecycleBinPolicy.Instance.NoRecycleFiles);

    private const long Margin = 64 * 1024;

    /// <summary>Spec (measured): "sizes are logical lengths (not the space on disk), the size setting is in MiB, and the
    /// Recycle Bin and its room are this user's own". Too large is more than the whole size; full is more than the room
    /// left without purging, the room made real by recycling one of the test's own files first. Margins are 64 KiB
    /// around the exact MiB arithmetic (the bin's own bookkeeping files).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TooLargeIsTheBinsWholeSizeInMiBAndFullIsTheRoomLeftWithoutPurging()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        using var recycled = RecycledFixture.Recycle(folder.File("eight.bin", 8 * MiB));
        var used = Win32.RecycleBinBytes(Volumes.System);
        var capacity = (int)((used + 3 * MiB) / MiB);
        Assert.IsTrue(capacity <= 300, $"this user's Recycle Bin on C: holds {used} bytes; empty it before this test can size the bin");
        settings.SetMaxCapacityMegabytes(capacity);
        var whole = capacity * MiB;
        var room = whole - used;
        Assert.IsTrue(room > 2 * MiB && room <= 3 * MiB, $"room {room}");

        var fits = folder.File("fits.bin", room - Margin);
        var full = folder.File("full.bin", room + Margin);
        var atWhole = folder.File("at-whole.bin", whole);
        var tooLarge = folder.File("too-large.bin", whole + 1);
        var fullFolder = folder.Dir("full-in-two");
        folder.File(@"full-in-two\a.bin", room / 2);
        folder.File(@"full-in-two\b.bin", room / 2 + Margin);
        var fitsFolder = folder.Dir("fits-in-two");
        folder.File(@"fits-in-two\a.bin", room / 2 - Margin);
        folder.File(@"fits-in-two\b.bin", room / 2 - Margin);

        Expect.Approved(Gates.Check(fits), Win32.FinalPath(fits));
        Expect.Refused(Gates.Check(full), RefusalReason.RecycleBinFull);
        Expect.Refused(Gates.Check(atWhole), RefusalReason.RecycleBinFull);
        Expect.Refused(Gates.Check(tooLarge), RefusalReason.TooLargeForRecycleBin);
        Expect.Refused(Gates.Check(fullFolder), RefusalReason.RecycleBinFull);
        var approved = Expect.Approved(Gates.Check(fitsFolder), Win32.FinalPath(fitsFolder));
        Assert.AreEqual(room - 2 * Margin, approved.LogicalBytes);
        Assert.AreEqual(2, approved.FileCount);
    }

    /// <summary>"Sizes are logical lengths (not the space on disk)": a sparse file and an NTFS-compressed file whose
    /// lengths exceed the room are refused although they use little disk space; one beyond the whole size is too large.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheLogicalLengthCountsNotTheSpaceOnDisk()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        using var recycled = RecycledFixture.Recycle(folder.File("eight.bin", 8 * MiB));
        var used = Win32.RecycleBinBytes(Volumes.System);
        var capacity = (int)((used + 3 * MiB) / MiB);
        settings.SetMaxCapacityMegabytes(capacity);
        var whole = capacity * MiB;
        var room = whole - used;

        var sparseFull = folder.Combine("sparse-full.bin");
        Win32.CreateSparseFile(sparseFull, room + Margin);
        var sparseTooLarge = folder.Combine("sparse-too-large.bin");
        Win32.CreateSparseFile(sparseTooLarge, whole + 1);
        var sparseFits = folder.Combine("sparse-fits.bin");
        Win32.CreateSparseFile(sparseFits, room - Margin);
        var compressed = folder.File("compressed.bin", room + Margin, fill: 0);
        Win32.SetNtfsCompression(compressed);
        Assert.AreEqual(Win32.AttrCompressed, Win32.Attributes(compressed) & Win32.AttrCompressed, "the file was not compressed");

        var approved = Expect.Approved(Gates.Check(sparseFits), Win32.FinalPath(sparseFits));
        Assert.AreEqual(room - Margin, approved.LogicalBytes, "the logical length");
        Assert.IsTrue(approved.AllocatedBytes < MiB, "a sparse file uses little disk space: " + approved.AllocatedBytes);
        Expect.Refused(Gates.Check(sparseFull), RefusalReason.RecycleBinFull);
        Expect.Refused(Gates.Check(sparseTooLarge), RefusalReason.TooLargeForRecycleBin);
        Expect.Refused(Gates.Check(compressed), RefusalReason.RecycleBinFull);
    }

    // The orderings "bin off before a contained special file" and "contained special file before a long path" need a
    // volume mount point (the only special file a folder is still refused for): AdminSetupTests.

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ALongPathIsDecidedBeforeTooLarge()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        settings.SetMaxCapacityMegabytes(1);
        var parent = folder.Dir("parent");
        folder.File(@"parent\big.bin", 3 * MiB);
        Expect.Refused(Gates.Check(parent), RefusalReason.TooLargeForRecycleBin);
        LongPaths.CreateFile(Win32.FinalPath(parent), 260);
        Expect.Refused(Gates.Check(parent), RefusalReason.PathTooLong);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheBinOffIsDecidedBeforeTooLarge()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        settings.SetMaxCapacityMegabytes(1);
        var file = folder.File("big.bin", 3 * MiB);
        Expect.Refused(Gates.Check(file), RefusalReason.TooLargeForRecycleBin);
        settings.SetNukeOnDelete(true);
        Expect.Refused(Gates.Check(file), RefusalReason.RecycleBinOff);
    }
}
