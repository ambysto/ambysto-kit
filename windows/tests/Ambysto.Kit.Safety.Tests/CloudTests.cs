using System;
using System.IO;
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

    private static TempFolder folder = null!;
    private static CloudRoot cloud = null!;
    /// <summary>A second root with partial hydration (population stays always-full: a file placeholder cannot be
    /// created under partial population, 0x801F0005): a partly downloaded file lives here.</summary>
    private static CloudRoot partial = null!;
    private static SafetyContext context = null!;

    [ClassInitialize]
    public static void RegisterAndServe(TestContext _)
    {
        folder = TempFolder.InProfile("cloud");
        partial = CloudRoot.Register(folder.Combine("partial-root"), folder.Combine("partial-fetch.log"), "partial", "always-full");
        partial.Populate((PartialFile, PartialSize));
        partial.HydrateRange(PartialFile, 0, PartialBytes);
        partial.Serve();
        cloud = CloudRoot.Register(folder.Combine("root"), folder.Combine("fetch.log"));
        cloud.Populate((OnlineFile, OnlineSize), (NestedOnlineFile, 65536), (PinnedFile, 4096), (HydratedFile, HydratedSize), (PlaceholderFolder + "\\", 0));
        var pinned = cloud.Combine(PinnedFile);
        Win32.SetAttributes(pinned, Win32.Attributes(pinned) | Win32.AttrPinned);
        cloud.Serve();
        cloud.Hydrate(HydratedFile, HydratedSize);
        Directory.CreateDirectory(cloud.Combine("plain"));
        File.WriteAllText(cloud.Combine(PlainFile), "not a placeholder");
        Directory.CreateDirectory(cloud.Combine("wrap"));
        Directory.Move(cloud.Combine(PlaceholderFolder), cloud.Combine(@"wrap\" + PlaceholderFolder));
        // Resolved after registration, as an app resolves its context once per session.
        context = SafetyContext.ForCurrentUser([]);
    }

    [ClassCleanup]
    public static void Unregister()
    {
        cloud?.Dispose();
        partial?.Dispose();
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

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheRegisteredRootIsASyncRootOfTheContext()
        => Expect.ContainsPath(context.SyncRoots, Win32.FinalPath(cloud.Root), "registered sync roots");

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
        Assert.IsTrue(status.OnDisk > 0 && status.OnDisk < status.Size, $"the file is not partly on disk: {status.OnDisk} of {status.Size} bytes ({status.Hydration})");
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
