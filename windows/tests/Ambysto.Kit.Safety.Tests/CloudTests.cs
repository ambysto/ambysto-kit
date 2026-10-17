using System;
using System.IO;
using Ambysto.Kit.Safety.Internal;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Step 9 with the test cloud provider: online-only and pinned placeholders and folders holding them are
/// refused as cloud files; a fully downloaded or plain file inside the root is refused as inside a cloud-sync folder;
/// and the provider's fetch log stays empty across every gate and FileFacts call (intent criterion 3).</summary>
[TestClass]
public sealed class CloudTests
{
    private const string OnlineFile = @"online\a.bin";
    private const string OnlineFolder = "online";
    private const string NestedOnlineFile = @"online\sub\b.bin";
    private const string PinnedFile = @"pinned\p.bin";
    private const string HydratedFile = @"hydrated\h.bin";
    private const string PlaceholderFolder = "tagged";
    private const string PlainFile = @"plain\normal.txt";
    private const long OnlineSize = 1 << 20;
    private const long HydratedSize = 8192;

    private const string PartialFile = @"partial\p.bin";
    private const string OnDemandFolder = "ondemand";
    private const long PartialSize = 4 << 20;
    private const int PartialBytes = 65536;

    /// <summary>H04: folders holding one not-fully-local file each (online-only in the full root, partly downloaded in
    /// the partial root), and a control folder holding a fully downloaded file.</summary>
    private const string H04OnlineFolder = "h04";
    private const string H04OnlineFile = @"h04\online.bin";
    private const string H04PartialFolder = "h04p";
    private const string H04PartialFile = @"h04p\partial.bin";
    private const string H04ControlFolder = "h04ctl";
    private const string H04HydratedFile = @"h04ctl\hydrated.bin";

    private static TempFolder folder = null!;
    private static CloudRoot cloud = null!;
    /// <summary>A second root with partial hydration (population stays always-full: a file placeholder cannot be
    /// created under partial population, 0x801F0005): a partly downloaded file lives here.</summary>
    private static CloudRoot partial = null!;
    /// <summary>A registered root with nothing in it but a plain file: the sync-root rule alone, no placeholder before it.</summary>
    private static CloudRoot empty = null!;
    private static SafetyContext context = null!;

