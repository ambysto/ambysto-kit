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

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void TheMachinePolicyIsDecidedBeforeAContainedLink()
    {
        var parent = folder.Dir("parent");
        Win32.SetJunction(folder.Dir(@"parent\junction"), folder.Dir("target"));
        Expect.Refused(Gates.Check(parent, policy: SystemRecycleBinPolicy.Instance), RefusalReason.RecycleBinOff);
        Expect.Refused(Gates.Check(parent, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);
    }

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

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAFileSymbolicLinkIsRefusedAsContaining()
        => Expect.Refused(Gates.Check(handoff.FolderWithFileSymlink, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingADirectorySymbolicLinkIsRefusedAsContaining()
        => Expect.Refused(Gates.Check(handoff.FolderWithDirectorySymlink, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAMountPointIsRefusedAsContaining()
        => Expect.Refused(Gates.Check(handoff.FolderWithMountPoint, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    /// <summary>A WOF-compressed file is a reparse point (spec step 3, "compression"); a folder holding one is refused
    /// until its recycle is shown to be recoverable. The file is the standard user's own (setup gives it ownership).
    /// Measured on the test machine: the WOF filter hides the reparse point from GetFileAttributes, FindFirstFile and
    /// FSCTL_GET_REPARSE_POINT (attributes read "Archive" only); fsutil file layout shows tag 0x80000017 and
    /// FSCTL_GET_EXTERNAL_BACKING reports the backing, which is what the gate has to ask.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AWofCompressedFileIsRefusedAsASpecialFile()
    {
        Assert.IsTrue(Win32.IsWofBacked(handoff.WofFile), "setup did not produce a WOF-backed file");
        Expect.Refused(Gates.CheckWith(handoff.WofFile, Gates.AnyStamp, policy: Allowed), RefusalReason.LinkOrSpecialFile);
        Assert.AreEqual(RefusalReason.LinkOrSpecialFile, FileFacts.Read(handoff.WofFile).FailureReason);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingAWofCompressedFileIsRefusedAsContaining()
        => Expect.Refused(Gates.Check(handoff.FolderWithWofFile, policy: Allowed), RefusalReason.ContainsLinkOrSpecialFile);

    /// <summary>A TrustedInstaller-owned file alone, outside every system location: "Owned by Windows".</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ATrustedInstallerOwnedFileIsRefusedAsOwnedByWindows()
    {
        var owner = new FileInfo(handoff.TrustedInstallerOwnedFile).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner)
            .GetOwner(typeof(System.Security.Principal.SecurityIdentifier))!.Value;
        Assert.AreEqual("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", owner, "precondition: owned by TrustedInstaller");
        Expect.Refused(Gates.Check(handoff.TrustedInstallerOwnedFile, policy: Allowed), RefusalReason.SystemOwned);
        Expect.Refused(Gates.Check(handoff.FolderWithTrustedInstallerOwnedFile, policy: Allowed), RefusalReason.SystemOwned);
    }

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void ASystemOwnedFileIsRefusedAsOwnedByWindows()
        => Expect.Refused(Gates.Check(handoff.SystemOwnedFile, policy: Allowed), RefusalReason.SystemOwned);

    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AFolderHoldingASystemOwnedFileIsRefusedAsOwnedByWindows()
        => Expect.Refused(Gates.Check(handoff.FolderWithSystemOwnedFile, policy: Allowed), RefusalReason.SystemOwned);

    /// <summary>Intent criterion 8: an Administrators-owned folder is not "owned by the current user".</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnAdministratorsOwnedFolderIsNotOwnedByTheCurrentUser()
    {
        var recording = new RecordingRule();
        Expect.Approved(Gates.Check(handoff.AdministratorsOwnedFile, rule: recording, policy: Allowed), Win32.FinalPath(handoff.AdministratorsOwnedFile));
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
