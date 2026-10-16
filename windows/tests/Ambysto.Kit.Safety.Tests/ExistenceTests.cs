using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 2 ("Already gone") and its boundaries: the spelling rules run before it, the volume after it.</summary>
[TestClass]
public sealed class ExistenceTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("exists");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMissingFileIsAlreadyGone()
        => Expect.Refused(Gates.CheckWith(folder.Combine("missing.txt"), Gates.AnyStamp), RefusalReason.AlreadyGone);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMissingFolderChainIsAlreadyGone()
        => Expect.Refused(Gates.CheckWith(folder.Combine(@"no\such\file.txt"), Gates.AnyStamp), RefusalReason.AlreadyGone);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileThatVanishedAfterTheScanIsAlreadyGone()
    {
        var file = folder.File("vanishes.txt");
        var request = Gates.Request(file);
        File.Delete(file);
        Expect.Refused(Gates.Seamed().Check(request), RefusalReason.AlreadyGone);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnExistingFileIsApproved()
    {
        var file = folder.File("present.txt", 100);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnExistingEmptyFolderIsApproved()
    {
        var dir = folder.Dir("empty");
        var approved = Expect.Approved(Gates.Check(dir), Win32.FinalPath(dir));
        Assert.AreEqual(0, approved.FileCount);
        Assert.AreEqual(0, approved.LogicalBytes);
        Assert.IsNull(approved.Stamp.Length);
    }

    /// <summary>Spec step 2: "a network path to a missing file is refused for its form, not as gone".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANetworkPathToAMissingFileIsRefusedForItsFormNotAsGone()
        => Expect.Refused(Gates.CheckWith(MappedDrive.Share + "\\" + TempFolder.RootName + "\\missing-" + Guid.NewGuid().ToString("N") + ".txt", Gates.AnyStamp), RefusalReason.UnsupportedPathForm);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ATrailingDotNameThatDoesNotExistIsRefusedForItsForm()
        => Expect.Refused(Gates.CheckWith(folder.Combine("missing."), Gates.AnyStamp), RefusalReason.UnsupportedPathForm);

    /// <summary>Step 2 runs before step 5: a missing item on the removable volume is gone, not a volume refusal.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMissingFileOnTheRemovableVolumeIsGoneBeforeTheVolumeCheck()
    {
        Volumes.RequireRemovableFat32();
        var path = Path.Combine(Volumes.RemovableFat32, TempFolder.RootName, "missing-" + Guid.NewGuid().ToString("N") + ".bin");
        Expect.Refused(Gates.CheckWith(path, Gates.AnyStamp), RefusalReason.AlreadyGone);
    }

    /// <summary>A drive-letter spelling passes the given-path form check; without a handle there is no canonical
    /// network path to refuse, so a missing file on a mapped drive is gone.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMissingFileOnAMappedDriveIsGone()
    {
        using var mapped = new MappedDrive();
        Expect.Refused(Gates.CheckWith(mapped.Combine("missing-" + Guid.NewGuid().ToString("N") + ".txt"), Gates.AnyStamp), RefusalReason.AlreadyGone);
    }
}
