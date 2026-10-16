using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 11: never-touch entries (the spec's rows and the app's additions) and system locations,
/// inside and containing, by path and by identity; the app's rule can never widen protection (criterion 8).</summary>
[TestClass]
public sealed class ProtectionTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("protect");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnItemInsideAnAppProtectedFolderIsProtected()
    {
        var app = folder.Dir("appdata");
        var inside = folder.File(@"appdata\journal.json", 10);
        var sibling = folder.File("sibling.txt", 10);
        var context = SafetyContext.ForCurrentUser([app]);
        Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
        Expect.Approved(Gates.Check(sibling, context: context), Win32.FinalPath(sibling));
        Expect.Approved(Gates.Check(inside), Win32.FinalPath(inside));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheAppProtectedFolderAndAFolderContainingItAreProtected()
    {
        var app = folder.Dir("appdata");
        folder.File(@"appdata\journal.json", 10);
        var context = SafetyContext.ForCurrentUser([app]);
        Expect.Refused(Gates.Check(app, context: context), RefusalReason.Protected);
        Expect.Refused(Gates.Check(folder.Path, context: context), RefusalReason.Protected);
        Expect.Approved(Gates.Check(folder.Path), Win32.FinalPath(folder.Path));
    }

    /// <summary>An entry in another spelling (8.3, other case) and an item through a subst letter still match.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ProtectionMatchesAcrossSpellings()
    {
        var app = folder.Dir("application data folder");
        var inside = folder.File(@"application data folder\journal.json", 10);
        var shortSpelling = Win32.ShortPath(app).ToUpperInvariant();
        Assert.AreNotEqual(app.ToUpperInvariant(), shortSpelling, "no 8.3 names on the system volume");
        var context = SafetyContext.ForCurrentUser([shortSpelling]);
        Expect.ContainsPath(context.NeverTouchFolders, Win32.FinalPath(app), "the app's entry, canonical");
        Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
        using var subst = new SubstDrive(folder.Path);
        Expect.Refused(Gates.Check(subst.Combine(@"application data folder\journal.json"), context: SafetyContext.ForCurrentUser([app])), RefusalReason.Protected);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAllowEverythingRuleCannotPassAProtectedItemAndIsNotEvenAsked()
    {
        var app = folder.Dir("appdata");
        var inside = folder.File(@"appdata\journal.json", 10);
        var context = SafetyContext.ForCurrentUser([app]);
        var recording = new RecordingRule();
        Expect.Refused(Gates.Check(inside, rule: AllowAllRule.Instance, context: context), RefusalReason.Protected);
        Expect.Refused(Gates.Check(inside, rule: recording, context: context), RefusalReason.Protected);
        Assert.IsEmpty(recording.Evaluated, "the rule ran although protection failed");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APasswordVaultIsProtectedByNameAndSoIsAFolderHoldingIt()
    {
        var vault = folder.File(@"vaults\passwords.kdbx", 10);
        var text = folder.File(@"vaults\passwords.txt", 10);
        Expect.Refused(Gates.Check(vault), RefusalReason.Protected);
        Expect.Refused(Gates.Check(folder.Combine("vaults")), RefusalReason.Protected);
        Expect.Approved(Gates.Check(text), Win32.FinalPath(text));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AMailStoreIsProtectedByName()
    {
        var pst = folder.File("archive.pst", 10);
        var ost = folder.File("mailbox.ost", 10);
        var txt = folder.File("archive.txt", 10);
        Expect.Refused(Gates.Check(pst), RefusalReason.Protected);
        Expect.Refused(Gates.Check(ost), RefusalReason.Protected);
        Expect.Approved(Gates.Check(txt), Win32.FinalPath(txt));
    }

    /// <summary>The spec protects `WindowsImageBackup` by name (device and system image backups), wherever it is.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AWindowsImageBackupFolderIsProtectedByName()
    {
        var backup = folder.Dir("WindowsImageBackup");
        var inside = folder.File(@"WindowsImageBackup\machine\disk.vhdx", 10);
        Expect.Refused(Gates.Check(backup), RefusalReason.Protected);
        Expect.Refused(Gates.Check(inside), RefusalReason.Protected);
        Expect.Refused(Gates.Check(folder.Path), RefusalReason.Protected);
    }

    /// <summary>Spec: a virtual-machine disk in an allowed root is not protected.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AVirtualMachineDiskIsNotProtected()
    {
        var vhdx = folder.File("machine.vhdx", 10);
        var vhd = folder.File("machine.vhd", 10);
        Expect.Approved(Gates.Check(vhdx), Win32.FinalPath(vhdx));
        Expect.Approved(Gates.Check(vhd), Win32.FinalPath(vhd));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheUsersRegistryHiveIsProtected()
    {
        var hive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NTUSER.DAT");
        Assert.IsTrue(File.Exists(hive), "no NTUSER.DAT in the profile");
        Expect.Refused(Gates.Check(hive), RefusalReason.Protected);
        var log = hive + ".LOG1";
        if (File.Exists(log))
        {
            Expect.Refused(Gates.Check(log), RefusalReason.Protected);
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileInsideASystemLocationIsProtected()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Expect.Refused(Gates.Check(cmd), RefusalReason.Protected);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderInsideASystemLocationIsProtected()
    {
        var etc = Path.Combine(Environment.SystemDirectory, "drivers", "etc");
        Assert.IsTrue(Directory.Exists(etc), etc);
        Expect.Refused(Gates.Check(etc), RefusalReason.Protected);
    }

    /// <summary>The Recycle Bin of every volume is never touched (its size may be read).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ThisUsersRecycleBinFolderIsProtected()
    {
        // The fixture must land in the bin whatever it holds: a quota that leaves room, asserted before the move.
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var used = Win32.RecycleBinBytes(Volumes.System);
        var capacityMegabytes = (int)(used / (1 << 20)) + 64;
        settings.SetMaxCapacityMegabytes(capacityMegabytes);
        settings.SetNukeOnDelete(false);
        Assert.IsTrue((long)capacityMegabytes * 1_000_000 - used > 1 << 20, "the Recycle Bin on C: has no room for a 100-byte fixture");
        using var recycled = RecycledFixture.Recycle(folder.File("to-bin.txt", 100));
        var bin = RecycledFixture.BinFolderFor(Volumes.System);
        Assert.IsTrue(Directory.Exists(bin), bin);
        Expect.Refused(Gates.Check(bin), RefusalReason.Protected);
    }

    /// <summary>Pictures resolves through the known-folder API. When OneDrive's folder backup has moved it into the
    /// OneDrive root, step 9 refuses it first (inside a cloud-sync folder); otherwise step 11 does.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ThePicturesKnownFolderIsProtected()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Assert.IsTrue(Directory.Exists(pictures), "no Pictures folder: " + pictures);
        var canonical = Win32.FinalPath(pictures);
        Expect.ContainsPath(Gates.Context.NeverTouchFolders, canonical, "the Pictures known folder");
        var insideSyncRoot = false;
        foreach (var root in Gates.Context.SyncRoots)
        {
            insideSyncRoot |= canonical.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        Expect.Refused(Gates.Check(pictures), insideSyncRoot ? RefusalReason.CloudSyncFolder : RefusalReason.Protected);
    }

    /// <summary>The Pictures known folder's path before a test redirected it; restored in finally and, should a test
    /// die, in the class cleanup.</summary>
    private static string? originalPictures;

    [ClassCleanup]
    public static void RestorePictures()
    {
        if (originalPictures is not null)
        {
            Win32.SetKnownFolderPath(Win32.PicturesFolderId, originalPictures);
            originalPictures = null;
        }
    }

    /// <summary>Intent criterion 7: a redirected known folder is still protected. The user's own Pictures folder is
    /// redirected to a temp folder for the test and restored after it (nothing is moved).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARedirectedKnownFolderIsStillProtected()
    {
        var current = Win32.KnownFolderPath(Win32.PicturesFolderId);
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive) && current.StartsWith(oneDrive.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
        {
            // Pictures is the only known folder the never-touch table names on its own; the others (%APPDATA%,
            // %LOCALAPPDATA%) are too dangerous to redirect on a live account. This is a loud stop, not a skip.
            Assert.Fail("OneDrive's folder backup manages Pictures for this user (" + current + "): turn it off for the test account, then this test can redirect Pictures");
        }
        var redirected = folder.Dir("Pictures-redirected");
        var inside = folder.File(@"Pictures-redirected\photo.jpg", 10);
        var untouched = folder.File("not-a-picture.txt", 10);
        originalPictures = current;
        Win32.SetKnownFolderPath(Win32.PicturesFolderId, redirected);
        try
        {
            Assert.AreEqual(redirected, Win32.KnownFolderPath(Win32.PicturesFolderId), "the redirection did not take");
            var context = SafetyContext.ForCurrentUser([]);
            Expect.ContainsPath(context.NeverTouchFolders, Win32.FinalPath(redirected), "the redirected Pictures folder");
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
            Expect.Refused(Gates.Check(redirected, context: context), RefusalReason.Protected);
            Expect.Approved(Gates.Check(untouched, context: context), Win32.FinalPath(untouched));
        }
        finally
        {
            RestorePictures();
        }
        Assert.AreEqual(current, Win32.KnownFolderPath(Win32.PicturesFolderId), "Pictures was not restored");
        Expect.Approved(Gates.Check(inside, context: SafetyContext.ForCurrentUser([])), Win32.FinalPath(inside));
    }
}
