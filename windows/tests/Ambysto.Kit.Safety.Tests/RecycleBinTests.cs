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

    /// <summary>"An absent setting means the system default."</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAbsentSettingMeansTheSystemDefault()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        settings.DeleteValue(RecycleBinVolumeSettings.MaxCapacityName);
        settings.DeleteValue(RecycleBinVolumeSettings.NukeOnDeleteName);
        var file = folder.File("item.bin", MiB);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        settings.DeleteKey();
        Assert.IsFalse(settings.KeyExists, "precondition: the volume's key is gone");
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
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

    /// <summary>Too large (more than the bin's whole size) against full (fits the size, not the room left without
    /// purging): the room is made real by recycling one of the test's own files first. Sizes keep a margin wide
    /// enough for MaxCapacity in MB or MiB.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TooLargeIsTheBinsWholeSizeAndFullIsTheRoomLeftWithoutPurging()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        using var recycled = RecycledFixture.Recycle(folder.File("eight.bin", 8 * MiB));
        var used = Win32.RecycleBinBytes(Volumes.System);
        var capacity = (int)((used + 3 * MiB) / MiB);
        Assert.IsTrue(capacity <= 300, $"this user's Recycle Bin on C: holds {used} bytes; empty it before this test can size the bin");
        settings.SetMaxCapacityMegabytes(capacity);

        var fits = folder.File("one.bin", MiB);
        var full = folder.File("four.bin", 4 * MiB);
        var tooLarge = folder.File("huge.bin", capacity * MiB + MiB);
        var fullFolder = folder.Dir("four-in-two");
        folder.File(@"four-in-two\a.bin", 2 * MiB);
        folder.File(@"four-in-two\b.bin", 2 * MiB);
        var fitsFolder = folder.Dir("one-in-one");
        folder.File(@"one-in-one\a.bin", MiB);

        Expect.Approved(Gates.Check(fits), Win32.FinalPath(fits));
        Expect.Refused(Gates.Check(full), RefusalReason.RecycleBinFull);
        Expect.Refused(Gates.Check(tooLarge), RefusalReason.TooLargeForRecycleBin);
        Expect.Refused(Gates.Check(fullFolder), RefusalReason.RecycleBinFull);
        var approved = Expect.Approved(Gates.Check(fitsFolder), Win32.FinalPath(fitsFolder));
        Assert.AreEqual(MiB, approved.LogicalBytes);
        Assert.AreEqual(1, approved.FileCount);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheBinOffIsDecidedBeforeAContainedLink()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var parent = folder.Dir("parent");
        Win32.SetJunction(folder.Dir(@"parent\junction"), folder.Dir("target"));
        settings.SetNukeOnDelete(true);
        Expect.Refused(Gates.Check(parent), RefusalReason.RecycleBinOff);
        settings.SetNukeOnDelete(false);
        Expect.Refused(Gates.Check(parent), RefusalReason.ContainsLinkOrSpecialFile);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AContainedLinkIsDecidedBeforeALongPath()
    {
        var parent = folder.Dir("parent");
        LongPaths.CreateFile(Win32.FinalPath(parent), 260);
        Expect.Refused(Gates.Check(parent), RefusalReason.PathTooLong);
        Win32.SetJunction(folder.Dir(@"parent\junction"), folder.Dir("target"));
        Expect.Refused(Gates.Check(parent), RefusalReason.ContainsLinkOrSpecialFile);
    }

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
