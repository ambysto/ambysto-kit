using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Spec "Putting back": the destination's folder exists, protection by path and by identity of every
/// existing folder above it, then the app's separate restore rule; the environment check runs first (seam tests).</summary>
[TestClass]
public sealed class RestoreTests
{
    private TempFolder folder = null!;
    private string parent = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("restore");
        parent = folder.Dir("parent");
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    /// <summary>G03 (b): protection by path when the entry has no identity: an app folder listed in the context and then
    /// removed still refuses a put-back whose destination is exactly that path; a sibling path is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationAtAListedAppFolderThatNoLongerExistsIsProtected()
    {
        var app = folder.Dir("appdata");
        var context = SafetyContext.ForCurrentUser([app]);
        Directory.Delete(app);
        Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(app), RefusalReason.Protected);
        Expect.Approved(Gates.Seamed(context: context).CheckRestoreDestination(folder.Combine("appdata-sibling")));
    }

    /// <summary>H12, "Putting back" Windows note (owner decision 2026-10-17): "A destination folder must be on a fixed
    /// local NTFS or ReFS volume ... a folder on FAT32, exFAT, a removable or a network volume is refused as an
    /// unsupported volume": a destination on the removable FAT32 drive or on the fixed exFAT drive is refused as an
    /// unsupported volume, not "could not verify".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationOnAFatOrExFatVolumeIsRefusedAsAnUnsupportedVolume()
    {
        Volumes.RequireRemovableFat32();
        Volumes.RequireFixedExFat();
        using var removable = TempFolder.OnDrive(Volumes.RemovableFat32, "restore");
        using var exfat = TempFolder.OnDrive(Volumes.FixedExFat, "restore");
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(removable.Combine("restored.txt")), RefusalReason.UnsupportedVolume);
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(exfat.Combine("restored.txt")), RefusalReason.UnsupportedVolume);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "restored.txt")));
    }

    /// <summary>G03 (c): a per-volume system name that does not exist on the second NTFS drive (`pagefile.sys`,
    /// `hiberfil.sys` are "on every volume") refuses a put-back by path alone; a plain name at the same root passes.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationNamedLikeAPerVolumeSystemLocationIsProtected()
    {
        Volumes.RequireSecondNtfs();
        foreach (var name in new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" })
        {
            var destination = Path.Combine(Volumes.SecondNtfs, name);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                continue;
            }
            Expect.Refused(Gates.Seamed().CheckRestoreDestination(destination), RefusalReason.Protected);
        }
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(Volumes.SecondNtfs, "ambysto-restore-" + Guid.NewGuid().ToString("N") + ".txt")));
    }

    /// <summary>G11, failing closed: a destination that exists but whose attributes this user cannot read (read attributes
    /// denied on it, listing denied on its folder) is "Could not verify", never approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationWhoseExistenceCannotBeReadIsCouldNotVerify()
    {
        var sealedParent = folder.Dir("sealed-parent");
        var taken = folder.File(@"sealed-parent\taken.txt", 10);
        Acl.DenyOwnerRights(taken, System.Security.AccessControl.FileSystemRights.ReadAttributes | System.Security.AccessControl.FileSystemRights.ReadData | System.Security.AccessControl.FileSystemRights.ReadExtendedAttributes);
        Acl.DenyOwnerRights(sealedParent, System.Security.AccessControl.FileSystemRights.ListDirectory);
        try
        {
            Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => Win32.OpenForAttributes(taken).Dispose(), "precondition: the destination still opens");
            Expect.CouldNotVerify(Gates.Seamed().CheckRestoreDestination(taken));
        }
        finally
        {
            Acl.Reset(sealedParent);
            Acl.Reset(taken);
        }
    }

    /// <summary>G34 (b): an existing folder whose attributes this user cannot read, as the destination's folder, is
    /// "Could not verify", not "destination folder gone".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationFolderWhoseAttributesCannotBeReadIsCouldNotVerifyNotGone()
    {
        var wrapper = folder.Dir("wrapper");
        var sealedFolder = folder.Dir(@"wrapper\sealed");
        Acl.DenyOwnerRights(sealedFolder, System.Security.AccessControl.FileSystemRights.ReadAttributes | System.Security.AccessControl.FileSystemRights.ListDirectory | System.Security.AccessControl.FileSystemRights.ReadExtendedAttributes);
        Acl.DenyOwnerRights(wrapper, System.Security.AccessControl.FileSystemRights.ListDirectory);
        try
        {
            Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => Win32.OpenForAttributes(sealedFolder).Dispose(), "precondition: the folder still opens");
            Expect.CouldNotVerify(Gates.Seamed().CheckRestoreDestination(Path.Combine(sealedFolder, "restored.txt")));
        }
        finally
        {
            Acl.Reset(wrapper);
            Acl.Reset(sealedFolder);
        }
    }

    /// <summary>G12: "its folder exists" means a folder: a destination whose parent is a file is "destination folder gone".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationWhoseParentIsAFileIsRefusedAsFolderGone()
    {
        var file = folder.File(@"parent\file.txt", 10);
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(file, "restored.txt")), RefusalReason.RestoreFolderMissing);
    }

    /// <summary>G13: links are never followed, putting back included: a destination folder that is a junction is refused
    /// as a link; its target folder as the destination folder is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationFolderThatIsAJunctionIsRefusedAsALink()
    {
        var target = folder.Dir("target");
        var junction = folder.Dir("junction");
        Win32.SetJunction(junction, target);
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(junction, "restored.txt")), RefusalReason.LinkOrSpecialFile);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(target, "restored.txt")));
    }

    /// <summary>G14: the approved put-back carries the checked path in canonical form, however the folder was spelled.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnApprovedPutBackCarriesTheCanonicalDestination()
    {
        var longName = folder.Dir("a folder name to shorten");
        var canonical = Win32.FinalPath(longName) + @"\restored.txt";
        var shortSpelling = Win32.ShortPath(longName);
        Assert.AreNotEqual(longName, shortSpelling, "no 8.3 names on the system volume");
        Assert.AreEqual(canonical, Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(shortSpelling, "restored.txt"))).CanonicalPath);
        using var subst = new SubstDrive(folder.Path);
        Assert.AreEqual(canonical, Expect.Approved(Gates.Seamed().CheckRestoreDestination(subst.Combine(@"a folder name to shorten\restored.txt"))).CanonicalPath);
        Assert.AreEqual(canonical, Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(longName.ToUpperInvariant(), "restored.txt"))).CanonicalPath);
    }

    /// <summary>G15: a destination inside a listed sync root is refused as a cloud-sync folder when the folder is given
    /// through a subst letter or its 8.3 spelling.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationInsideASyncRootGivenInAnotherSpellingIsRefused()
    {
        const string variable = "OneDriveCommercial";
        var previous = Environment.GetEnvironmentVariable(variable);
        var root = folder.Dir("a sync root to shorten");
        try
        {
            Environment.SetEnvironmentVariable(variable, root);
            var context = SafetyContext.ForCurrentUser([]);
            var shortSpelling = Win32.ShortPath(root);
            Assert.AreNotEqual(root, shortSpelling, "no 8.3 names on the system volume");
            Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(Path.Combine(shortSpelling, "restored.txt")), RefusalReason.CloudSyncFolder);
            using var subst = new SubstDrive(folder.Path);
            Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(subst.Combine(@"a sync root to shorten\restored.txt")), RefusalReason.CloudSyncFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>G16: never touch "any `WindowsImageBackup` folder": a put-back directly into one, or into a sub-folder of
    /// one, is refused as Protected; into a folder named `Backups` it is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationInAFolderProtectedByNameIsProtected()
    {
        var backup = folder.Dir("WindowsImageBackup");
        var sub = folder.Dir(@"WindowsImageBackup\machine");
        var plain = folder.Dir("Backups");
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(backup, "restored.vhdx")), RefusalReason.Protected);
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(sub, "restored.vhdx")), RefusalReason.Protected);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(plain, "restored.vhdx")));
    }

    /// <summary>Owner decision 2026-10-17, Putting back: "Protection by name applies to the destination whatever the item
    /// is: a destination whose own name is a protected folder name (for example WindowsImageBackup) or a protected file
    /// name is refused as protected." Nothing exists at either destination; `Backups` is the control.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationWhoseOwnNameIsProtectedIsProtected()
    {
        var destination = folder.Combine("WindowsImageBackup");
        Assert.IsFalse(Directory.Exists(destination) || File.Exists(destination));
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(destination), RefusalReason.Protected);
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(folder.Combine("passwords.kdbx")), RefusalReason.Protected);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(folder.Combine("Backups")));
        Assert.IsFalse(Directory.Exists(destination), "the gate writes nothing");
    }

    /// <summary>G37: only "inside" counts: a destination that is a missing folder above a missing never-touch entry
    /// (`%USERPROFILE%\Apple`, above `Apple\MobileSync\Backup`) is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationAboveAMissingNeverTouchEntryIsApproved()
    {
        var apple = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apple");
        if (Directory.Exists(apple) || File.Exists(apple))
        {
            Assert.Inconclusive(apple + " exists on this machine: the case needs it absent");
        }
        var recording = new RecordingRule();
        Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(apple));
        Assert.HasCount(1, recording.RestoreEvaluated);
        Assert.IsFalse(Directory.Exists(apple), "the gate writes nothing");
    }

    /// <summary>Spec "Putting back": protection is "by path and by identity of every existing folder above it" and
    /// refuses what is *inside* a protected entry or system location. The profile folder holds never-touch entries
    /// (`.ssh`, the hives) but is not one itself: a put-back directly into it is approved, the app's restore rule
    /// consulted once. Nothing is created at the destination.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationDirectlyInTheProfileFolderIsApproved()
    {
        var recording = new RecordingRule();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var destination = Path.Combine(profile, "ambysto-restore-" + Guid.NewGuid().ToString("N") + ".txt");
        Assert.IsFalse(File.Exists(destination));
        var approved = Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(destination));
        Assert.AreEqual(Win32.FinalPath(profile), approved.Facts.CanonicalPath);
        Assert.HasCount(1, recording.RestoreEvaluated);
        Assert.IsEmpty(recording.Evaluated);
        Assert.IsFalse(File.Exists(destination), "the gate writes nothing");
    }

    /// <summary>The root of a fixed drive holds system locations (`$Recycle.Bin`, `System Volume Information`): a
    /// put-back into a folder the test made at that root, and one directly into the root, are approved, since only a
    /// destination inside a protected entry refuses (the root is a system location's parent, not a system location).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationAtTheRootOfAFixedDriveIsApproved()
    {
        Volumes.RequireSecondNtfs();
        var recording = new RecordingRule();
        var rootFolder = Path.Combine(Volumes.SecondNtfs, "ambysto-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootFolder);
        try
        {
            var approvedInFolder = Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(Path.Combine(rootFolder, "x.txt")));
            Assert.AreEqual(Win32.FinalPath(rootFolder), approvedInFolder.Facts.CanonicalPath);
            var directlyInRoot = Path.Combine(Volumes.SecondNtfs, "ambysto-restore-" + Guid.NewGuid().ToString("N") + ".txt");
            var approvedInRoot = Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(directlyInRoot));
            Assert.AreEqual(Win32.FinalPath(Volumes.SecondNtfs), approvedInRoot.Facts.CanonicalPath);
            Assert.IsFalse(File.Exists(directlyInRoot), "the gate writes nothing");
            Assert.HasCount(2, recording.RestoreEvaluated);
            Assert.IsEmpty(recording.Evaluated);
        }
        finally
        {
            Directory.Delete(rootFolder, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationWhoseFolderExistsIsApprovedWithTheParentsFacts()
    {
        var recording = new RecordingRule();
        var result = Gates.Seamed(recording).CheckRestoreDestination(Path.Combine(parent, "restored.txt"));
        var approved = Expect.Approved(result);
        Assert.AreEqual(Win32.FinalPath(parent), approved.Facts.CanonicalPath);
        Assert.HasCount(1, recording.RestoreEvaluated);
        Assert.IsEmpty(recording.Evaluated);
        Assert.AreEqual(Win32.FinalPath(parent), recording.RestoreEvaluated.Single().CanonicalPath);
        Assert.IsTrue(recording.RestoreEvaluated.Single().Owners[0].OwnedByCurrentUser);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationWhoseFolderIsMissingIsRefusedAsFolderGone()
    {
        var recording = new RecordingRule();
        Expect.Refused(Gates.Seamed(recording).CheckRestoreDestination(folder.Combine(@"missing\restored.txt")), RefusalReason.RestoreFolderMissing);
        Expect.Refused(Gates.Seamed(recording).CheckRestoreDestination(folder.Combine(@"missing\deeper\restored.txt")), RefusalReason.RestoreFolderMissing);
        Assert.IsEmpty(recording.RestoreEvaluated);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationInsideAnAppProtectedFolderIsProtectedAndTheRuleIsNotAsked()
    {
        var app = folder.Dir("appdata");
        var recording = new RecordingRule();
        var context = SafetyContext.ForCurrentUser([app]);
        Expect.Refused(Gates.Seamed(recording, context: context).CheckRestoreDestination(Path.Combine(app, "restored.txt")), RefusalReason.Protected);
        Assert.IsEmpty(recording.RestoreEvaluated);
        Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(Path.Combine(app, "restored.txt")));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationInsideASystemLocationIsProtected()
        => Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(Environment.SystemDirectory, "ambysto-kit-safety-restore.txt")), RefusalReason.Protected);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AProtectedNameIsRefusedAsADestination()
    {
        Expect.Refused(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "passwords.kdbx")), RefusalReason.Protected);
        Expect.Approved(Gates.Seamed().CheckRestoreDestination(Path.Combine(parent, "passwords.txt")));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheAppsRestoreRuleDecidesLast()
    {
        var destination = Path.Combine(parent, "restored.txt");
        Expect.NotAllowed(Gates.Seamed(new RefuseAllRule("Not a place this app restores to.")).CheckRestoreDestination(destination), "Not a place this app restores to.");
        Expect.Approved(Gates.Seamed(AllowAllRule.Instance).CheckRestoreDestination(destination));
    }

    /// <summary>Spec "Putting back": a destination inside a cloud-sync root is refused as a cloud-sync folder.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADestinationInsideASyncRootIsRefusedAsCloudSyncFolder()
    {
        const string variable = "OneDriveCommercial";
        var previous = Environment.GetEnvironmentVariable(variable);
        var recording = new RecordingRule();
        try
        {
            Environment.SetEnvironmentVariable(variable, parent);
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Refused(Gates.Seamed(recording, context: context).CheckRestoreDestination(Path.Combine(parent, "restored.txt")), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Seamed(recording, context: context).CheckRestoreDestination(Path.Combine(parent, "deeper", "restored.txt")), RefusalReason.CloudSyncFolder);
            Assert.IsEmpty(recording.RestoreEvaluated);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>Spec "Putting back": a destination that already exists is refused (the shell's own restore would block
    /// on "Replace or Skip"), whether a file or a folder is in the way.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnExistingDestinationIsRefusedAsDestinationExists()
    {
        var recording = new RecordingRule();
        var takenByFile = folder.File(@"parent\taken.txt", 10);
        var takenByFolder = folder.Dir(@"parent\taken-folder");
        Expect.Refused(Gates.Seamed(recording).CheckRestoreDestination(takenByFile), RefusalReason.RestoreDestinationExists);
        Expect.Refused(Gates.Seamed(recording).CheckRestoreDestination(takenByFolder), RefusalReason.RestoreDestinationExists);
        Expect.Refused(Gates.Seamed(recording).CheckRestoreDestination(takenByFile.ToUpperInvariant()), RefusalReason.RestoreDestinationExists);
        Assert.IsEmpty(recording.RestoreEvaluated);
        Expect.Approved(Gates.Seamed(recording).CheckRestoreDestination(Path.Combine(parent, "free.txt")));
    }
}
