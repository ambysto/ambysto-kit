using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
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

    /// <summary>G03 (a), step 11 "compared both by path and by file identity": an app folder listed before it exists has
    /// no identity yet; once created, a file in it is still refused by path.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAppFolderListedBeforeItExistsProtectsByPath()
    {
        var app = folder.Combine("appdata-later");
        var context = SafetyContext.ForCurrentUser([app]);
        Directory.CreateDirectory(app);
        var inside = folder.File(@"appdata-later\journal.json", 10);
        var sibling = folder.File("sibling.txt", 10);
        Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
        Expect.Refused(Gates.Check(app, context: context), RefusalReason.Protected);
        Expect.Approved(Gates.Check(sibling, context: context), Win32.FinalPath(sibling));
    }

    /// <summary>G03 with another spelling (the fix of b765771): an app folder listed before it exists, through a subst
    /// letter or an 8.3 parent spelling, has no identity and its given spelling never equals the canonical path. Once
    /// created, a file inside at its real path is still refused as protected, and a put-back there is refused too
    /// (Putting back: "protection (step 7, by path ...)"). A sibling is approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAppFolderListedBeforeItExistsThroughAnotherSpellingProtectsByCanonicalPath()
    {
        var spellings = new System.Collections.Generic.List<(string Spelling, string What)>();
        using var subst = new SubstDrive(folder.Path);
        spellings.Add((subst.Combine("appdata-subst"), "subst letter"));
        var shortParent = Win32.ShortPath(folder.Path);
        Assert.AreNotEqual(folder.Path, shortParent, "no 8.3 names on the system volume");
        spellings.Add((Path.Combine(shortParent, "appdata-short"), "8.3 parent"));
        foreach (var (spelling, what) in spellings)
        {
            var name = Path.GetFileName(spelling);
            var real = folder.Combine(name);
            Assert.IsFalse(Directory.Exists(real), what + ": the folder exists before the context is resolved");
            var context = SafetyContext.ForCurrentUser([spelling]);
            Directory.CreateDirectory(real);
            var inside = folder.File(name + @"\journal.json", 10);
            var sibling = folder.File("sibling-" + name + ".txt", 10);
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
            Expect.Refused(Gates.Check(real, context: context), RefusalReason.Protected);
            Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(Path.Combine(real, "restored.txt")), RefusalReason.Protected);
            Expect.Approved(Gates.Check(sibling, context: context), Win32.FinalPath(sibling));
            Expect.Approved(Gates.Check(inside), Win32.FinalPath(inside));
        }
    }

    /// <summary>G04, step 7 "by file identity ... so no alternative spelling slips past": a listed app folder renamed on
    /// the same volume keeps protecting its contents and itself; a fresh context resolved after the rename does not.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARenamedProtectedFolderIsStillProtectedByIdentity()
    {
        var app = folder.Dir("appdata");
        folder.File(@"appdata\journal.json", 10);
        var context = SafetyContext.ForCurrentUser([app]);
        var renamed = folder.Combine("appdata-renamed");
        Directory.Move(app, renamed);
        var inside = Path.Combine(renamed, "journal.json");
        Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
        Expect.Refused(Gates.Check(renamed, context: context), RefusalReason.Protected);
        var fresh = SafetyContext.ForCurrentUser([app]);
        Expect.Approved(Gates.Check(inside, context: fresh), Win32.FinalPath(inside));
    }

    /// <summary>H03, step 11 "compared both by path and by file identity ... so no alternative spelling slips past" and
    /// step 2 "opened for attributes only": an app folder whose permissions cannot be read (READ_CONTROL denied to the
    /// user and to OWNER RIGHTS) still gets its identity when the context is resolved. Renamed on the same volume, the
    /// file inside it is refused as protected; the folder itself is refused too (protected, or "could not verify"
    /// because its own owner cannot be read: the facts note). A context resolved after the rename approves the file.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARenamedProtectedFolderWhosePermissionsCannotBeReadIsStillProtectedByIdentity()
    {
        var app = folder.Dir("sealed-app");
        folder.File(@"sealed-app\journal.json", 10);
        Acl.DenyOwnerRights(app, FileSystemRights.ReadPermissions);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => new DirectoryInfo(app).GetAccessControl(AccessControlSections.Owner), "precondition: the folder's permissions are still readable");
        var renamed = folder.Combine("sealed-app-renamed");
        try
        {
            var context = SafetyContext.ForCurrentUser([app]);
            Directory.Move(app, renamed);
            var inside = Path.Combine(renamed, "journal.json");
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
            var itself = Gates.Check(renamed, context: context);
            Assert.IsFalse(itself.IsApproved, "the renamed folder itself was approved");
            Assert.IsTrue(itself.Reason is RefusalReason.Protected or RefusalReason.CouldNotVerify, "the renamed folder itself: " + itself.Reason);
            Console.WriteLine("the renamed folder itself: " + itself.Message);
            var fresh = SafetyContext.ForCurrentUser([app]);
            Expect.Approved(Gates.Check(inside, context: fresh), Win32.FinalPath(inside));
        }
        finally
        {
            // The sealed folder's DACL cannot be written back (measured: see FailClosedTests), and the temp folder's
            // scrub, which tries, leaves its child undeletable; a plain delete of the child and the folder still works.
            foreach (var sealedFolder in new[] { app, renamed })
            {
                if (Directory.Exists(sealedFolder))
                {
                    File.Delete(Path.Combine(sealedFolder, "journal.json"));
                    Directory.Delete(sealedFolder);
                }
            }
        }
    }

    /// <summary>H08, "Putting back" Windows note (owner decision 2026-10-17): "both name rules apply to [the item the gate
    /// checks] whatever its kind, so a folder named notes.kdbx and a file named WindowsImageBackup are refused as
    /// protected". Controls: a folder and a file whose names match neither rule are approved.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void BothNameRulesApplyToTheItemItselfWhateverItsKind()
    {
        var vaultFolder = folder.Dir("notes.kdbx");
        folder.File(@"notes.kdbx\plain.txt", 10);
        var backupFile = folder.File("WindowsImageBackup", 10);
        var plainFolder = folder.Dir("notes");
        folder.File(@"notes\plain.txt", 10);
        var plainFile = folder.File("WindowsImageBackup.txt", 10);
        Expect.Refused(Gates.Check(vaultFolder), RefusalReason.Protected);
        Expect.Refused(Gates.Check(backupFile), RefusalReason.Protected);
        Expect.Approved(Gates.Check(plainFolder), Win32.FinalPath(plainFolder));
        Expect.Approved(Gates.Check(plainFile), Win32.FinalPath(plainFile));
    }

    /// <summary>The same note: "entries inside a folder follow the rule for their kind": a folder holding a file named
    /// WindowsImageBackup and a sub-folder named x.pst is approved; one holding a file x.pst is protected (control).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void EntriesInsideAFolderFollowTheRuleForTheirKind()
    {
        var item = folder.Dir("item");
        folder.File(@"item\WindowsImageBackup", 10);
        folder.File(@"item\x.pst\inside.txt", 10);
        Expect.Approved(Gates.Check(item), Win32.FinalPath(item));
        var control = folder.Dir("control");
        folder.File(@"control\mail.pst", 10);
        Expect.Refused(Gates.Check(control), RefusalReason.Protected);
    }

    /// <summary>H13, Never touch, browser row (owner decision 2026-10-17): "the profile data itself: ...\User Data for
    /// Chrome, Edge and Brave, Mozilla\Firefox; the rest of the vendor folder is not protected": a file in the vendor
    /// folder outside its profile data is approved. Only what the test creates is created, and removed again. The context
    /// is resolved inside the test, after the folder exists, so the rows are pinned as built now, not as cached once per
    /// test run (Stryker group C: a row shortened to the vendor folder would be found by path and by identity).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Environment.SpecialFolder.LocalApplicationData, @"Google\Chrome")]
    [DataRow(Environment.SpecialFolder.LocalApplicationData, @"Microsoft\Edge")]
    [DataRow(Environment.SpecialFolder.LocalApplicationData, @"BraveSoftware\Brave-Browser")]
    [DataRow(Environment.SpecialFolder.ApplicationData, "Mozilla")]
    public void AVendorFolderOutsideItsProfileDataIsNotProtected(Environment.SpecialFolder baseFolder, string vendor)
    {
        var vendorPath = Path.Combine(Environment.GetFolderPath(baseFolder), vendor);
        var topCreated = vendorPath;
        while (!Directory.Exists(topCreated) && Path.GetDirectoryName(topCreated) is { } above && !Directory.Exists(above))
        {
            topCreated = above;
        }
        var existed = Directory.Exists(vendorPath);
        var test = Path.Combine(vendorPath, TempFolder.RootName + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(test);
        try
        {
            var file = Path.Combine(test, "x.txt");
            File.WriteAllText(file, "outside the profile data");
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Approved(Gates.Check(file, context: context), Win32.FinalPath(file));
            Expect.Approved(Gates.Check(test, context: context), Win32.FinalPath(test));
        }
        finally
        {
            TempFolder.DeleteTree(existed ? test : topCreated);
        }
    }

    /// <summary>H22 (plan Design, public API): an empty string among the app's folders is ignored; the context resolves
    /// with the same entries as without it.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnEmptyAppEntryIsIgnored()
    {
        var plain = SafetyContext.ForCurrentUser([]);
        var withEmpty = SafetyContext.ForCurrentUser([""]);
        CollectionAssert.AreEqual(plain.NeverTouchFolders.ToList(), withEmpty.NeverTouchFolders.ToList());
    }

    /// <summary>H24 (plan Design, "resolved once per session"): an app folder on a drive letter that is not assigned
    /// resolves promptly and is listed by its given spelling.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [Timeout(30000)]
    public void AnAppFolderOnADriveThatDoesNotExistIsListedAsGivenWithoutDelay()
    {
        var app = DriveLetters.Free() + @"\missing\app";
        var stopwatch = Stopwatch.StartNew();
        var context = SafetyContext.ForCurrentUser([app]);
        stopwatch.Stop();
        Expect.ContainsPath(context.NeverTouchFolders, app, "an app folder on a drive that does not exist");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"the context took {stopwatch.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>G04 for a system location the user can make: `FileHistory` at the root of the system drive (never touch:
    /// "any FileHistory folder at a volume root"), listed by a context, then renamed: a file inside is still refused.
    /// Only when no such folder exists already; cleaned up.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARenamedFileHistoryFolderAtTheVolumeRootIsStillProtectedByIdentity()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        var fileHistory = Path.Combine(systemRoot, "FileHistory");
        var renamed = Path.Combine(systemRoot, "FileHistory-renamed-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(fileHistory) || File.Exists(fileHistory))
        {
            Assert.Inconclusive(fileHistory + " exists on this machine: the case needs to create it");
        }
        Directory.CreateDirectory(fileHistory);
        try
        {
            var inside = Path.Combine(fileHistory, "inside.txt");
            File.WriteAllText(inside, "inside File History");
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.Protected);
            Directory.Move(fileHistory, renamed);
            Expect.Refused(Gates.Check(Path.Combine(renamed, "inside.txt"), context: context), RefusalReason.Protected);
            Expect.Approved(Gates.Check(Path.Combine(renamed, "inside.txt"), context: SafetyContext.ForCurrentUser([])), Win32.FinalPath(Path.Combine(renamed, "inside.txt")));
        }
        finally
        {
            TempFolder.DeleteTree(fileHistory);
            TempFolder.DeleteTree(renamed);
        }
    }

    /// <summary>G29: the rows protect what they name and no more: a file in %APPDATA% outside every row is approved. The
    /// context is resolved inside the test, after the folder exists, so the rows are pinned as built now, not as cached
    /// once per test run (Stryker group C: a row shortened to %APPDATA% itself would be found by path and by identity).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileInAppDataOutsideEveryRowIsApproved()
    {
        var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), TempFolder.RootName, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outside);
        try
        {
            var file = Path.Combine(outside, "x.txt");
            File.WriteAllText(file, "outside every row");
            Expect.Approved(Gates.Check(file, context: SafetyContext.ForCurrentUser([])), Win32.FinalPath(file));
        }
        finally
        {
            TempFolder.DeleteTree(outside);
        }
    }

    /// <summary>G43: an app folder given through a subst letter is listed by its real canonical path.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnAppFolderGivenThroughASubstLetterIsListedCanonical()
    {
        var app = folder.Dir("appdata");
        using var subst = new SubstDrive(folder.Path);
        var context = SafetyContext.ForCurrentUser([subst.Combine("appdata")]);
        var canonical = Win32.FinalPath(app);
        Assert.IsTrue(context.NeverTouchFolders.Any(e => string.Equals(e, canonical, StringComparison.Ordinal)), "listed as given, not canonical: " + string.Join(", ", context.NeverTouchFolders.Where(e => e.Contains("appdata", StringComparison.OrdinalIgnoreCase))));
        // H23 (plan Design "resolved to canonical paths"): an existing entry is listed by its canonical path only.
        Assert.IsFalse(context.NeverTouchFolders.Any(e => e.StartsWith(subst.Letter, StringComparison.OrdinalIgnoreCase)), "the subst spelling is listed too: " + string.Join(", ", context.NeverTouchFolders.Where(e => e.StartsWith(subst.Letter, StringComparison.OrdinalIgnoreCase))));
        Expect.Refused(Gates.Check(folder.File(@"appdata\journal.json", 10), context: context), RefusalReason.Protected);
    }

    /// <summary>G44, step 7 "not inside, and not containing": a name that only starts like a protected entry is not inside
    /// it: `%USERPROFILE%\.ssh-old` next to the listed `.ssh`, and `<app>2` next to an app-protected `<app>`.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANameThatOnlyStartsLikeAProtectedEntryIsNotInsideIt()
    {
        var app = folder.Dir("appdata");
        var lookalike = folder.File(@"appdata2\x.txt", 10);
        var context = SafetyContext.ForCurrentUser([app]);
        Expect.Approved(Gates.Check(lookalike, context: context), Win32.FinalPath(lookalike));
        var sshOld = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh-old-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sshOld);
        try
        {
            var key = Path.Combine(sshOld, "id_test.pub");
            File.WriteAllText(key, "not a key");
            Expect.Approved(Gates.Check(key), Win32.FinalPath(key));
        }
        finally
        {
            TempFolder.DeleteTree(sshOld);
        }
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
        // The fixture must land in the bin whatever it holds, and the bin folder itself (whose size is everything in the bin)
        // must fit the room left, or step 8 refuses it as RecycleBinFull before step 11 is reached (2026-10-19: the test bed's
        // bin had grown past the old fixed 64 MB of room). So the quota leaves room for the whole bin again, asserted first.
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        var used = Win32.RecycleBinBytes(Volumes.System);
        var capacityMegabytes = (int)(2 * used / (1 << 20)) + 64;
        settings.SetMaxCapacityMegabytes(capacityMegabytes);
        settings.SetNukeOnDelete(false);
        Assert.IsTrue((long)capacityMegabytes * 1_000_000 - used > used + (1 << 20), "the Recycle Bin on C: has no room for its own folder and a 100-byte fixture");
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
