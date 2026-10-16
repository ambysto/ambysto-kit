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
