using System;
using System.Diagnostics;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Step 9, cloud-sync roots found through the environment and the known provider folder names (the spec's
/// Never-touch row); the registered-provider case is in CloudTests.</summary>
[TestClass]
public sealed class SyncRootTests
{
    private const string CommercialVariable = "OneDriveCommercial";
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("sync");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnEnvironmentSyncRootIsRecognisedInsideAndContaining()
    {
        var root = folder.Dir("od-commercial");
        var inside = folder.File(@"od-commercial\inside.txt", 10);
        var previous = Environment.GetEnvironmentVariable(CommercialVariable);
        try
        {
            Environment.SetEnvironmentVariable(CommercialVariable, root);
            var context = SafetyContext.ForCurrentUser([]);
            Expect.ContainsPath(context.SyncRoots, Win32.FinalPath(root), "sync roots from the environment");
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Check(root, context: context), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Check(folder.Path, context: context), RefusalReason.CloudSyncFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CommercialVariable, previous);
        }
        if (string.IsNullOrEmpty(previous))
        {
            var fresh = SafetyContext.ForCurrentUser([]);
            Expect.Approved(Gates.Check(inside, context: fresh), Win32.FinalPath(inside));
        }
    }

    /// <summary>Runs <paramref name="body"/> with one environment variable set for the process, restored afterwards.</summary>
    private static void WithVariable(string variable, string value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>G09, cloud row: "`%OneDrive%`, `%OneDriveConsumer%`, `%OneDriveCommercial%`": each variable alone names a
    /// cloud-sync root (the existing test covers only the commercial one).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void EveryOneDriveVariableNamesASyncRoot()
    {
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer" })
        {
            var root = folder.Dir("root-" + variable);
            var inside = folder.File("root-" + variable + @"\inside.txt", 10);
            WithVariable(variable, root, () =>
            {
                var context = SafetyContext.ForCurrentUser([]);
                Expect.ContainsPath(context.SyncRoots, Win32.FinalPath(root), "%" + variable + "%");
                Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
                Expect.Refused(Gates.Check(root, context: context), RefusalReason.CloudSyncFolder);
            });
        }
    }

    /// <summary>G10, Windows note "Every Windows location is resolved ... so a redirected folder is still covered": a listed
    /// sync root given in its 8.3 spelling or through a subst letter is listed by its canonical path, and a file inside,
    /// given by its real path, is refused.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AListedSyncRootGivenInAnotherSpellingStillMatches()
    {
        var root = folder.Dir("a sync root to shorten");
        var inside = folder.File(@"a sync root to shorten\inside.txt", 10);
        var canonical = Win32.FinalPath(root);
        var shortSpelling = Win32.ShortPath(root);
        Assert.AreNotEqual(root, shortSpelling, "no 8.3 names on the system volume");
        WithVariable(CommercialVariable, shortSpelling, () =>
        {
            var context = SafetyContext.ForCurrentUser([]);
            Expect.ContainsPath(context.SyncRoots, canonical, "the 8.3 spelling, listed canonical");
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
        });
        using var subst = new SubstDrive(folder.Path);
        WithVariable(CommercialVariable, subst.Combine("a sync root to shorten"), () =>
        {
            var context = SafetyContext.ForCurrentUser([]);
            Expect.ContainsPath(context.SyncRoots, canonical, "the subst spelling, listed canonical");
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
        });
    }

    /// <summary>The fix of b765771 for sync roots: a root named by the environment before it exists, through a subst
    /// letter, is listed by its canonical spelling too. Once created, the root and a file inside at their real paths
    /// are refused as inside a cloud-sync folder, and so is a put-back destination inside it (Putting back: "a
    /// destination inside a cloud-sync root is refused as a cloud-sync folder").</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ASyncRootNamedBeforeItExistsThroughASubstLetterMatchesByCanonicalPath()
    {
        var real = folder.Combine("od-later");
        using var subst = new SubstDrive(folder.Path);
        WithVariable(CommercialVariable, subst.Combine("od-later"), () =>
        {
            Assert.IsFalse(Directory.Exists(real), "the root exists before the context is resolved");
            var context = SafetyContext.ForCurrentUser([]);
            Directory.CreateDirectory(real);
            var inside = folder.File(@"od-later\inside.txt", 10);
            var sibling = folder.File("sibling.txt", 10);
            Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Check(real, context: context), RefusalReason.CloudSyncFolder);
            Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(Path.Combine(real, "restored.txt")), RefusalReason.CloudSyncFolder);
            Expect.Approved(Gates.Check(sibling, context: context), Win32.FinalPath(sibling));
        });
    }

    /// <summary>H24: a sync root named by the environment on a drive letter that is not assigned resolves promptly
    /// and is listed by its given spelling.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [Timeout(30000)]
    public void ASyncRootOnADriveThatDoesNotExistIsListedAsGivenWithoutDelay()
    {
        var root = DriveLetters.Free() + @"\missing\od";
        WithVariable(CommercialVariable, root, () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var context = SafetyContext.ForCurrentUser([]);
            stopwatch.Stop();
            Expect.ContainsPath(context.SyncRoots, root, "a sync root on a drive that does not exist");
            Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"the context took {stopwatch.Elapsed.TotalSeconds:F1} s");
        });
    }

    /// <summary>G42: a network path in the lists does not break the context: as an app folder and as %OneDriveCommercial%
    /// a UNC folder on the loopback share resolves and is listed (covered by its spelling).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANetworkPathInTheListsDoesNotBreakTheContext()
    {
        using var share = new ShareFolder();
        var appContext = SafetyContext.ForCurrentUser([share.Path]);
        Expect.Covers(appContext.NeverTouchFolders, share.Path, "a UNC app folder");
        WithVariable(CommercialVariable, share.Path, () =>
        {
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Covers(context.SyncRoots, share.Path, "a UNC sync root");
            var local = folder.File("local.txt", 10);
            Expect.Approved(Gates.Check(local, context: context), Win32.FinalPath(local));
        });
    }

    /// <summary>G44, step 7 "not inside, and not containing": a folder next to a listed sync root whose name merely extends
    /// it is neither inside nor containing it.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANameThatOnlyStartsLikeASyncRootIsNotInsideIt()
    {
        var root = folder.Dir("syncroot");
        var lookalike = folder.File(@"syncroot-old\inside.txt", 10);
        WithVariable(CommercialVariable, root, () =>
        {
            var context = SafetyContext.ForCurrentUser([]);
            Expect.Approved(Gates.Check(lookalike, context: context), Win32.FinalPath(lookalike));
            Expect.Approved(Gates.Check(folder.Combine("syncroot-old"), context: context), Win32.FinalPath(folder.Combine("syncroot-old")));
        });
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ADropboxFolderDirectlyInTheProfileIsASyncRoot() => ProviderFolderInProfile("Dropbox");

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnOneDriveDashFolderDirectlyInTheProfileIsASyncRoot() => ProviderFolderInProfile("OneDrive - Ambysto Test");

    private static void ProviderFolderInProfile(string name)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), name);
        var created = false;
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            created = true;
        }
        try
        {
            var context = SafetyContext.ForCurrentUser([]);
            Expect.ContainsPath(context.SyncRoots, Win32.FinalPath(path), "known provider folders in the profile");
            if (created)
            {
                var inside = Path.Combine(path, "ambysto-kit-safety-test.txt");
                File.WriteAllText(inside, "inside a provider folder");
                Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
            }
        }
        finally
        {
            if (created)
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>The row says "directly in the profile": the same name deeper down is an ordinary folder.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AProviderFolderNameDeeperInTheProfileIsNotASyncRoot()
    {
        var inside = folder.File(@"Dropbox\inside.txt", 10);
        Expect.Approved(Gates.Check(inside), Win32.FinalPath(inside));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSignedInOneDriveIsASyncRoot()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        Assert.IsFalse(string.IsNullOrEmpty(oneDrive), "%OneDrive% is not set: the brief says OneDrive is signed in for the standard user");
        Assert.IsTrue(Directory.Exists(oneDrive), "%OneDrive% points to a missing folder");
        Expect.ContainsPath(Gates.Context.SyncRoots, Win32.FinalPath(oneDrive!), "the signed-in OneDrive");
    }
}
