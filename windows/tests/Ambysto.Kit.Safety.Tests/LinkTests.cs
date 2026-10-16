using System;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 3 (a link or special file is refused, never followed) and the "contains a link or special
/// file" rule of step 8, with what a standard user can make: junctions, unknown reparse tags, WOF compression.
/// Symbolic links and mount points need an administrator: AdminSetupTests.</summary>
[TestClass]
public sealed class LinkTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("link");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    private string Junction(string relative, string target)
    {
        var dir = folder.Dir(relative);
        Win32.SetJunction(dir, target);
        Assert.AreEqual(Win32.AttrReparsePoint, Win32.Attributes(dir) & Win32.AttrReparsePoint, "the junction was not made");
        return dir;
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AJunctionIsRefusedAsALink()
    {
        var target = folder.Dir("target");
        var junction = Junction("junction", target);
        Expect.Refused(Gates.CheckWith(junction, Gates.AnyStamp), RefusalReason.LinkOrSpecialFile);
        Expect.Approved(Gates.Check(target), Win32.FinalPath(target));
    }

    /// <summary>Links are never followed: a junction to nowhere is still a link, not "Already gone".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AJunctionToAMissingTargetIsStillALink()
    {
        var junction = Junction("dangling", folder.Combine("nowhere"));
        Expect.Refused(Gates.CheckWith(junction, Gates.AnyStamp), RefusalReason.LinkOrSpecialFile);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAJunctionIsRefusedAsContainingALink()
    {
        var parent = folder.Dir("parent");
        folder.File(@"parent\plain.txt", 10);
        Expect.Approved(Gates.Check(parent), Win32.FinalPath(parent));
        Junction(@"parent\sub\junction", folder.Dir("target"));
        Expect.Refused(Gates.Check(parent), RefusalReason.ContainsLinkOrSpecialFile);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWithAnUnknownReparseTagIsRefusedAsASpecialFile()
    {
        var file = folder.File("tagged.bin", 100);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Win32.SetUnknownReparsePoint(file);
        Assert.AreEqual(Win32.AttrReparsePoint, Win32.Attributes(file) & Win32.AttrReparsePoint, "the reparse point was not set");
        Expect.Refused(Gates.CheckWith(file, Gates.AnyStamp), RefusalReason.LinkOrSpecialFile);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAnUnknownReparseFileIsRefusedAsContaining()
    {
        var parent = folder.Dir("parent");
        var file = folder.File(@"parent\deep\tagged.bin", 100);
        Win32.SetUnknownReparsePoint(file);
        Expect.Refused(Gates.Check(parent), RefusalReason.ContainsLinkOrSpecialFile);
    }

    /// <summary>NTFS (LZNT1) compression is an attribute, not a reparse point: a plain file to the gate (the WOF
    /// case, a reparse point, is in AdminSetupTests: only an administrator could make one on the test machine).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnNtfsCompressedFileIsNotASpecialFile()
    {
        var parent = folder.Dir("parent");
        var file = folder.File(@"parent\lznt1.bin", 1 << 16, fill: 0);
        Win32.SetNtfsCompression(file);
        Assert.AreEqual(Win32.AttrCompressed, Win32.Attributes(file) & Win32.AttrCompressed, "the file was not compressed");
        Assert.AreEqual(0u, Win32.Attributes(file) & Win32.AttrReparsePoint);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Expect.Approved(Gates.Check(parent), Win32.FinalPath(parent));
    }
}
