using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Ambysto.Kit.Safety.Internal;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>States no real machine produces, made through the test-only fault seam (<see cref="Faults"/>, one injection
/// per flow): a system query that errs (G21, "Windows: failing closed": "No check passes on an error"), an item marked
/// recall-on-open (G20, step 2), a file Windows Resource Protection covers (G22, step 11), an item replaced while the
/// gate runs (G23, step 4 "unchanged since it was shown"), a WOF file whose tag the listing shows (G25, step 3), and
/// a recall attribute on an entry that is fully on disk (G26, "Two signals, either refuses").
/// Every stamp is read before its fault is injected, every injection ends with its scope, and the control (the same
/// item with no fault) is approved before and after, so a left-over injection would show.</summary>
[TestClass]
public sealed class FaultSeamTests
{
    /// <summary>Which paths a fault is limited to.</summary>
    public enum Target
    {
        /// <summary>Every call (queries that take a volume root or no path).</summary>
        Any,
        /// <summary>The item itself.</summary>
        Item,
        /// <summary>A file inside the folder item (<c>item\sub\inner.txt</c>).</summary>
        InsideFile,
        /// <summary>A sub-folder of the folder item (<c>item\sub</c>).</summary>
        InsideFolder,
        /// <summary>The folder above the item.</summary>
        Above,
    }

    public enum Shape
    {
        File,
        Folder,
    }

    /// <summary>Query names for the data rows (an attribute argument cannot name the internal enum directly).</summary>
    private static class Q
    {
        public const string AllocatedSize = nameof(Faults.Query.AllocatedSize);
        public const string AttributesByName = nameof(Faults.Query.AttributesByName);
        public const string FileId = nameof(Faults.Query.FileId);
        public const string FileSystem = nameof(Faults.Query.FileSystem);
        public const string FinalPath = nameof(Faults.Query.FinalPath);
        public const string ListFirst = nameof(Faults.Query.ListFirst);
        public const string ListNext = nameof(Faults.Query.ListNext);
        public const string Owner = nameof(Faults.Query.Owner);
        public const string PlaceholderInfo = nameof(Faults.Query.PlaceholderInfo);
        public const string ProcessesUsingFile = nameof(Faults.Query.ProcessesUsingFile);
        public const string RecycleBinUsage = nameof(Faults.Query.RecycleBinUsage);
        public const string ResourceProtection = nameof(Faults.Query.ResourceProtection);
        public const string RestartManagerList = nameof(Faults.Query.RestartManagerList);
        public const string RestartManagerRegister = nameof(Faults.Query.RestartManagerRegister);
        public const string RestartManagerStart = nameof(Faults.Query.RestartManagerStart);
        public const string Size = nameof(Faults.Query.Size);
        public const string SyncRoot = nameof(Faults.Query.SyncRoot);
        public const string TimesAndLength = nameof(Faults.Query.TimesAndLength);
        public const string VolumeName = nameof(Faults.Query.VolumeName);
        public const string VolumeRoot = nameof(Faults.Query.VolumeRoot);
    }

    private TempFolder folder = null!;
    private string file = "";
    private string item = "";
    private string insideFolder = "";
    private string insideFile = "";

    /// <summary>The shared context is resolved before any fault is injected: a context built under a fault would be wrong.</summary>
    [ClassInitialize]
    public static void ResolveContext(TestContext _) => GC.KeepAlive(Gates.Context);

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("seam");
        file = folder.File("plain.txt", 100);
        item = folder.Dir("item");
        folder.File(@"item\top.txt", 16);
        insideFolder = folder.Dir(@"item\sub");
        insideFile = folder.File(@"item\sub\inner.txt", 32);
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    /// <summary>True for the plain and the canonical spelling of <paramref name="path"/> (the seam sees both).</summary>
    private static Func<string, bool> PathIs(string path)
    {
        var plain = path.TrimEnd('\\');
        var canonical = Win32.FinalPath(path).TrimEnd('\\');
        return p =>
        {
            var q = p.TrimEnd('\\');
            return string.Equals(q, plain, StringComparison.OrdinalIgnoreCase) || string.Equals(q, canonical, StringComparison.OrdinalIgnoreCase);
        };
    }

    private string PathOf(Shape shape) => shape == Shape.File ? file : item;

    private Func<string, bool>? Where(Target target, Shape shape) => target switch
    {
        Target.Any => null,
        Target.Item => PathIs(PathOf(shape)),
        Target.InsideFile => PathIs(insideFile),
        Target.InsideFolder => PathIs(insideFolder),
        Target.Above => PathIs(folder.Path),
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static Faults.Query Query(string name) => Enum.Parse<Faults.Query>(name);

    private static Exception Throws(Func<object?> call, string what)
    {
        object? returned;
#pragma warning disable CA1031 // any exception is the closed answer; what it is gets printed
        try
        {
            returned = call();
        }
        catch (Exception e) when (e is not AssertFailedException)
        {
            return e;
        }
#pragma warning restore CA1031
        throw new AssertFailedException($"{what}: no exception; returned {Describe(returned)}");
    }

    private static string Describe(object? value) => value switch
    {
        InUseResult r => $"IsInUse={r.IsInUse}, holders=[{string.Join(", ", r.Holders.Select(h => $"{h.ProcessId} {h.ImageName} ({h.Method})"))}]",
        null => "null",
        _ => value.ToString() ?? value.GetType().Name,
    };

    // ---- G21: a system query that errs refuses as "Could not verify", for the item and for what is inside it ----

    /// <summary>"Windows: failing closed": "Every check that cannot establish its fact (an error ...) refuses the item
    /// with 'Could not verify: &lt;check&gt;'. No check passes on an error." One row per query, at each place it runs: on
    /// the item, on a file or folder inside it, on the folder above it (identity of every folder above, step 11).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Q.AttributesByName, Target.Item, Shape.File)]
    [DataRow(Q.AttributesByName, Target.Item, Shape.Folder)]
    [DataRow(Q.FileId, Target.Item, Shape.File)]
    [DataRow(Q.FileId, Target.Item, Shape.Folder)]
    [DataRow(Q.FileId, Target.Above, Shape.File)]
    [DataRow(Q.TimesAndLength, Target.Item, Shape.File)]
    [DataRow(Q.FinalPath, Target.Item, Shape.File)]
    [DataRow(Q.FileSystem, Target.Item, Shape.File)]
    [DataRow(Q.VolumeRoot, Target.Item, Shape.File)]
    [DataRow(Q.VolumeName, Target.Any, Shape.File)]
    [DataRow(Q.RecycleBinUsage, Target.Any, Shape.File)]
    [DataRow(Q.Size, Target.Item, Shape.File)]
    [DataRow(Q.AllocatedSize, Target.Item, Shape.File)]
    [DataRow(Q.AllocatedSize, Target.InsideFile, Shape.Folder)]
    [DataRow(Q.ListFirst, Target.Item, Shape.Folder)]
    [DataRow(Q.ListFirst, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.ListNext, Target.Item, Shape.Folder)]
    [DataRow(Q.ListNext, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.PlaceholderInfo, Target.Item, Shape.File)]
    [DataRow(Q.PlaceholderInfo, Target.InsideFile, Shape.Folder)]
    [DataRow(Q.SyncRoot, Target.Item, Shape.File)]
    [DataRow(Q.SyncRoot, Target.Item, Shape.Folder)]
    [DataRow(Q.SyncRoot, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.Owner, Target.Item, Shape.File)]
    [DataRow(Q.Owner, Target.InsideFile, Shape.Folder)]
    [DataRow(Q.Owner, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.ResourceProtection, Target.Item, Shape.File)]
    [DataRow(Q.ResourceProtection, Target.InsideFile, Shape.Folder)]
    public void AnErringQueryRefusesAsCouldNotVerify(string query, Target target, Shape shape)
    {
        var path = PathOf(shape);
        var request = Gates.Request(path);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
        GateResult result;
        using (Faults.Fail(Query(query), Where(target, shape)))
        {
            result = Gates.Seamed().Check(request);
        }
        Expect.CouldNotVerify(result);
        Console.WriteLine($"{query} erring on {target} of a {shape}: {result.Message}");
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
    }

    /// <summary>The reparse-tag query runs only on an item whose attributes say "reparse point" (step 3); a junction is
    /// one a standard user can make. When the query errs the junction is "Could not verify", not "a link" and never
    /// approved; FileFacts reports the same reason.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnErringReparseTagQueryRefusesAsCouldNotVerify()
    {
        var target = folder.Dir("target");
        var junction = folder.Dir("junction");
        Win32.SetJunction(junction, target);
        Expect.Refused(Gates.CheckWith(junction, Gates.AnyStamp), RefusalReason.LinkOrSpecialFile);
        GateResult result;
        FileFactsResult facts;
        using (Faults.Fail(Faults.Query.AttributeTag, PathIs(junction)))
        {
            result = Gates.CheckWith(junction, Gates.AnyStamp);
            facts = FileFacts.Read(junction);
        }
        Expect.CouldNotVerify(result);
        Assert.IsFalse(facts.Succeeded);
        Assert.AreEqual(RefusalReason.CouldNotVerify, facts.FailureReason);
        Expect.Refused(Gates.CheckWith(junction, Gates.AnyStamp), RefusalReason.LinkOrSpecialFile);
    }

    /// <summary>Step 1 through the real environment facts: when the token-elevation query errs, the gate and the put-back
    /// check refuse as "Could not verify" before any decision on the session. The control is whatever the real
    /// session gives (approved on the console, "not interactive" over SSH), never "Could not verify".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnErringTokenElevationQueryRefusesAsCouldNotVerify()
    {
        var request = Gates.Request(file);
        var destination = folder.Combine("restored.txt");
        GateResult result;
        GateResult restore;
        using (Faults.Fail(Faults.Query.TokenElevation))
        {
            result = Gates.Seamed(environment: SystemEnvironmentFacts.Instance).Check(request);
            restore = Gates.Seamed(environment: SystemEnvironmentFacts.Instance).CheckRestoreDestination(destination);
        }
        Expect.CouldNotVerify(result);
        Expect.CouldNotVerify(restore);
        var control = Gates.Seamed(environment: SystemEnvironmentFacts.Instance).Check(request);
        Assert.AreNotEqual(RefusalReason.CouldNotVerify, control.Reason, $"the real environment without a fault: {control.Message}");
        Assert.IsTrue(control.IsApproved || control.Reason == RefusalReason.NotInteractive, $"the real environment without a fault: {control.Reason} {control.Message}");
    }

    /// <summary>"Windows: failing closed" (owner decision 2026-10-17): "A query the system may never answer counts as an
    /// error after a time limit: the Recycle Bin usage query ... answers within 10 s or the item is refused with 'Could
    /// not verify: Recycle Bin settings'. The query that did not answer is left running on its own thread." The hook
    /// sleeps past the limit on the query's own thread (which inherits the injection); the gate answers after about
    /// the limit, well before the sleep ends, and the next check is not held up by the thread left behind.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [Timeout(60000)]
    public void ARecycleBinUsageQueryThatDoesNotAnswerWithinTheLimitIsCouldNotVerify()
    {
        var request = Gates.Request(file);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
        var limit = TimeSpan.FromSeconds(10);
        var sleep = TimeSpan.FromSeconds(13);
        var testThread = Environment.CurrentManagedThreadId;
        var hookThread = 0;
        var hookBackground = false;
        var ran = 0;
        var stopwatch = Stopwatch.StartNew();
        GateResult result;
        using (Faults.Hook(Faults.Query.RecycleBinUsage, _ =>
        {
            Interlocked.Increment(ref ran);
            hookThread = Environment.CurrentManagedThreadId;
            hookBackground = Thread.CurrentThread.IsBackground;
            Thread.Sleep(sleep);
        }))
        {
            result = Gates.Seamed().Check(request);
        }
        stopwatch.Stop();
        Assert.AreEqual(1, ran, "the hook did not run once");
        Assert.AreNotEqual(testThread, hookThread, "the query did not run on a thread of its own");
        Assert.IsTrue(hookBackground, "H25: the query's thread is not a background thread: left behind, it would keep the app from exiting");
        Expect.CouldNotVerify(result);
        Assert.AreEqual("Recycle Bin settings", result.Detail);
        // Thread.Join's timer and Stopwatch differ by up to a timer tick (measured: a Join of 10 s read as 9.99 s).
        Assert.IsTrue(stopwatch.Elapsed >= limit - TimeSpan.FromMilliseconds(100), $"answered after {stopwatch.Elapsed.TotalSeconds:F3} s, before the {limit.TotalSeconds} s limit");
        Assert.IsTrue(stopwatch.Elapsed < sleep - TimeSpan.FromSeconds(1), $"answered after {stopwatch.Elapsed.TotalSeconds:F1} s: it waited for the query ({sleep.TotalSeconds} s), not for the limit");
        var again = Stopwatch.StartNew();
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
        Assert.IsTrue(again.Elapsed < TimeSpan.FromSeconds(2), $"the next check took {again.Elapsed.TotalSeconds:F1} s: held up by the thread left behind");
    }

    /// <summary>"Windows: failing closed": the usage query runs on a thread of its own; an exception there is carried back
    /// to the caller and is "Could not verify: Recycle Bin settings", not the end of the process (3771788). The next
    /// check is approved: the process survived and nothing stays injected.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ARecycleBinUsageQueryThatThrowsOnItsThreadIsCouldNotVerify()
    {
        var request = Gates.Request(file);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
        GateResult result;
        using (Faults.Hook(Faults.Query.RecycleBinUsage, _ => throw new InvalidOperationException("thrown on the query thread")))
        {
            result = Gates.Seamed().Check(request);
        }
        Expect.CouldNotVerify(result);
        Assert.AreEqual("Recycle Bin settings", result.Detail);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
    }

    /// <summary>H07, step 7: "A cloud-tagged folder inside the item is not entered either, and the item is refused as a
    /// cloud file". A sub-folder listed with the recall-on-open attribute (no cloud tag) is not entered: once it has
    /// been reported, listing it is made to fail, and the item is still refused as a cloud file, not "could not
    /// verify" (the hand-over between the two injections is the mount-point test's).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderListedWithARecallAttributeIsNotEntered()
    {
        var request = Gates.Request(item);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
        var isSub = PathIs(insideFolder);
        var presented = 0;
        IDisposable? fail = null;
        GateResult result;
        var change = Faults.Change(Faults.Query.Listing, (path, attributes, tag) =>
        {
            if (!isSub(path))
            {
                return (attributes, tag);
            }
            presented++;
            fail ??= Faults.Fail(Faults.Query.ListFirst, isSub);
            return (attributes | Win32.AttrRecallOnOpen, tag);
        });
        try
        {
            result = Gates.Seamed().Check(request);
        }
        finally
        {
            fail?.Dispose();
            change.Dispose();
        }
        Assert.AreEqual(1, presented, "the sub-folder was not listed once");
        Expect.Refused(result, RefusalReason.CloudFile);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
    }

    /// <summary>H09, plan FileFacts ("decides nothing") and "Windows: failing closed": when the attribute read by name
    /// errs while the open would succeed, FileFacts reads no stamp and reports "could not verify" naming the
    /// existence check; it is not "cloud, not opened".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnErringAttributeReadMakesFileFactsCouldNotVerifyNotARead()
    {
        FileFactsResult facts;
        using (Faults.Fail(Faults.Query.AttributesByName, PathIs(file)))
        {
            facts = FileFacts.Read(file);
        }
        Assert.IsFalse(facts.Succeeded, "a stamp was read although the attribute read by name failed");
        Assert.IsNull(facts.Stamp);
        Assert.AreEqual(RefusalReason.CouldNotVerify, facts.FailureReason);
        Assert.AreEqual("exists", facts.Detail);
        Assert.IsFalse(facts.IsCloudNotOpened);
        Assert.IsTrue(FileFacts.Read(file).Succeeded);
    }

    /// <summary>H26 (a), the seam itself: a failure limited to another path leaves the item alone: the gate approves it
    /// and the in-use check answers "not in use". Without this, the targeted rows of the erring-query tests could pass
    /// because the fault also hit the item.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFaultLimitedToAnotherPathLeavesTheItemAlone()
    {
        var sibling = folder.File("sibling.txt", 10);
        var isSibling = PathIs(sibling);
        foreach (var shape in new[] { Shape.File, Shape.Folder })
        {
            var path = PathOf(shape);
            var request = Gates.Request(path);
            foreach (var query in new[] { Faults.Query.AttributesByName, Faults.Query.FileId, Faults.Query.Owner, Faults.Query.AllocatedSize, Faults.Query.SyncRoot, Faults.Query.ResourceProtection })
            {
                GateResult result;
                using (Faults.Fail(query, isSibling))
                {
                    result = Gates.Seamed().Check(request);
                }
                Assert.IsTrue(result.IsApproved, $"{query} limited to the sibling hit the {shape}: {result.Reason} {result.Message}");
            }
        }
        using var data = TempFolder.InTestData("seam-unrelated");
        var dataFile = data.File("plain.txt", 100);
        var dataSibling = data.File("sibling.txt", 100);
        var approved = InUseTests.Item(dataFile);
        foreach (var query in new[] { Faults.Query.ProcessesUsingFile, Faults.Query.AttributesByName, Faults.Query.Size })
        {
            InUseResult result;
            using (Faults.Fail(query, PathIs(dataSibling)))
            {
                result = new InUseChecker().Check(approved);
            }
            Assert.IsFalse(result.IsInUse, $"{query} limited to the sibling: {Describe(result)}");
        }
    }

    /// <summary>H26 (b), the seam itself: the open hook sees every open for attributes: the gate's and FileFacts' open
    /// of the item, and the attempt to open the target of a junction inside a folder (the positive control for the
    /// "never opened" proof of the recall-on-open test).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheOpenHookSeesEveryOpenIncludingALinkTargets()
    {
        var opened = new List<string>();
        var isFile = PathIs(file);
        var request = Gates.Request(file);
        using (Faults.Hook(Faults.Query.Open, opened.Add))
        {
            Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(file));
        }
        Assert.AreNotEqual(0, opened.Count(isFile), "the gate's open of the item was not seen: " + string.Join(", ", opened));
        opened.Clear();
        using (Faults.Hook(Faults.Query.Open, opened.Add))
        {
            Assert.IsTrue(FileFacts.Read(file).Succeeded);
        }
        Assert.AreNotEqual(0, opened.Count(isFile), "FileFacts' open of the item was not seen: " + string.Join(", ", opened));
        var target = folder.Dir("target");
        var junction = folder.Dir(@"item\junction");
        Win32.SetJunction(junction, target);
        var isJunction = PathIs(junction);
        var folderRequest = Gates.Request(item);
        opened.Clear();
        using (Faults.Hook(Faults.Query.Open, opened.Add))
        {
            Expect.Approved(Gates.Seamed().Check(folderRequest), Win32.FinalPath(item));
        }
        Assert.AreNotEqual(0, opened.Count(isJunction), "the attempt to open the junction's target was not seen: " + string.Join(", ", opened));
    }

    /// <summary>The mount-point target's path query. A standard user's own junction is one Windows refuses to follow
    /// (measured here: ERROR_UNTRUSTED_MOUNT_POINT), so the target is never opened and its path never asked; the
    /// state is made instead by presenting a plain sub-folder to the walk as a mount-point tag (the listing seam) and
    /// failing the target's path query on it. A target whose kind cannot be told is a mount point to the gate: the
    /// folder is refused as "contains a link or special file" (recoverable moves: "Any other failure counts as a mount
    /// point"), never approved. The two injections never overlap: the listing change hands over to the failure once it
    /// has reported the sub-folder, and the scopes end in reverse order.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnErringPathQueryOnALinkTargetInsideRefusesTheFolderAsHoldingAMountPoint()
    {
        var request = Gates.Request(item);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
        var isSub = PathIs(insideFolder);
        var presented = 0;
        IDisposable? fail = null;
        GateResult result;
        var change = Faults.Change(Faults.Query.Listing, (path, attributes, tag) =>
        {
            if (!isSub(path))
            {
                return (attributes, tag);
            }
            presented++;
            fail ??= Faults.Fail(Faults.Query.FinalPath, isSub);
            return (attributes | Win32.AttrReparsePoint, Win32.TagMountPoint);
        });
        try
        {
            result = Gates.Seamed().Check(request);
        }
        finally
        {
            fail?.Dispose();
            change.Dispose();
        }
        Assert.AreEqual(1, presented, "the sub-folder was not listed once");
        Assert.IsNotNull(fail, "the path query was never made to fail");
        Expect.Refused(result, RefusalReason.ContainsLinkOrSpecialFile);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
    }

    /// <summary>The same query on a junction Windows does follow (the administrator-made one of the AdminSetup fixtures):
    /// the folder holding it is approved without the fault (AdminSetupTests) and refused as holding a mount point when
    /// the target's path query errs.</summary>
    [TestMethod]
    [TestCategory(Cat.AdminSetup)]
    public void AnErringPathQueryOnAFollowableJunctionInsideRefusesTheFolderAsHoldingAMountPoint()
    {
        var handoff = AdminHandoff.Read();
        var withJunction = handoff.FolderWithAdminJunction;
        var junction = Path.Combine(withJunction, "junction");
        Assert.IsTrue(File.Exists(Path.Combine(junction, "inner.txt")), "precondition: the junction can be followed by this user");
        var request = Gates.Request(withJunction);
        GateResult result;
        using (Faults.Fail(Faults.Query.FinalPath, PathIs(junction)))
        {
            result = Gates.Seamed(policy: FakeRecycleBinPolicy.Allowed).Check(request);
        }
        Expect.Refused(result, RefusalReason.ContainsLinkOrSpecialFile);
        Expect.Approved(Gates.Seamed(policy: FakeRecycleBinPolicy.Allowed).Check(request), Win32.FinalPath(withJunction));
    }

    /// <summary>"In use", Windows note: an item is in use when any method names a holder, for every file and folder in
    /// its tree. The checker has no "could not verify" answer, so when a query it relies on errs it throws; it never
    /// answers "not in use". Fixtures live in the Defender-excluded test data folder, so the control is "not in use".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Q.ProcessesUsingFile, Target.Item, Shape.File)]
    [DataRow(Q.ProcessesUsingFile, Target.InsideFile, Shape.Folder)]
    [DataRow(Q.ProcessesUsingFile, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.RestartManagerStart, Target.Any, Shape.File)]
    [DataRow(Q.RestartManagerRegister, Target.Any, Shape.File)]
    [DataRow(Q.RestartManagerList, Target.Any, Shape.Folder)]
    [DataRow(Q.ListFirst, Target.Item, Shape.Folder)]
    [DataRow(Q.ListNext, Target.InsideFolder, Shape.Folder)]
    [DataRow(Q.Size, Target.Item, Shape.File)]
    [DataRow(Q.AllocatedSize, Target.InsideFile, Shape.Folder)]
    [DataRow(Q.AttributesByName, Target.Item, Shape.Folder)]
    [DataRow(Q.AttributesByName, Target.Item, Shape.File)]
    public void AnErringQueryMakesTheInUseCheckThrowNeverNotInUse(string query, Target target, Shape shape)
    {
        using var data = TempFolder.InTestData("seam-inuse");
        var dataFile = data.File("plain.txt", 100);
        var dataItem = data.Dir("item");
        data.File(@"item\top.txt", 16);
        var dataInsideFolder = data.Dir(@"item\sub");
        var dataInsideFile = data.File(@"item\sub\inner.txt", 32);
        var path = shape == Shape.File ? dataFile : dataItem;
        Func<string, bool>? where = target switch
        {
            Target.Any => null,
            Target.Item => PathIs(path),
            Target.InsideFile => PathIs(dataInsideFile),
            Target.InsideFolder => PathIs(dataInsideFolder),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        var approved = InUseTests.Item(path);
        var checker = new InUseChecker();
        var control = checker.Check(approved);
        Assert.IsFalse(control.IsInUse, "control: " + Describe(control));
        using (Faults.Fail(Query(query), where))
        {
            var thrown = Throws(() => checker.Check(approved), $"{query} erring on {target} of a {shape}");
            Console.WriteLine($"{query} erring on {target} of a {shape}: {thrown.GetType().Name}: {thrown.Message}");
        }
        var after = checker.Check(approved);
        Assert.IsFalse(after.IsInUse, "after the fault: " + Describe(after));
    }

    // ---- G20: an item marked recall-on-open is never opened ----

    /// <summary>Step 2: "an item marked 'recall on open' is never opened and goes straight to the cloud refusal (step 9)";
    /// plan FileFacts: "never opens a RECALL_ON_OPEN item (it returns 'cloud, not opened', no stamp)". The attribute read
    /// by name reports the mark; from that read on every open is recorded through the seam (the change hands over to the
    /// open hook once it has marked the item, so the two never overlap), and none may happen.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Shape.File)]
    [DataRow(Shape.Folder)]
    public void AnItemMarkedRecallOnOpenIsRefusedAsACloudFileWithoutBeingOpened(Shape shape)
    {
        var path = PathOf(shape);
        var request = Gates.Request(path);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
        var isItem = PathIs(path);

        (T Result, int Marked, List<string> Opened) Marked<T>(Func<T> call)
        {
            var opened = new List<string>();
            var marked = 0;
            IDisposable? hook = null;
            var change = Faults.Change(Faults.Query.AttributesByName, (p, attributes, tag) =>
            {
                if (!isItem(p))
                {
                    return (attributes, tag);
                }
                marked++;
                hook ??= Faults.Hook(Faults.Query.Open, opened.Add);
                return (attributes | Win32.AttrRecallOnOpen, tag);
            });
            try
            {
                return (call(), marked, opened);
            }
            finally
            {
                hook?.Dispose();
                change.Dispose();
            }
        }

        var (result, gateMarked, gateOpened) = Marked(() => Gates.Seamed().Check(request));
        Assert.AreEqual(1, gateMarked, "the gate did not read the item's attributes by name exactly once before anything else");
        Expect.Refused(result, RefusalReason.CloudFile);
        Assert.IsEmpty(gateOpened, "the gate opened: " + string.Join(", ", gateOpened));

        var (facts, factsMarked, factsOpened) = Marked(() => FileFacts.Read(path));
        Assert.AreEqual(1, factsMarked, "FileFacts did not read the item's attributes by name exactly once before anything else");
        Assert.IsTrue(facts.IsCloudNotOpened, "FileFacts did not report cloud, not opened");
        Assert.IsFalse(facts.Succeeded);
        Assert.IsNull(facts.Stamp);
        Assert.IsNull(facts.FailureReason, "cloud, not opened is not a failure: " + facts.FailureReason);
        Assert.IsEmpty(factsOpened, "FileFacts opened: " + string.Join(", ", factsOpened));

        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
        Assert.IsTrue(FileFacts.Read(path).Succeeded);
    }

    // ---- G22: the Windows Resource Protection backstop ----

    /// <summary>Step 11: "neither the item nor anything in it ... protected by Windows Resource Protection", "Owned by
    /// Windows". Every earlier check passes (the control is approved); only the protection query answers "covered".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Target.Item)]
    [DataRow(Target.InsideFile)]
    public void AFileWindowsResourceProtectionCoversIsRefusedAsOwnedByWindows(Target target)
    {
        var shape = target == Target.Item ? Shape.File : Shape.Folder;
        var path = PathOf(shape);
        var request = Gates.Request(path);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
        GateResult result;
        using (Faults.Report(Faults.Query.ResourceProtection, Where(target, shape)))
        {
            result = Gates.Seamed().Check(request);
        }
        Expect.Refused(result, RefusalReason.SystemOwned);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
    }

    // ---- G23: the item replaced while the gate runs ----

    /// <summary>Step 4 (shared gate): "unchanged since it was shown"; step 11 reads the item's identity again with the
    /// protection check. Between the stamp (step 6) and that read the item is renamed away and a new one is created at
    /// its path, the same length and write time, so only the identity differs: "Changed since the scan".</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Shape.File)]
    [DataRow(Shape.Folder)]
    public void AnItemReplacedWhileTheGateRunsIsChangedSinceScan(Shape shape)
    {
        var path = PathOf(shape);
        var request = Gates.Request(path);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(path));
        var away = folder.Combine("moved-away");
        var replaced = 0;
        GateResult result;
        using (Faults.Hook(Faults.Query.BeforeProtectionIdentity, _ =>
        {
            replaced++;
            if (shape == Shape.File)
            {
                var content = File.ReadAllBytes(path);
                var time = File.GetLastWriteTimeUtc(path);
                File.Move(path, away);
                File.WriteAllBytes(path, content);
                File.SetLastWriteTimeUtc(path, time);
            }
            else
            {
                var time = Directory.GetLastWriteTimeUtc(path);
                Directory.Move(path, away);
                Directory.CreateDirectory(path);
                Directory.SetLastWriteTimeUtc(path, time);
            }
        }))
        {
            result = Gates.Seamed().Check(request);
        }
        Assert.AreEqual(1, replaced, "the hook between the stamp and the protection identity did not run once");
        Expect.Refused(result, RefusalReason.ChangedSinceScan);
        Assert.AreNotEqual(request.ShownStamp.Identity, Gates.StampOf(path).Identity, "the replacement has the old identity");
    }

    // ---- G25: a WOF file whose tag the listing shows ----

    /// <summary>Step 3 and recoverable moves: "A compressed (WOF) file, on its own or in a folder ... is allowed", "whether
    /// or not Windows shows its tag". A standard user never sees the tag (measured), so the listing is made to show it
    /// (reparse attribute, tag 0x80000017) on one file: the folder is approved and the file counts in LogicalBytes.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAWofFileWhoseTagTheListingShowsIsApprovedAndTheFileCounted()
    {
        var request = Gates.Request(item);
        var plain = Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
        var expectedBytes = new DirectoryInfo(item).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        Assert.AreEqual(expectedBytes, plain.LogicalBytes, "control");
        var isFile = PathIs(insideFile);
        var shown = 0;
        GateResult result;
        using (Faults.Change(Faults.Query.Listing, (path, attributes, tag) =>
        {
            if (!isFile(path))
            {
                return (attributes, tag);
            }
            shown++;
            return (attributes | Win32.AttrReparsePoint, Win32.TagWofCompressed);
        }))
        {
            result = Gates.Seamed().Check(request);
        }
        Assert.AreEqual(1, shown, "the file was not listed once");
        var approved = Expect.Approved(result, Win32.FinalPath(item));
        Assert.AreEqual(expectedBytes, approved.LogicalBytes, "LogicalBytes does not include the WOF file");
        Assert.AreEqual(plain.FileCount, approved.FileCount, "the WOF file is not counted");
        Assert.AreEqual(plain.LogicalBytes, approved.LogicalBytes);
    }

    // ---- G26: a recall attribute alone, on an entry that is fully on disk ----

    /// <summary>Recoverable moves: "Two signals, either refuses: the 'recall on data access' or 'recall on open'
    /// attributes, and fewer bytes on disk than the file's length"; step 7: a cloud-tagged folder inside is not entered
    /// and the item is refused as a cloud file. The entry is a plain file or folder, fully on disk (the real placeholder
    /// query says so); only the listing shows the attribute.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    [DataRow(Target.InsideFile, Win32.AttrRecallOnDataAccess)]
    [DataRow(Target.InsideFile, Win32.AttrRecallOnOpen)]
    [DataRow(Target.InsideFolder, Win32.AttrRecallOnDataAccess)]
    [DataRow(Target.InsideFolder, Win32.AttrRecallOnOpen)]
    public void AnEntryListedWithARecallAttributeWhileFullyOnDiskRefusesTheFolderAsACloudFile(Target target, uint attribute)
    {
        var entry = target == Target.InsideFile ? insideFile : insideFolder;
        Assert.AreEqual(0u, Win32.Attributes(entry) & (Win32.AttrRecallOnOpen | Win32.AttrRecallOnDataAccess | Win32.AttrReparsePoint), "precondition: a plain entry");
        if (target == Target.InsideFile)
        {
            Assert.IsFalse(Cloud.NotFullyLocal(Win32.FinalPath(insideFile), new FileInfo(insideFile).Length), "precondition: the placeholder query reports the file fully on disk");
        }
        var request = Gates.Request(item);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
        var isEntry = PathIs(entry);
        var shown = 0;
        GateResult result;
        using (Faults.Change(Faults.Query.Listing, (path, attributes, tag) =>
        {
            if (!isEntry(path))
            {
                return (attributes, tag);
            }
            shown++;
            return (attributes | attribute, tag);
        }))
        {
            result = Gates.Seamed().Check(request);
        }
        Assert.AreEqual(1, shown, "the entry was not listed once");
        Expect.Refused(result, RefusalReason.CloudFile);
        Expect.Approved(Gates.Seamed().Check(request), Win32.FinalPath(item));
    }
}
