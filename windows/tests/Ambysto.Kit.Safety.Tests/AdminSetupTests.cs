using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>What only an administrator can set up (windows/tests/admin/setup.ps1, reverted by teardown.ps1): the
/// NoRecycleFiles machine policy, symbolic links, a volume mount point, SYSTEM- and Administrators-owned items and
/// an elevated holder. The tests run as the standard user; the policy is real here, so every test that is not about
/// the policy passes the seam "allowed" to reach its own check.</summary>
[TestClass]
public sealed class AdminSetupTests
{
    private const string AdministratorsSid = "S-1-5-32-544";
    private static readonly FakeRecycleBinPolicy Allowed = FakeRecycleBinPolicy.Allowed;
    private static AdminHandoff handoff = null!;
    private TempFolder folder = null!;

    [ClassInitialize]
    public static void ReadHandoff(TestContext _) => handoff = AdminHandoff.Read();

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("adminsetup");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    /// <summary>Precondition for an approval or an ownership refusal (spec step 11 and the facts note): the standard user
    /// can read the owner of the fixture item and of everything in it (links and mount points are not entered). A
    /// folder above it that the user may not read, such as Public, is the gate's business ("unknown"), not asserted.</summary>
    private static string Readable(string path)
    {
        AssertOwnerReadable(path);
        if (Directory.Exists(path) && (Win32.Attributes(path) & Win32.AttrReparsePoint) == 0)
        {
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false }))
            {
                if (entry is DirectoryInfo sub && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Readable(sub.FullName);
                }
                else
                {
                    AssertOwnerReadable(entry.FullName);
                }
            }
        }
        return path;
    }

    private static void AssertOwnerReadable(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                new DirectoryInfo(path).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner);
            }
            else
            {
                new FileInfo(path).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner);
            }
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Fail("precondition: the standard user cannot read the owner of the fixture " + path + " (setup.ps1 must grant it)");
        }
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void TheMachinePolicyIsReadAsDisallowed()
        => Assert.AreEqual(true, SystemRecycleBinPolicy.Instance.NoRecycleFiles);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void TheMachinePolicyRefusesAsRecycleBinOff()
    {
        var file = folder.File("item.bin", 4096);
        Expect.Refused(Gates.Check(file, policy: SystemRecycleBinPolicy.Instance), RefusalReason.RecycleBinOff);
        Expect.Approved(Gates.Check(file, policy: Allowed), Win32.FinalPath(file));
    }

    /// <summary>Step 8's order: the bin off or disallowed before a contained mount point.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void TheMachinePolicyIsDecidedBeforeAContainedMountPoint()
    {
        Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: SystemRecycleBinPolicy.Instance), RefusalReason.RecycleBinOff);
        Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void TheBinOffIsDecidedBeforeAContainedMountPoint()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        settings.SetNukeOnDelete(true);
        Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: Allowed), RefusalReason.RecycleBinOff);
        settings.SetNukeOnDelete(false);
        Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);
    }

    /// <summary>Step 8's order: a contained mount point before a path of 260 characters or more (both inside).</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AContainedMountPointIsDecidedBeforeALongPath()
        => Expect.Refused(Gates.Check(handoff.FolderWithMountPointAndLongPath, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    /// <summary>Spec, recoverable moves: "A folder holding a volume mount point is refused ... the Recycle Bin counts the
    /// mounted volume's whole capacity as part of the folder". The same folder without the mount point is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderWithoutTheMountPointIsApproved()
        => Expect.Approved(Gates.Check(Readable(handoff.FolderWithoutMountPoint), policy: Allowed), Win32.FinalPath(handoff.FolderWithoutMountPoint));

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFileSymbolicLinkIsRefusedAsALink()
        => Expect.Refused(Gates.CheckWith(handoff.FileSymlink, Gates.AnyStamp, policy: Allowed), RefusalReason.LinkOrSpecialFile);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ADirectorySymbolicLinkIsRefusedAsALink()
        => Expect.Refused(Gates.CheckWith(handoff.DirectorySymlink, Gates.AnyStamp, policy: Allowed), RefusalReason.LinkOrSpecialFile);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AVolumeMountPointIsRefusedAsALink()
        => Expect.Refused(Gates.CheckWith(handoff.MountPoint, Gates.AnyStamp, policy: Allowed), RefusalReason.LinkOrSpecialFile);

    /// <summary>Spec, recoverable moves: "recycling a folder that holds a junction or a symbolic link leaves the target
    /// untouched ... so such a folder is allowed"; the link is never entered, so the target's content is not counted.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAFileSymbolicLinkIsApprovedWithoutFollowingIt()
    {
        var approved = Expect.Approved(Gates.Check(Readable(handoff.FolderWithFileSymlink), policy: Allowed), Win32.FinalPath(handoff.FolderWithFileSymlink));
        Assert.AreEqual(0, approved.LogicalBytes, "the link's target was counted");
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingADirectorySymbolicLinkIsApprovedWithoutEnteringIt()
    {
        var approved = Expect.Approved(Gates.Check(Readable(handoff.FolderWithDirectorySymlink), policy: Allowed), Win32.FinalPath(handoff.FolderWithDirectorySymlink));
        Assert.AreEqual(0, approved.FileCount, "the linked folder was entered");
        Assert.AreEqual(0, approved.LogicalBytes, "the linked folder's content was counted");
    }

    /// <summary>Spec, recoverable moves: "recycling a folder that holds a junction ... leaves the target untouched ...
    /// so such a folder is allowed". A junction made by the administrator can be followed (unlike the standard user's
    /// own, which Windows refuses as an untrusted mount point), so this is the case where entering it would count the
    /// target: the precondition shows the target's file through the junction, the gate must not count it.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAFollowableJunctionIsApprovedWithoutEnteringIt()
    {
        var folderWithJunction = Readable(handoff.FolderWithAdminJunction);
        var junction = Path.Combine(folderWithJunction, "junction");
        Assert.AreEqual(Win32.AttrReparsePoint, Win32.Attributes(junction) & Win32.AttrReparsePoint, "precondition: a junction");
        Assert.IsTrue(File.Exists(Path.Combine(junction, "inner.txt")), "precondition: the junction can be followed by this user");
        var approved = Expect.Approved(Gates.Check(folderWithJunction, policy: Allowed), Win32.FinalPath(folderWithJunction));
        Assert.AreEqual(0, approved.FileCount, "the junction's target was entered");
        Assert.AreEqual(0, approved.LogicalBytes, "the junction's target was counted");
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAMountPointIsRefusedAsContaining()
        => Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    /// <summary>Spec step 3: "A compressed (WOF) file is not refused, whether or not Windows shows its tag"; recoverable
    /// moves: "A compressed (WOF) file, on its own or in a folder, recycles and comes back intact and is allowed". The
    /// file is the standard user's own (setup gives it ownership); FSCTL_GET_EXTERNAL_BACKING proves it is WOF-backed
    /// (the attribute APIs show a plain file). Its size is the logical length.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AWofCompressedFileIsApproved()
    {
        Assert.IsTrue(Win32.IsWofBacked(handoff.WofFile), "setup did not produce a WOF-backed file");
        var facts = FileFacts.Read(handoff.WofFile);
        Assert.IsTrue(facts.Succeeded, $"{facts.FailureReason} {facts.Detail}");
        var approved = Expect.Approved(Gates.Check(Readable(handoff.WofFile), policy: Allowed), Win32.FinalPath(handoff.WofFile));
        Assert.AreEqual(1 << 20, approved.LogicalBytes);
        Assert.AreEqual(1, approved.FileCount);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAWofCompressedFileIsApproved()
    {
        Assert.IsTrue(Win32.IsWofBacked(handoff.WofFile), "setup did not produce a WOF-backed file");
        var approved = Expect.Approved(Gates.Check(Readable(handoff.FolderWithWofFile), policy: Allowed), Win32.FinalPath(handoff.FolderWithWofFile));
        Assert.AreEqual(1 << 20, approved.LogicalBytes);
    }

    /// <summary>Never touch: "File History (..., and any `FileHistory` folder at a volume root)": the folder at the second
    /// NTFS volume's root and a file inside it are protected (setup made the folder when it was absent).</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFileHistoryFolderAtAVolumeRootIsProtected()
    {
        Assert.AreEqual(Path.GetPathRoot(handoff.FileHistoryRoot), Path.GetDirectoryName(handoff.FileHistoryRoot), "precondition: at a volume root");
        Expect.Refused(Gates.Check(handoff.FileHistoryFile, policy: Allowed), RefusalReason.Protected);
        Expect.Refused(Gates.Check(handoff.FileHistoryRoot, policy: Allowed), RefusalReason.Protected);
    }

    /// <summary>A TrustedInstaller-owned file alone, outside every system location: "Owned by Windows".</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ATrustedInstallerOwnedFileIsRefusedAsOwnedByWindows()
    {
        var owner = new FileInfo(handoff.TrustedInstallerOwnedFile).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner)
            .GetOwner(typeof(System.Security.Principal.SecurityIdentifier))!.Value;
        Assert.AreEqual("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", owner, "precondition: owned by TrustedInstaller");
        Expect.Refused(Gates.Check(Readable(handoff.TrustedInstallerOwnedFile), policy: Allowed), RefusalReason.SystemOwned);
        Expect.Refused(Gates.Check(Readable(handoff.FolderWithTrustedInstallerOwnedFile), policy: Allowed), RefusalReason.SystemOwned);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ASystemOwnedFileIsRefusedAsOwnedByWindows()
        => Expect.Refused(Gates.Check(Readable(handoff.SystemOwnedFile), policy: Allowed), RefusalReason.SystemOwned);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingASystemOwnedFileIsRefusedAsOwnedByWindows()
        => Expect.Refused(Gates.Check(Readable(handoff.FolderWithSystemOwnedFile), policy: Allowed), RefusalReason.SystemOwned);

    /// <summary>Facts note: "the owner of a folder above the item that the user may open but whose owner it may not read
    /// is reported as unknown and never as the current user's". Preconditions on the fixture: the folder opens for
    /// attributes for this user, and its owner is unreadable; the item and its parent are readable.</summary>
    private static void AssertOwnerUnreadableButOpens(string above)
    {
        Win32.OpenForAttributes(above).Dispose();
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new DirectoryInfo(above).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner),
            "precondition: this user can read the owner of " + above);
    }

    private static void AssertUnknownOwner(ItemFacts facts, string above)
    {
        var entry = facts.Owners.Single(o => string.Equals(o.Path, Win32.FinalPath(above), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(entry.OwnedByCurrentUser, "an unreadable owner above is never the current user's");
        Assert.AreNotEqual(Acl.CurrentUser.Value, entry.OwnerSid, "an unreadable owner above is not reported as the user's SID");
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnUnreadableOwnerAboveTheItemIsUnknownNotRefused()
    {
        AssertOwnerUnreadableButOpens(handoff.OwnerUnreadableAbove);
        var item = Readable(handoff.OwnerUnreadableAboveItem);
        var recording = new RecordingRule();
        var approved = Expect.Approved(Gates.Check(item, rule: recording, policy: Allowed), Win32.FinalPath(item));
        AssertUnknownOwner(approved.Facts, handoff.OwnerUnreadableAbove);
        Assert.IsFalse(approved.Facts.Owners[0].OwnedByCurrentUser, "the fixture was made by the administrator");
        Assert.HasCount(1, recording.Evaluated);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ARestoreDestinationUnderAnUnreadableOwnerAboveIsApproved()
    {
        AssertOwnerUnreadableButOpens(handoff.OwnerUnreadableAbove);
        var parent = Readable(handoff.OwnerUnreadableAboveParent);
        var recording = new RecordingRule();
        var approved = Expect.Approved(Gates.Seamed(recording, policy: Allowed).CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
        Assert.AreEqual(Win32.FinalPath(parent), approved.Facts.CanonicalPath);
        AssertUnknownOwner(approved.Facts, handoff.OwnerUnreadableAbove);
        Assert.HasCount(1, recording.RestoreEvaluated);
    }

    /// <summary>Facts note: "a folder above the item that cannot be opened at all, as C:\Users\Public for a standard user,
    /// refuses with 'Could not verify': its identity is compared too". Preconditions: the folder cannot be opened even
    /// for attributes by this user, while the item and its parent can.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnItemBelowAFolderThatCannotBeOpenedIsCouldNotVerify()
    {
        Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => Win32.OpenForAttributes(handoff.CannotOpenAbove).Dispose(),
            "precondition: this user can open " + handoff.CannotOpenAbove);
        var parent = Readable(handoff.CannotOpenAboveParent);
        var item = Readable(handoff.CannotOpenAboveItem);
        var recording = new RecordingRule();
        Expect.CouldNotVerify(Gates.Check(item, rule: recording, policy: Allowed));
        Expect.CouldNotVerify(Gates.Seamed(recording, policy: Allowed).CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
        Assert.IsEmpty(recording.Evaluated);
        Assert.IsEmpty(recording.RestoreEvaluated);
    }

    /// <summary>Intent criterion 8: an Administrators-owned folder is not "owned by the current user".</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnAdministratorsOwnedFolderIsNotOwnedByTheCurrentUser()
    {
        var recording = new RecordingRule();
        Readable(handoff.AdministratorsOwnedFolder);
        Expect.Approved(Gates.Check(Readable(handoff.AdministratorsOwnedFile), rule: recording, policy: Allowed), Win32.FinalPath(handoff.AdministratorsOwnedFile));
        var facts = recording.Evaluated.Single();
        var file = facts.Owners[0];
        Assert.AreEqual(AdministratorsSid, file.OwnerSid);
        Assert.IsFalse(file.OwnedByCurrentUser, "a file made elevated");
        var canonicalFolder = Win32.FinalPath(handoff.AdministratorsOwnedFolder);
        var owner = facts.Owners.Single(o => string.Equals(o.Path, canonicalFolder, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(AdministratorsSid, owner.OwnerSid);
        Assert.IsFalse(owner.OwnedByCurrentUser, "a folder made elevated");
    }

    /// <summary>Intent criterion 6: an elevated (here SYSTEM, other-session) holder makes the item in use.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnElevatedHolderMakesTheItemInUse()
    {
        var result = new InUseChecker().Check(InUseTests.Item(handoff.HeldFile));
        Assert.IsTrue(result.IsInUse, "not in use");
        Assert.IsTrue(result.Holders.Any(h => h.ProcessId == handoff.HolderPid),
            $"holder {handoff.HolderPid} not named; named: {string.Join(", ", result.Holders.Select(h => $"{h.ProcessId} {h.ImageName} ({h.Method})"))}");
    }
}
