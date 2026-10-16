using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Spec "In use", Windows note (intent criterion 6): any method naming a holder makes the item in use, with
/// the holder's id and image where readable. Fixtures live in the Defender-excluded test data folder.</summary>
[TestClass]
public sealed class InUseTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InTestData("inuse");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    /// <summary>An approved item built directly (the checker is tested on its own; the gate's own tests cover approval).</summary>
    internal static ApprovedItem Item(string path)
    {
        var stamp = Gates.StampOf(path);
        var canonical = Win32.FinalPath(path);
        var facts = new ItemFacts(canonical, Gates.Context.ProfileFolder, Gates.Context.ProfilesFolder, stamp.Identity.VolumeSerial, stamp.Identity.VolumeSerial, [], false);
        return new ApprovedItem(canonical, stamp, 0, 0, stamp.Length is null ? 0 : 1, facts);
    }

    private static InUseResult Check(string path) => new InUseChecker().Check(Item(path));

    private static InUseHolder HolderNamed(InUseResult result, int processId)
    {
        Assert.IsTrue(result.IsInUse, "not in use");
        var holder = result.Holders.FirstOrDefault(h => h.ProcessId == processId);
        Assert.IsTrue(holder.ProcessId == processId, $"holder {processId} not named; named: {string.Join(", ", result.Holders.Select(h => $"{h.ProcessId} {h.ImageName} ({h.Method})"))}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(holder.Method), "the method that named the holder");
        return holder;
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileNobodyHoldsIsNotInUse()
    {
        var file = folder.File("idle.bin", 4096);
        var result = Check(file);
        Assert.IsFalse(result.IsInUse, string.Join(", ", result.Holders.Select(h => $"{h.ProcessId} {h.ImageName} ({h.Method})")));
        Assert.IsEmpty(result.Holders);
        // Other accounts' processes (SYSTEM's, protected ones) exist on the machine and cannot be read by a standard user: counted, never blocking.
        Assert.IsTrue(result.UnreadableProcessCount > 0, "no process was reported unreadable, yet a standard user cannot read every process on the machine");
    }

    /// <summary>The third method: a process whose image is inside the item (a copy of cmd.exe running from the folder).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AProcessWhoseImageIsInsideTheItemMakesItInUse()
    {
        var dir = folder.Dir("image");
        var exe = folder.Combine(@"image\holder-cmd.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), exe);
        using var holder = HolderProcess.StartImage(exe);
        var named = HolderNamed(Check(dir), holder.ProcessId);
        Assert.IsTrue(named.ImageName?.Contains("holder-cmd", StringComparison.OrdinalIgnoreCase), "image name: " + named.ImageName);
        HolderNamed(Check(exe), holder.ProcessId);
    }

    /// <summary>The third method: a process whose command line names a file inside the item, without opening it.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AProcessWhoseCommandLineNamesTheItemMakesItInUse()
    {
        var dir = folder.Dir("named");
        var file = folder.File(@"named\argument.txt", 16);
        using var holder = HolderProcess.Start(HoldKind.CommandLineOnly, file);
        HolderNamed(Check(file), holder.ProcessId);
        HolderNamed(Check(dir), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOpenWithoutDeleteSharingIsInUse()
    {
        var file = folder.File("open.bin", 4096);
        using var holder = HolderProcess.Start(HoldKind.OpenNoDeleteSharing, file);
        var named = HolderNamed(Check(file), holder.ProcessId);
        Assert.IsTrue(named.ImageName?.Contains("powershell", StringComparison.OrdinalIgnoreCase), "image name where readable: " + named.ImageName);
    }

    /// <summary>Spec "In use", Windows note: the per-file process query **and** the Restart Manager both name a holder
    /// that has a file open without delete sharing. The values of <see cref="InUseHolder.Method"/> are not documented
    /// on the public surface, so the test asserts that at least two different methods named the holder's process, and
    /// that one of them is the Restart Manager (by name) when the methods are named at all.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOpenWithoutDeleteSharingIsNamedByTheRestartManagerToo()
    {
        var file = folder.File("open-rm.bin", 4096);
        using var holder = HolderProcess.Start(HoldKind.OpenNoDeleteSharing, file);
        var result = Check(file);
        HolderNamed(result, holder.ProcessId);
        var methods = result.Holders.Where(h => h.ProcessId == holder.ProcessId).Select(h => h.Method).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.IsTrue(methods.Count >= 2, "only one method named the holder: " + string.Join(", ", methods));
        Assert.IsTrue(methods.Any(m => m.Contains("Restart", StringComparison.OrdinalIgnoreCase)), "no method names the Restart Manager: " + string.Join(", ", methods));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFileOpenWithDeleteSharingIsInUse()
    {
        var file = folder.File("open-shared.bin", 4096);
        using var holder = HolderProcess.Start(HoldKind.OpenWithDeleteSharing, file);
        HolderNamed(Check(file), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ALoadedLibraryIsInUse()
    {
        var dll = folder.Combine("loaded.dll");
        File.Copy(Path.Combine(Environment.SystemDirectory, "version.dll"), dll);
        using var holder = HolderProcess.Start(HoldKind.LoadedLibrary, dll);
        HolderNamed(Check(dll), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AReadOnlyMappedViewIsInUse()
    {
        var file = folder.File("mapped.bin", 65536);
        using var holder = HolderProcess.Start(HoldKind.MappedView, file);
        HolderNamed(Check(file), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderUsedAsACurrentDirectoryIsInUse()
    {
        var dir = folder.Dir("cwd");
        using var holder = HolderProcess.Start(HoldKind.CurrentDirectory, dir);
        HolderNamed(Check(dir), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AFolderHoldingAnOpenFileIsInUse()
    {
        var dir = folder.Dir("tree");
        var file = folder.File(@"tree\deep\open.bin", 4096);
        using var holder = HolderProcess.Start(HoldKind.OpenNoDeleteSharing, file);
        HolderNamed(Check(dir), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AHolderThroughAnotherSpellingIsFound()
    {
        var file = folder.File("another spelling of the name.bin", 4096);
        var shortPath = Win32.ShortPath(file);
        Assert.AreNotEqual(file, shortPath, "no 8.3 names on the system volume");
        using var holder = HolderProcess.Start(HoldKind.OpenNoDeleteSharing, shortPath);
        HolderNamed(Check(file), holder.ProcessId);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AfterTheHolderExitsTheItemIsNotInUse()
    {
        var file = folder.File("released.bin", 4096);
        using (var holder = HolderProcess.Start(HoldKind.OpenNoDeleteSharing, file))
        {
            HolderNamed(Check(file), holder.ProcessId);
        }
        Assert.IsFalse(Check(file).IsInUse);
    }
}
