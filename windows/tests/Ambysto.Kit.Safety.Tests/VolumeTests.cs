using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 5: only a fixed local NTFS or ReFS volume, decided by the resolved volume, never by the drive
/// letter. The test machine: C: NTFS system, E: NTFS fixed, F: FAT32 removable, G: exFAT fixed (the brief).</summary>
[TestClass]
public sealed class VolumeTests
{
    private TempFolder profileFolder = null!;

    [TestInitialize]
    public void Create() => profileFolder = TempFolder.InProfile("volume");

    [TestCleanup]
    public void Delete() => profileFolder.Dispose();

    /// <summary>G01, step 5: "removable and network volumes are refused" holds for a removable volume that is NTFS too
    /// (the drive-type rule, not the file-system rule). The volume is provisioned by the owner and named by
    /// %AMBYSTO_REMOVABLE_NTFS% (a drive root); without it the test is inconclusive, never a pass.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileAndAFolderOnARemovableNtfsVolumeAreRefused()
    {
        var root = Environment.GetEnvironmentVariable("AMBYSTO_REMOVABLE_NTFS");
        if (string.IsNullOrEmpty(root))
        {
            Assert.Inconclusive("AMBYSTO_REMOVABLE_NTFS is not set: no removable NTFS volume on this test machine");
        }
        var drive = new DriveInfo(root!);
        Assert.IsTrue(drive.IsReady, root + " is not ready");
        Assert.AreEqual(DriveType.Removable, drive.DriveType, "precondition: a removable volume");
        Assert.AreEqual("NTFS", drive.DriveFormat, "precondition: NTFS");
        using var removable = TempFolder.OnDrive(root!, "vol");
        var file = removable.File("on-removable-ntfs.bin", 100);
        var dir = removable.Dir("sub");
        Expect.Refused(Gates.Check(file), RefusalReason.UnsupportedVolume);
        Expect.Refused(Gates.Check(dir), RefusalReason.UnsupportedVolume);
        var control = profileFolder.File("on-c.bin", 100);
        Expect.Approved(Gates.Check(control), Win32.FinalPath(control));
    }

    /// <summary>The stamp is junk on purpose: the volume is refused at step 5, before the stamp is compared.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOnTheRemovableFat32VolumeIsRefused()
    {
        Volumes.RequireRemovableFat32();
        using var removable = TempFolder.OnDrive(Volumes.RemovableFat32, "vol");
        var file = removable.File("on-usb.bin", 100);
        Expect.Refused(Gates.CheckWith(file, Gates.AnyStamp), RefusalReason.UnsupportedVolume);
        var control = profileFolder.File("on-c.bin", 100);
        Expect.Approved(Gates.Check(control), Win32.FinalPath(control));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderOnTheRemovableVolumeIsRefused()
    {
        Volumes.RequireRemovableFat32();
        using var removable = TempFolder.OnDrive(Volumes.RemovableFat32, "vol");
        var dir = removable.Dir("sub");
        Expect.Refused(Gates.CheckWith(dir, Gates.AnyStamp), RefusalReason.UnsupportedVolume);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOnTheFixedExFatVolumeIsRefused()
    {
        Volumes.RequireFixedExFat();
        using var exfat = TempFolder.OnDrive(Volumes.FixedExFat, "vol");
        var file = exfat.File("on-exfat.bin", 100);
        Expect.Refused(Gates.CheckWith(file, Gates.AnyStamp), RefusalReason.UnsupportedVolume);
    }

    /// <summary>A second fixed NTFS volume passes step 5: the Kit's gate does not require the profile's volume (that
    /// is an app's allowed-root rule).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOnTheSecondFixedNtfsVolumeIsApproved()
    {
        Volumes.RequireSecondNtfs();
        using var second = TempFolder.OnDrive(Volumes.SecondNtfs, "vol");
        var file = second.File("on-e.bin", 100);
        var approved = Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Assert.AreNotEqual(approved.Facts.SystemVolumeSerial, approved.Facts.VolumeSerial);
        Assert.AreEqual(approved.Stamp.Identity.VolumeSerial, approved.Facts.VolumeSerial);
    }

    /// <summary>The decision follows the resolved volume: a subst letter onto the removable volume is refused, the
    /// same letter onto the profile is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ASubstLetterIsJudgedByTheVolumeBehindIt()
    {
        Volumes.RequireRemovableFat32();
        using var removable = TempFolder.OnDrive(Volumes.RemovableFat32, "vol");
        removable.File("on-usb.bin", 100);
        using (var subst = new SubstDrive(removable.Path))
        {
            Expect.Refused(Gates.CheckWith(subst.Combine("on-usb.bin"), Gates.AnyStamp), RefusalReason.UnsupportedVolume);
        }
        var file = profileFolder.File("on-c.bin", 100);
        using (var subst = new SubstDrive(profileFolder.Path))
        {
            Expect.Approved(Gates.Check(subst.Combine("on-c.bin")), Win32.FinalPath(file));
        }
    }
}
