using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 4: refused spellings (network, device, stream, trailing dot or space) and the canonical path
/// of the accepted ones (8.3 names expanded, file-system case, subst resolved, no extended prefix).</summary>
[TestClass]
public sealed class PathFormTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("form");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AUncPathToAnExistingFileIsRefused()
    {
        using var share = new ShareFolder();
        var unc = share.File("on-share.txt");
        Expect.Refused(Gates.CheckWith(unc, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnExtendedUncPathIsRefused()
    {
        using var share = new ShareFolder();
        var unc = share.File("on-share.txt");
        Expect.Refused(Gates.CheckWith(@"\\?\UNC\" + unc[2..], Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADevicePathToAnExistingFileIsRefused()
    {
        var file = folder.File("plain.txt");
        Expect.Refused(Gates.CheckWith(@"\\.\" + file, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AGlobalRootPathIsRefused()
        => Expect.Refused(Gates.CheckWith(@"\\?\GLOBALROOT\Device\HarddiskVolume1\Users\x.txt", Gates.AnyStamp), RefusalReason.UnsupportedPathForm);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AStreamPathIsRefused()
    {
        var file = folder.File("streamed.txt");
        File.WriteAllText(file + ":alt", "stream");
        Expect.Refused(Gates.CheckWith(file + ":alt", Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
        Expect.Refused(Gates.CheckWith(file + "::$DATA", Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANameEndingInADotIsRefusedInBothSpellings()
    {
        var dotted = folder.Combine("dotted.");
        File.WriteAllBytes(@"\\?\" + dotted, new byte[8]);
        try
        {
            Expect.Refused(Gates.CheckWith(dotted, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
            Expect.Refused(Gates.CheckWith(@"\\?\" + dotted, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
        }
        finally
        {
            File.Delete(@"\\?\" + dotted);
        }
        var control = folder.File("dotted.txt");
        Expect.Approved(Gates.Check(control), Win32.FinalPath(control));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANameEndingInASpaceIsRefusedInBothSpellings()
    {
        var spaced = folder.Combine("spaced ");
        File.WriteAllBytes(@"\\?\" + spaced, new byte[8]);
        try
        {
            Expect.Refused(Gates.CheckWith(spaced, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
            Expect.Refused(Gates.CheckWith(@"\\?\" + spaced, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
        }
        finally
        {
            File.Delete(@"\\?\" + spaced);
        }
    }

    /// <summary>"Names ending in a dot or a space": a middle component counts too.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMiddleComponentEndingInADotOrASpaceIsRefused()
    {
        var dottedDir = folder.Combine("dotted.");
        var spacedDir = folder.Combine("spaced ");
        Directory.CreateDirectory(@"\\?\" + dottedDir);
        Directory.CreateDirectory(@"\\?\" + spacedDir);
        var underDot = dottedDir + "\\inner.txt";
        var underSpace = spacedDir + "\\inner.txt";
        File.WriteAllBytes(@"\\?\" + underDot, new byte[8]);
        File.WriteAllBytes(@"\\?\" + underSpace, new byte[8]);
        try
        {
            Expect.Refused(Gates.CheckWith(underDot, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
            Expect.Refused(Gates.CheckWith(@"\\?\" + underDot, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
            Expect.Refused(Gates.CheckWith(underSpace, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
            Expect.Refused(Gates.CheckWith(@"\\?\" + underSpace, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
        }
        finally
        {
            File.Delete(@"\\?\" + underDot);
            File.Delete(@"\\?\" + underSpace);
            Directory.Delete(@"\\?\" + dottedDir);
            Directory.Delete(@"\\?\" + spacedDir);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMappedDriveResolvesToANetworkPathAndIsRefused()
    {
        using var share = new ShareFolder();
        var name = "mapped.txt";
        share.File(name);
        using var mapped = new MappedDrive();
        var viaLetter = mapped.Combine(share.Relative + "\\" + name);
        Assert.IsTrue(File.Exists(viaLetter), "the mapped drive does not show the share's file");
        Expect.Refused(Gates.CheckWith(viaLetter, Gates.AnyStamp), RefusalReason.UnsupportedPathForm);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnExtendedDrivePathIsApprovedWithThePlainCanonicalPath()
    {
        var file = folder.File("plain.txt", 100);
        var canonical = Win32.FinalPath(file);
        Assert.IsFalse(canonical.StartsWith(@"\\?\", StringComparison.Ordinal));
        Expect.Approved(Gates.Check(@"\\?\" + file), canonical);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AShortNameSpellingIsApprovedWithTheLongCanonicalPath()
    {
        var file = folder.File("a long file name to shorten.txt", 100);
        var shortPath = Win32.ShortPath(file);
        Assert.AreNotEqual(file, shortPath, "the system volume has no 8.3 names: the test machine is not set up as the brief says");
        Expect.Approved(Gates.Check(shortPath), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnotherCaseIsApprovedWithTheFileSystemsCase()
    {
        var file = folder.File("MixedCase.txt", 100);
        var canonical = Win32.FinalPath(file);
        Expect.Approved(Gates.Check(file.ToUpperInvariant()), canonical);
        Expect.Approved(Gates.Check(file.ToLowerInvariant()), canonical);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ASubstDriveIsApprovedWithTheRealPath()
    {
        var file = folder.File("behind-subst.txt", 100);
        using var subst = new SubstDrive(folder.Path);
        Expect.Approved(Gates.Check(subst.Combine("behind-subst.txt")), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderGivenWithATrailingSeparatorIsApprovedWithoutIt()
    {
        var dir = folder.Dir("sub");
        folder.File(@"sub\inner.txt", 10);
        var approved = Expect.Approved(Gates.Check(dir + "\\"), Win32.FinalPath(dir));
        Assert.IsFalse(approved.CanonicalPath.EndsWith('\\'));
        Assert.AreEqual(1, approved.FileCount);
        Assert.AreEqual(10, approved.LogicalBytes);
    }
}
