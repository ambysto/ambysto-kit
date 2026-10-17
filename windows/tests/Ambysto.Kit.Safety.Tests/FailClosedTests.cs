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
/// FILE_READ_ATTRIBUTES a listable parent grants is taken away by denying the listing of the parent.
/// Measured on the test machine (2026-10-16): once READ_CONTROL is denied to the user and to OWNER RIGHTS, the
/// standard user cannot write the DACL back (a fresh unprotected DACL, a protected DACL with an explicit allow and
/// `icacls /reset` are all refused: writing a DACL reads the security descriptor first), it can only delete the item.
/// So the owner cases take their control from a fresh identical item, not from the same item restored; the other
/// denies keep READ_CONTROL and are undone by <see cref="Acl.Reset"/>.
/// The facts note: "the owner of a folder above the item that the user may not read ... is reported as unknown and
/// never as the current user's; the item's own owner, and its contents', must be readable" (two tests below).
/// The unreadable Recycle Bin key and the unreadable policy are in RecycleBinTests.</summary>
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

    private static void AssertCanReadOwner(string path)
    {
        if (Directory.Exists(path))
        {
            new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner);
        }
        else
        {
            new FileInfo(path).GetAccessControl(AccessControlSections.Owner);
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
        Win32.OpenForAttributes(file).Dispose();
        Expect.Approved(Gates.CheckWith(file, stamp), Win32.FinalPath(file));
    }

    /// <summary>The item's own owner must be readable (facts note); the control is a fresh identical file.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileWhoseOwnerCannotBeReadIsCouldNotVerify()
    {
        var file = folder.File("no-owner.txt", 10);
        var control = folder.File("owner-readable.txt", 10);
        var stamp = Gates.StampOf(file);
        Acl.DenyOwnerRights(file, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(file);
        Win32.OpenForAttributes(file).Dispose();
        Expect.CouldNotVerify(Gates.CheckWith(file, stamp));
        AssertCanReadOwner(control);
        Expect.Approved(Gates.Check(control), Win32.FinalPath(control));
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
        Assert.HasCount(1, Directory.EnumerateFileSystemEntries(sealedDir).ToList());
        Expect.Approved(Gates.CheckWith(parent, stamp), Win32.FinalPath(parent));
    }

    /// <summary>The contents' owners must be readable (facts note); the control is a fresh identical tree.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAFileWhoseOwnerCannotBeReadIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var inner = folder.File(@"parent\deep\no-owner.txt", 10);
        var control = folder.Dir("control");
        var controlInner = folder.File(@"control\deep\owner-readable.txt", 10);
        var stamp = Gates.StampOf(parent);
        Acl.DenyOwnerRights(inner, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(inner);
        Expect.CouldNotVerify(Gates.CheckWith(parent, stamp));
        AssertCanReadOwner(controlInner);
        Expect.Approved(Gates.Check(control), Win32.FinalPath(control));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAFileThatCannotBeOpenedIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var deep = folder.Dir(@"parent\deep");
        var inner = folder.File(@"parent\deep\sealed.txt", 10);
        var stamp = Gates.StampOf(parent);
        Acl.DenyOwnerRights(inner, FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes);
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
        Win32.OpenForAttributes(inner).Dispose();
        Expect.Approved(Gates.CheckWith(parent, stamp), Win32.FinalPath(parent));
    }

    /// <summary>The destination's parent is the item of the restore check, so its owner must be readable (facts note);
    /// the control is a fresh parent.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARestoreDestinationWhoseParentsOwnerCannotBeReadIsCouldNotVerify()
    {
        var parent = folder.Dir("parent");
        var control = folder.Dir("control");
        Acl.DenyOwnerRights(parent, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(parent);
        Expect.CouldNotVerify(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
        AssertCanReadOwner(control);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(control, "restored.txt")));
    }

    /// <summary>H10, step 2 "opened for attributes only" and the gate's order ("the first failure refusing the item"):
    /// an item whose permissions cannot be read (READ_CONTROL denied to the user and to OWNER RIGHTS) keeps every
    /// earlier reason: a differing stamp is "changed" (step 6), inside a listed sync root it is a cloud-sync folder
    /// (step 9), a put-back into such a folder inside a listed sync root is a cloud-sync folder, FileFacts still reads
    /// it, and such an app folder given through a subst letter is listed by its canonical path. Only its own owner
    /// (the facts note) is "could not verify".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnItemWhosePermissionsCannotBeReadKeepsItsEarlierReason()
    {
        var file = folder.File("sealed.txt", 10);
        var stamp = Gates.StampOf(file);
        Acl.DenyOwnerRights(file, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(file);
        var facts = FileFacts.Read(file);
        Assert.IsTrue(facts.Succeeded, $"FileFacts: {facts.FailureReason} {facts.Detail}");
        Assert.AreEqual(stamp, facts.Stamp);
        Expect.Refused(Gates.CheckWith(file, stamp with { Length = stamp.Length + 1 }), RefusalReason.ChangedSinceScan);
        Expect.CouldNotVerify(Gates.CheckWith(file, stamp));
        var sealedDestination = folder.Dir("sealed-dest");
        Acl.DenyOwnerRights(sealedDestination, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(sealedDestination);
        var previous = Environment.GetEnvironmentVariable("OneDriveCommercial");
        try
        {
            Environment.SetEnvironmentVariable("OneDriveCommercial", folder.Path);
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Refused(Gates.CheckWith(file, stamp, context: context), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(Path.Combine(sealedDestination, "restored.txt")), RefusalReason.CloudSyncFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OneDriveCommercial", previous);
        }
        var sealedApp = folder.Dir("sealed-app");
        Acl.DenyOwnerRights(sealedApp, FileSystemRights.ReadPermissions);
        AssertCannotReadOwner(sealedApp);
        using var subst = new SubstDrive(folder.Path);
        var appContext = SafetyContext.ForCurrentUser([subst.Combine("sealed-app")]);
        Expect.ContainsPath(appContext.NeverTouchFolders, Win32.FinalPath(sealedApp), "a sealed app folder given through a subst letter");
    }

    // The facts note's other half, an unreadable owner of a folder *above* the item reported as unknown and not
    // refused, is tested in AdminSetupTests on the real case (fixtures under Public): a standard user that denies
    // READ_CONTROL to itself on one of its own folders can no longer delete that folder's children (measured), so the
    // case cannot be built and cleaned up here.
}
