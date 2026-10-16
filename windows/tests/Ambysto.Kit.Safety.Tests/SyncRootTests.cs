using System;
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