    [ClassInitialize]
    public static void RegisterAndServe(TestContext _)
    {
        // Under the Defender-excluded test data folder: elsewhere Defender reads a freshly hydrated range and that
        // read hydrates the rest of the file, which destroys the partly-downloaded state.
        folder = TempFolder.InTestData("cloud");
        partial = CloudRoot.Register(folder.Combine("partial-root"), folder.Combine("partial-fetch.log"), "partial", "always-full");
        partial.Populate((PartialFile, PartialSize), (H04PartialFile, PartialSize));
        partial.HydrateRange(PartialFile, 0, PartialBytes);
        partial.HydrateRange(H04PartialFile, 0, PartialBytes);
        partial.Serve();
        cloud = CloudRoot.Register(folder.Combine("root"), folder.Combine("fetch.log"));
        cloud.Populate((OnlineFile, OnlineSize), (NestedOnlineFile, 65536), (PinnedFile, 4096), (HydratedFile, HydratedSize), (PlaceholderFolder + "\\", 0),
            (H04OnlineFile, OnlineSize), (H04HydratedFile, HydratedSize));
        var pinned = cloud.Combine(PinnedFile);
        Win32.SetAttributes(pinned, Win32.Attributes(pinned) | Win32.AttrPinned);
        cloud.Serve();
        cloud.Hydrate(HydratedFile, HydratedSize);
        cloud.Hydrate(H04HydratedFile, HydratedSize);
        Directory.CreateDirectory(cloud.Combine("plain"));
        File.WriteAllText(cloud.Combine(PlainFile), "not a placeholder");
        Directory.CreateDirectory(cloud.Combine("wrap"));
        Directory.Move(cloud.Combine(PlaceholderFolder), cloud.Combine(@"wrap\" + PlaceholderFolder));
        empty = CloudRoot.Register(folder.Combine(@"holder\empty-root"), folder.Combine("empty-fetch.log"));
        File.WriteAllText(empty.Combine("plain.txt"), "a plain file inside a registered root");
        // Resolved after registration, as an app resolves its context once per session.
        context = SafetyContext.ForCurrentUser([]);
    }

    [ClassCleanup]
    public static void Unregister()
    {
        cloud?.Dispose();
        partial?.Dispose();
        empty?.Dispose();
        folder?.Dispose();
    }

    private static void RequireOnlineOnly(string relative)
    {
        var status = cloud.Status(relative);
        Assert.IsTrue(status.IsPlaceholder, relative + " is not a placeholder");
        Assert.IsTrue(status.RecallOnDataAccess, relative + " has no recall-on-data-access attribute");
        Assert.AreEqual(0, status.OnDisk, relative + " has bytes on disk");
    }

    private static GateResult Check(string relative) => Gates.Check(cloud.Combine(relative), context: context);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnOnlineOnlyFileIsRefusedAsACloudFileWithoutADownload()
    {
        RequireOnlineOnly(OnlineFile);
        Expect.Refused(Check(OnlineFile), RefusalReason.CloudFile);
        cloud.AssertNoFetch("gate on an online-only file");
        Assert.AreEqual(0, cloud.Status(OnlineFile).OnDisk);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAnOnlineOnlyFileIsRefusedAsACloudFileWithoutADownload()
    {
        RequireOnlineOnly(NestedOnlineFile);
        Expect.Refused(Check(OnlineFolder), RefusalReason.CloudFile);
        cloud.AssertNoFetch("gate on a folder holding online-only files");
        Assert.AreEqual(0, cloud.Status(NestedOnlineFile).OnDisk);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APinnedButNotDownloadedFileIsStillACloudFile()
    {
        RequireOnlineOnly(PinnedFile);
        Assert.AreEqual(Win32.AttrPinned, Win32.Attributes(cloud.Combine(PinnedFile)) & Win32.AttrPinned, "the pin was not set");
        Expect.Refused(Check(PinnedFile), RefusalReason.CloudFile);
        cloud.AssertNoFetch("gate on a pinned online-only file");
    }

    /// <summary>Spec step 7: a cloud-tagged folder inside the item is not entered, and the item is refused as a cloud file.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingACloudTaggedFolderIsRefusedAsACloudFile()
    {
        var status = cloud.Status(@"wrap\" + PlaceholderFolder);
        Assert.IsTrue(status.IsPlaceholder, "the moved folder is not a placeholder any more");
        Expect.Refused(Check("wrap"), RefusalReason.CloudFile);
        cloud.AssertNoFetch("gate on a folder holding a placeholder folder");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFullyDownloadedPlaceholderIsNotACloudFileButInsideTheSyncRoot()
    {
        var status = cloud.Status(HydratedFile);
        Assert.AreEqual("hydrated", status.Hydration, "the fixture did not download the file");
        Expect.Refused(Check(HydratedFile), RefusalReason.CloudSyncFolder);
        cloud.AssertNoFetch("gate on a downloaded placeholder");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APlainFileInsideTheSyncRootIsRefusedAsInsideACloudSyncFolder()
    {
        Assert.IsFalse(cloud.Status(PlainFile).IsPlaceholder);
        Expect.Refused(Check(PlainFile), RefusalReason.CloudSyncFolder);
    }

    /// <summary>Spec: "a cloud-sync root is also what the system reports per item" (measured: a root registered through
    /// the classic Cloud Files API leaves no listable entry; only asking about a path finds it). So the context lists
    /// only the listable roots (%OneDrive% when set), and the gate finds the fixture's root per item: the root itself
    /// and an item inside it are refused as inside a cloud-sync folder; a sibling outside passes. A folder containing
    /// the root is refused as a cloud file, because the registered root folder itself carries a cloud tag and the
    /// order decides: step 7 ("A cloud-tagged folder inside the item is not entered either, and the item is refused as
    /// a cloud file") and step 9 ("... is checked before the cloud-sync roots").</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnUnlistedSyncRootWinsOverAMissingDestinationFolder()
    {
        // G34 (a), Putting back: "a destination inside a cloud-sync root is refused as a cloud-sync folder, even when its
        // folder is gone; then one whose folder is gone".
        Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(empty.Combine(@"missing-folder\restored.txt")), RefusalReason.CloudSyncFolder);
        Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(cloud.Combine(@"online\missing\restored.txt")), RefusalReason.CloudSyncFolder);
    }

    /// <summary>G08, recoverable moves: "Two signals, either refuses: the recall ... attributes, and fewer bytes on disk than
    /// the file's length". The test process's placeholder compatibility mode is set so that Windows hides the
    /// placeholders from it (3: fully disguised, then 1: disguised); what each mode hides is measured and printed. The
    /// online-only and partly downloaded files and the folder holding online-only files are still refused as cloud
    /// files, with no fetch. The empty placeholder folder inside `wrap` is asserted only while its tag stays visible.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void PlaceholdersHiddenFromTheProcessAreStillRefused()
    {
        var tried = 0;
        foreach (var mode in new[] { Win32.PlaceholderModeDisguiseFull, Win32.PlaceholderModeDisguise })
        {
            var previous = Win32.SetPlaceholderMode(mode);
            if (previous < 0)
            {
                Console.WriteLine($"placeholder mode {mode}: refused by the system ({previous})");
                continue;
            }
            tried++;
            try
            {
                var attributes = Win32.Attributes(cloud.Combine(OnlineFile));
                var folderAttributes = Win32.Attributes(cloud.Combine(@"wrap\" + PlaceholderFolder));
                var what = $"placeholder mode {mode}: file recall attribute {((attributes & Win32.AttrRecallOnDataAccess) != 0 ? "visible" : "hidden")}, "
                    + $"file reparse tag {((attributes & Win32.AttrReparsePoint) != 0 ? "visible" : "hidden")}, folder reparse tag {((folderAttributes & Win32.AttrReparsePoint) != 0 ? "visible" : "hidden")}";
                Console.WriteLine(what);
                RequireOnlineOnly(OnlineFile);
                Assert.AreEqual(RefusalReason.CloudFile, Check(OnlineFile).Reason, what + ": online-only file");
                Assert.AreEqual(RefusalReason.CloudFile, Gates.Check(partial.Combine(PartialFile), context: context).Reason, what + ": partly downloaded file");
                Assert.AreEqual(RefusalReason.CloudFile, Check(OnlineFolder).Reason, what + ": folder holding online-only files");
                if ((folderAttributes & Win32.AttrReparsePoint) != 0)
                {
                    Assert.AreEqual(RefusalReason.CloudFile, Check("wrap").Reason, what + ": folder holding a placeholder folder");
                }
            }
            finally
            {
                Win32.SetPlaceholderMode(previous);
            }
            cloud.AssertNoFetch($"gate in placeholder mode {mode}");
            partial.AssertNoFetch($"gate in placeholder mode {mode}");
        }
        if (tried == 0)
        {
            Assert.Inconclusive("the system refused both placeholder compatibility modes");
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARegisteredRootIsFoundPerItemNotListed()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
        {
            Expect.ContainsPath(context.SyncRoots, Win32.FinalPath(oneDrive), "the listable %OneDrive% root");
        }
        var inside = empty.Combine("plain.txt");
        var containing = folder.Combine("holder");
        var sibling = folder.File(@"holder-sibling\outside.txt", 10);
        Expect.Refused(Gates.Check(empty.Root, context: context), RefusalReason.CloudSyncFolder);
        Expect.Refused(Gates.Check(inside, context: context), RefusalReason.CloudSyncFolder);
        Expect.Refused(Gates.Check(containing, context: context), RefusalReason.CloudFile);
        Expect.Approved(Gates.Check(sibling, context: context), Win32.FinalPath(sibling));
        Expect.Refused(Gates.Seamed(context: context).CheckRestoreDestination(empty.Combine("restored.txt")), RefusalReason.CloudSyncFolder);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ASiblingOutsideTheRootIsApproved()
    {
        var outside = folder.File("outside.txt", 100);
        Expect.Approved(Gates.Check(outside, context: context), Win32.FinalPath(outside));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void FileFactsReadsAPlaceholderWithoutDownloadingIt()
    {
        RequireOnlineOnly(OnlineFile);
        var facts = FileFacts.Read(cloud.Combine(OnlineFile));
        Assert.IsTrue(facts.Succeeded, $"{facts.FailureReason} {facts.Detail}");
        Assert.IsFalse(facts.IsCloudNotOpened, "recall-on-data-access is not recall-on-open: the item is opened for attributes");
        Assert.AreEqual(OnlineSize, facts.Stamp!.Value.Length);
        Assert.AreEqual(Win32.FinalPath(cloud.Combine(OnlineFile)), facts.CanonicalPath);
        cloud.AssertNoFetch("FileFacts.Read on an online-only file");
        Assert.AreEqual(0, cloud.Status(OnlineFile).OnDisk);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheFetchLogStaysEmptyAcrossEveryCall()
    {
        foreach (var relative in new[] { OnlineFile, OnlineFolder, NestedOnlineFile, PinnedFile, HydratedFile, PlainFile, "wrap" })
        {
            FileFacts.Read(cloud.Combine(relative));
            Check(relative);
        }
        Gates.Check(cloud.Root, context: context);
        cloud.AssertNoFetch("every gate and FileFacts call");
        Assert.AreEqual(0, cloud.Status(OnlineFile).OnDisk);
        Assert.AreEqual(0, cloud.Status(NestedOnlineFile).OnDisk);
        Assert.AreEqual(0, cloud.Status(PinnedFile).OnDisk);
    }

    /// <summary>Step 8 runs before step 9: a folder holding an online-only file on a volume whose bin is off is
    /// refused for the bin, and for the cloud once the bin is on again; no download either way.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheRecycleBinIsDecidedBeforeTheCloud()
    {
        using var settings = new RecycleBinVolumeSettings(Volumes.System);
        RequireOnlineOnly(NestedOnlineFile);
        settings.SetNukeOnDelete(true);
        Expect.Refused(Check(OnlineFolder), RefusalReason.RecycleBinOff);
        settings.SetNukeOnDelete(false);
        Expect.Refused(Check(OnlineFolder), RefusalReason.CloudFile);
        cloud.AssertNoFetch("gate on a cloud folder with the bin off and on");
    }

    /// <summary>Lists one file of the fixture to the walk without its recall attributes (the seam's listing change):
    /// the bytes-on-disk signal alone is left to decide.</summary>
    private static GateResult CheckWithRecallAttributesHidden(CloudRoot root, string relativeFolder, string relativeFile, out int hidden)
    {
        var fileCanonical = Win32.FinalPath(root.Combine(relativeFile)).TrimEnd('\\');
        var count = 0;
        GateResult result;
        using (Faults.Change(Faults.Query.Listing, (path, attributes, tag) =>
        {
            if (!string.Equals(path.TrimEnd('\\'), fileCanonical, StringComparison.OrdinalIgnoreCase))
            {
                return (attributes, tag);
            }
            count++;
            return (attributes & ~(Win32.AttrRecallOnOpen | Win32.AttrRecallOnDataAccess), tag);
        }))
        {
            result = Gates.Check(root.Combine(relativeFolder), context: context);
        }
        hidden = count;
        return result;
    }

    /// <summary>H04, recoverable moves: "Two signals, either refuses: the 'recall on data access' or 'recall on open'
    /// attributes, and fewer bytes on disk than the file's length"; step 9: the file "is checked before the cloud-sync
    /// roots". The folder's listing hides the file's recall attributes, so only the bytes on disk can refuse: the
    /// folder is refused as a cloud file (not as inside a cloud-synced folder, which would refuse it anyway), with no
    /// download. Control: the folder holding a fully downloaded file, listed the same way, is a cloud-sync folder.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(H04OnlineFolder, H04OnlineFile, false)]
    [DataRow(H04PartialFolder, H04PartialFile, true)]
    public void FewerBytesOnDiskAloneRefusesAsACloudFile(string relativeFolder, string relativeFile, bool inPartialRoot)
    {
        var root = inPartialRoot ? partial : cloud;
        var status = root.Status(relativeFile);
        Assert.IsTrue(status.IsPlaceholder, relativeFile + " is not a placeholder");
        Assert.IsTrue(status.OnDisk < status.Size, $"{relativeFile} is fully on disk: {status.OnDisk} of {status.Size} bytes");
        var folderAttributes = Win32.Attributes(root.Combine(relativeFolder));
        Assert.AreEqual(0u, folderAttributes & (Win32.AttrRecallOnOpen | Win32.AttrRecallOnDataAccess), "the folder itself carries a recall attribute: the file's signal would not be what decides");
        var result = CheckWithRecallAttributesHidden(root, relativeFolder, relativeFile, out var hidden);
        Assert.AreEqual(1, hidden, "the file was not listed once");
        Expect.Refused(result, RefusalReason.CloudFile);
        root.AssertNoFetch("gate on a folder whose listing hides the recall attributes");
        Assert.AreEqual(status.OnDisk, root.Status(relativeFile).OnDisk);
        var control = CheckWithRecallAttributesHidden(cloud, H04ControlFolder, H04HydratedFile, out hidden);
        Assert.AreEqual(1, hidden, "the control file was not listed once");
        Assert.AreEqual("hydrated", cloud.Status(H04HydratedFile).Hydration, "the control is not fully downloaded");
        Expect.Refused(control, RefusalReason.CloudSyncFolder);
    }

    /// <summary>Failing closed on the cloud check itself. Ignored with the facts: the fixture's own status proves that
    /// CfGetPlaceholderInfo succeeds on a FILE_READ_ATTRIBUTES-only handle (its query uses exactly that), so the
    /// only way to make it fail for this user is to make that attribute-only open fail, which is the generic
    /// fail-closed case FailClosedTests already covers; no real condition fails the placeholder query alone.</summary>
    [Ignore("no real condition makes CfGetPlaceholderInfo fail on an item the gate can open for attributes; see the remarks")]
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnUnreadablePlaceholderStateIsCouldNotVerify()
    {
        Expect.CouldNotVerify(Check(OnlineFile));
    }

    /// <summary>A root with partial hydration: a short read leaves the file partly on disk (proved by the fixture's
    /// status), and such a file is refused as a cloud file without any further download.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void APartlyDownloadedFileIsRefusedAsACloudFileWithoutADownload()
    {
        var status = partial.Status(PartialFile);
        Assert.IsTrue(status.IsPlaceholder, "not a placeholder");
        // The provider-side range hydration ran before serve started, so every fetch in the log came from someone
        // else (a scanner such as Defender); name it when the file is no longer partial.
        Assert.IsTrue(status.OnDisk > 0 && status.OnDisk < status.Size,
            $"the file is not partly on disk: {status.OnDisk} of {status.Size} bytes ({status.Hydration}); fetches since serve started: [{string.Join(" | ", partial.Fetches())}]");
        Expect.Refused(Gates.Check(partial.Combine(PartialFile), context: context), RefusalReason.CloudFile);
        Expect.Refused(Gates.Check(partial.Combine("partial"), context: context), RefusalReason.CloudFile);
        partial.AssertNoFetch("gate on a partly downloaded file");
        Assert.AreEqual(status.OnDisk, partial.Status(PartialFile).OnDisk);
    }

    /// <summary>An unpopulated (on-demand) directory placeholder is what carries RECALL_ON_OPEN: it is never opened
    /// (FileFacts says "cloud, not opened"), the gate refuses it as a cloud file, and so is a folder holding it.
    /// Ignored with the facts measured on the test machine (2026-10-16, fixture `register --population partial` and a
    /// directory placeholder created without "disable on-demand population"): under population policy ALWAYS_FULL
    /// such a directory cannot be created (CfCreatePlaceholders 0x8007017C "The cloud operation is invalid"); under
    /// PARTIAL it is created, but with no provider answering FETCH_PLACEHOLDERS every name lookup in the root fails
    /// with 0x801F0005 (ERROR_FLT_INVALID_NAME_REQUEST): creating a sibling folder, listing the directory and the
    /// fixture's own status. Serving population (a FETCH_PLACEHOLDERS handler) would be a further fixture extension
    /// and would populate the folder on the first listing, which removes the very attribute under test.</summary>
    [Ignore("question 2 remains: an on-demand directory placeholder needs a FETCH_PLACEHOLDERS provider (0x801F0005 without one); see the remarks")]
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARecallOnOpenFolderIsNeverOpenedAndRefusedAsACloudFile()
    {
        var relative = @"holder\" + OnDemandFolder;
        var status = partial.Status(relative);
        Assert.IsTrue(status.IsPlaceholder, "not a placeholder");
        Assert.IsTrue(status.ListingRecallOnOpen, "the directory listing does not show RECALL_ON_OPEN on the on-demand folder");
        var path = partial.Combine(relative);
        var facts = FileFacts.Read(path);
        Assert.IsTrue(facts.IsCloudNotOpened, "the item was opened");
        Assert.IsFalse(facts.Succeeded);
        Assert.IsNull(facts.Stamp);
        Expect.Refused(Gates.CheckWith(path, Gates.AnyStamp, context: context), RefusalReason.CloudFile);
        Expect.Refused(Gates.Check(partial.Combine("holder"), context: context), RefusalReason.CloudFile);
        partial.AssertNoFetch("gate on an on-demand folder");
        Assert.IsTrue(partial.Status(relative).ListingRecallOnOpen, "the folder was populated by the gate");
    }
}
