using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 12: the app's rule runs last, with the facts the spec lists (intent Outcome 5, criterion 8).</summary>
[TestClass]
public sealed partial class AllowedRootTests
{
    private TempFolder folder = null!;
    private string file = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("rule");
        file = folder.File(@"sub\item.txt", 100);
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARefusingRuleRefusesAsNotAllowedWithTheAppsText()
    {
        Expect.NotAllowed(Gates.Check(file, rule: new RefuseAllRule("Outside your home folder.")), "Outside your home folder.");
        Expect.Approved(Gates.Check(file, rule: AllowAllRule.Instance), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheRuleRunsOnlyAfterEveryOtherCheckPassed()
    {
        var recording = new RecordingRule();
        Expect.Refused(Gates.CheckWith(folder.Combine("missing.txt"), Gates.AnyStamp), RefusalReason.AlreadyGone);
        var stamp = Gates.StampOf(file);
        Expect.Refused(Gates.Seamed(recording).Check(new GateRequest(file, stamp with { Length = 1 })), RefusalReason.ChangedSinceScan);
        Expect.Refused(Gates.Seamed(recording, FakeEnvironment.Session0).Check(new GateRequest(file, stamp)), RefusalReason.NotInteractive);
        Assert.IsEmpty(recording.Evaluated, "the rule ran before an earlier check refused");
        Expect.Approved(Gates.Check(file, rule: recording), Win32.FinalPath(file));
        Assert.HasCount(1, recording.Evaluated);
        Assert.IsEmpty(recording.RestoreEvaluated);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheRuleSeesTheItemsFacts()
    {
        var recording = new RecordingRule();
        var approved = Expect.Approved(Gates.Check(file, rule: recording), Win32.FinalPath(file));
        var facts = recording.Evaluated.Single();
        Assert.AreEqual(Win32.FinalPath(file), facts.CanonicalPath);
        Assert.AreEqual(facts.CanonicalPath, approved.Facts.CanonicalPath);
        Assert.AreEqual(Gates.Context.ProfileFolder, facts.ProfileFolder);
        Assert.AreEqual(Gates.Context.ProfilesFolder, facts.ProfilesFolder);
        Assert.AreEqual(approved.Stamp.Identity.VolumeSerial, facts.VolumeSerial);
        Assert.AreEqual(Win32.FileIdInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).VolumeSerial, facts.SystemVolumeSerial);
        Assert.IsFalse(facts.IsSystemLocation);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void OwnersListTheItemFirstThenEveryFolderUpToTheVolumeRoot()
    {
        var recording = new RecordingRule();
        Expect.Approved(Gates.Check(file, rule: recording));
        var facts = recording.Evaluated.Single();
        var canonical = Win32.FinalPath(file);
        Assert.AreEqual(canonical, facts.Owners[0].Path);
        Assert.AreEqual(Path.GetPathRoot(canonical), facts.Owners[^1].Path);
        for (var i = 1; i < facts.Owners.Count; i++)
        {
            Assert.AreEqual(Path.GetDirectoryName(facts.Owners[i - 1].Path), facts.Owners[i].Path, "each entry is the parent of the one before");
        }
        Assert.IsTrue(facts.Owners[0].OwnedByCurrentUser, "the test's own file");
        Assert.AreEqual(Acl.CurrentUser.Value, facts.Owners[0].OwnerSid);
        Assert.IsTrue(facts.Owners[1].OwnedByCurrentUser, "the test's own folder");
        var profiles = facts.Owners.Single(o => string.Equals(o.Path, facts.ProfilesFolder, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(profiles.OwnedByCurrentUser, "the profiles folder is not the user's");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnApprovedItemReportsItsMeasuredTree()
    {
        var dir = folder.Dir("tree");
        folder.File(@"tree\a.bin", 1000);
        folder.File(@"tree\deep\b.bin", 2000);
        folder.File(@"tree\deep\deeper\c.bin", 3000);
        folder.Dir(@"tree\empty");
        var approved = Expect.Approved(Gates.Check(dir), Win32.FinalPath(dir));
        Assert.AreEqual(3, approved.FileCount);
        Assert.AreEqual(6000, approved.LogicalBytes);
        Assert.IsTrue(approved.AllocatedBytes >= approved.LogicalBytes, "allocated covers the logical bytes of plain files");
        Assert.IsTrue(approved.AllocatedBytes <= approved.LogicalBytes + 3 * 65536, "allocated is at most a cluster per file above");
    }

    /// <summary>Defect 7: allocated bytes are the clusters a plain file uses, not its length: a 5,002-byte file uses two
    /// 4 KiB clusters (the cluster size read from the volume), and a file small enough to live in its record uses less than one.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnApprovedPlainFileCountsTheClustersItUses()
    {
        var cluster = ClusterSize(Path.GetPathRoot(folder.Path)!);
        var file = folder.File("plain.bin", 5002);
        var approved = Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
        Assert.AreEqual(5002, approved.LogicalBytes);
        Assert.AreEqual((5002 + cluster - 1) / cluster * cluster, approved.AllocatedBytes, "whole clusters, cluster size " + cluster);

        var tiny = folder.File("tiny.bin", 100);
        var tinyAllocated = Expect.Approved(Gates.Check(tiny), Win32.FinalPath(tiny)).AllocatedBytes;
        Assert.IsTrue(tinyAllocated < cluster, "resident in its record (measured 2026-10-17: 104 bytes, the data rounded to 8): " + tinyAllocated);
    }

    private static long ClusterSize(string root)
    {
        Assert.IsTrue(GetDiskFreeSpaceW(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _), "cluster size of " + root);
        return (long)sectorsPerCluster * bytesPerSector;
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
