using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>"Windows: failing closed": a check that cannot establish its fact refuses with "Could not verify".
/// Real conditions on the tests' own items, each asserted first (the read the gate needs really fails for this
/// user): the owner's implicit READ_CONTROL/WRITE_DAC is taken away with an OWNER RIGHTS deny, and the implicit
/// FILE_READ_ATTRIBUTES a listable parent grants is taken away by denying the listing of the parent. Everything is
/// undone in finally. The unreadable Recycle Bin key and the unreadable policy are in RecycleBinTests.</summary>
[TestClass]
public sealed class FailClosedTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("closed");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    private static void AssertCannotOpenForAttributes(string path)
        => Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => Win32.OpenForAttributes(path).Dispose(), "precondition: an attribute-only open still works on " + path);

    private static void AssertCannotReadOwner(string path)
    {
        if (Directory.Exists(path))
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner), "precondition: the owner is still readable on " + path);
        }
        else
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => new FileInfo(path).GetAccessControl(AccessControlSections.Owner), "precondition: the owner is still readable on " + path);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileThatCannotBeOpenedForAttributesIsCouldNotVerify()
    {
        var parent = folder.Dir("sealed-parent");
        var file = folder.File(@"sealed-parent\sealed.txt", 10);
        var stamp = Gates.StampOf(file);
        Acl.DenyOwnerRights(file, FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes);
        Acl.DenyOwnerRights(parent, FileSystemRights.ListDirectory);
        try
        {
            AssertCannotOpenForAttributes(file);
            Expect.CouldNotVerify(Gates.CheckWith(file, stamp));
        }
        finally
        {
            Acl.Reset(parent);
            Acl.Reset(file);
        }
        Expect.Approved(Gates.CheckWith(file, stamp), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWhoseOwnerCannotBeReadIsCouldNotVerify()
    {
        var file = folder.File("no-owner.txt", 10);
        var stamp = Gates.StampOf(file);
        Acl.DenyOwnerRights(file, FileSystemRights.ReadPermissions);
        try
        {
            AssertCannotReadOwner(file);
            Assert.IsTrue(Win32.FinalPath(file).Length > 0, "precondition: the item itself still opens for attributes");
            Expect.CouldNotVerify(Gates.CheckWith(file, stamp));
        }
        finally
        {
            Acl.Reset(file);
        }
        Expect.Approved(Gates.CheckWith(file, stamp), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderWithASubfolderThatCannotBeListedIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var sealedDir = folder.Dir(@"parent\sealed");
        folder.File(@"parent\sealed\inner.txt", 10);
        var stamp = Gates.StampOf(parent);
        Acl.DenyOwnerRights(sealedDir, FileSystemRights.ListDirectory);
        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(sealedDir).ToList(), "precondition: the subfolder can still be listed");
            Expect.CouldNotVerify(Gates.CheckWith(parent, stamp));
        }
        finally
        {
            Acl.Reset(sealedDir);
        }
        Expect.Approved(Gates.CheckWith(parent, stamp), Win32.FinalPath(parent));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAFileWhoseOwnerCannotBeReadIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var inner = folder.File(@"parent\deep\no-owner.txt", 10);
        var stamp = Gates.StampOf(parent);
        Acl.DenyOwnerRights(inner, FileSystemRights.ReadPermissions);
        try
        {
            AssertCannotReadOwner(inner);
            Expect.CouldNotVerify(Gates.CheckWith(parent, stamp));
        }
        finally
        {
            Acl.Reset(inner);
        }
        Expect.Approved(Gates.CheckWith(parent, stamp), Win32.FinalPath(parent));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAFileThatCannotBeOpenedIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var deep = folder.Dir(@"parent\deep");
        var inner = folder.File(@"parent\deep\sealed.txt", 10);
        var stamp = Gates.StampOf(parent);
        Acl.DenyOwnerRights(inner, FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions);
        Acl.DenyOwnerRights(deep, FileSystemRights.ListDirectory);
        try
        {
            AssertCannotOpenForAttributes(inner);
            Expect.CouldNotVerify(Gates.CheckWith(parent, stamp));
        }
        finally
        {
            Acl.Reset(deep);
            Acl.Reset(inner);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARestoreDestinationWhoseParentsOwnerCannotBeReadIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        Acl.DenyOwnerRights(parent, FileSystemRights.ReadPermissions);
        try
        {
            AssertCannotReadOwner(parent);
            Expect.CouldNotVerify(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
        }
        finally
        {
            Acl.Reset(parent);
        }
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
    }
}
