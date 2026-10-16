using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 6: same volume serial and file id, same last-write time, same length for files.</summary>
[TestClass]
public sealed class ChangedTests
{
    private TempFolder folder = null!;
    private string file = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("changed");
        file = folder.File("shown.txt", 100);
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADifferentFileIdIsChangedSinceScan()
    {
        var other = folder.File("other.txt", 100);
        var stamp = Gates.StampOf(file) with { Identity = Gates.StampOf(other).Identity };
        Expect.Refused(Gates.CheckWith(file, stamp), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADifferentLastWriteTimeIsChangedSinceScan()
    {
        var stamp = Gates.StampOf(file);
        Expect.Refused(Gates.CheckWith(file, stamp with { LastWriteTimeUtcTicks = stamp.LastWriteTimeUtcTicks + 1 }), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADifferentLengthIsChangedSinceScan()
    {
        var stamp = Gates.StampOf(file);
        Expect.Refused(Gates.CheckWith(file, stamp with { Length = stamp.Length + 1 }), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSameStampIsApproved()
    {
        var request = Gates.Request(file);
        var approved = Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
        Assert.AreEqual(request.ShownStamp, approved.Stamp);
        Assert.AreEqual(100, approved.LogicalBytes);
        Assert.AreEqual(1, approved.FileCount);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWrittenAfterTheScanIsChangedSinceScan()
    {
        var request = Gates.Request(file);
        File.AppendAllText(file, "x");
        Expect.Refused(Gates.Seamed().Check(request), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileTouchedAfterTheScanIsChangedSinceScan()
    {
        var request = Gates.Request(file);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(2));
        Expect.Refused(Gates.Seamed().Check(request), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileRecreatedUnderTheSameNameIsChangedSinceScan()
    {
        var request = Gates.Request(file);
        var content = File.ReadAllBytes(file);
        var time = File.GetLastWriteTimeUtc(file);
        File.Delete(file);
        File.WriteAllBytes(file, content);
        File.SetLastWriteTimeUtc(file, time);
        Expect.Refused(Gates.Seamed().Check(request), RefusalReason.ChangedSinceScan);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderStampIsComparedWithoutALength()
    {
        var dir = folder.Dir("sub");
        folder.File(@"sub\inner.txt", 10);
        var stamp = Gates.StampOf(dir);
        Assert.IsNull(stamp.Length);
        Expect.Approved(Gates.CheckWith(dir, stamp), Win32.FinalPath(dir));
        Expect.Refused(Gates.CheckWith(dir, stamp with { LastWriteTimeUtcTicks = stamp.LastWriteTimeUtcTicks - 1 }), RefusalReason.ChangedSinceScan);
    }

    /// <summary>Step 6 runs before step 8: a changed item on a volume whose bin is off is "changed".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheStampIsComparedBeforeTheRecycleBin()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        settings.SetNukeOnDelete(true);
        var stamp = Gates.StampOf(file);
        Expect.Refused(Gates.CheckWith(file, stamp with { Length = 1 }), RefusalReason.ChangedSinceScan);
    }
}
