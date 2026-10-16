using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Step 8: no path in the item's tree of 260 characters or more, measured on the canonical plain spelling;
/// 259 passes everywhere. The walk must see long descendants under a short item.</summary>
[TestClass]
public sealed class LongPathTests
{
    private TempFolder folder = null!;
    private string root = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("long");
        root = folder.Canonical;
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWhoseCanonicalPathHas259CharactersIsApproved()
    {
        var file = LongPaths.CreateFile(root, 259);
        var approved = Expect.Approved(Gates.Check(file), file);
        Assert.AreEqual(64, approved.LogicalBytes);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWhoseCanonicalPathHas260CharactersIsRefused()
    {
        var file = LongPaths.CreateFile(root, 260);
        Expect.Refused(Gates.Check(file), RefusalReason.PathTooLong);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderWhoseOwnPathHas260CharactersIsRefused()
    {
        var dir = root + "\\" + new string('d', 260 - root.Length - 1);
        Directory.CreateDirectory(dir);
        Assert.AreEqual(260, dir.Length);
        Expect.Refused(Gates.Check(dir), RefusalReason.PathTooLong);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AShortFolderHoldingA260CharacterDescendantIsRefused()
    {
        var item = folder.Dir("short");
        LongPaths.CreateFile(Win32.FinalPath(item), 260);
        Expect.Refused(Gates.Check(item), RefusalReason.PathTooLong);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AShortFolderHoldingA259CharacterDescendantIsApproved()
    {
        var item = folder.Dir("short");
        LongPaths.CreateFile(Win32.FinalPath(item), 259);
        var approved = Expect.Approved(Gates.Check(item), Win32.FinalPath(item));
        Assert.AreEqual(1, approved.FileCount);
    }

    /// <summary>A short item whose descendant directory is itself 260 characters or longer, with a file inside (about
    /// 300): a walk that enumerates without extended-length names never sees that file.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AShortFolderHoldingADirectoryBeyond260CharactersIsRefused()
    {
        var item = folder.Dir("short");
        var deep = LongPaths.CreateDeepFile(Win32.FinalPath(item));
        Assert.IsTrue(deep.Length >= 290, deep.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Expect.Refused(Gates.Check(item), RefusalReason.PathTooLong);
    }

    /// <summary>The length is the canonical spelling's: a short (8.3) spelling under 260 characters of a 260-character
    /// path is refused, and the extended prefix of a 259-character path does not count.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheLengthIsMeasuredOnTheCanonicalSpelling()
    {
        var longFile = LongPaths.CreateFile(root, 260);
        var shortSpelling = LongPaths.ShortSpelling(longFile);
        Assert.IsTrue(shortSpelling.Length < 260, $"no 8.3 spelling under 260 characters: {shortSpelling}");
        Expect.Refused(Gates.Check(shortSpelling), RefusalReason.PathTooLong);

        var fits = LongPaths.CreateFile(Win32.FinalPath(folder.Dir("fits")), 259);
        Expect.Approved(Gates.Check(@"\\?\" + fits), fits);
    }
}
