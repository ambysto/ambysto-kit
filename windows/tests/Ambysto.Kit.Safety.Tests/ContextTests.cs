using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>SafetyContext: profile folders, the never-touch rows and system locations resolved through known folders
/// and the environment (intent criterion 7), the app's additions (add-only).</summary>
[TestClass]
public sealed class ContextTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>A context resolved inside the test (not the one cached for the test process): under mutation testing
    /// a cached context was resolved before the mutant was active, so a mutated resolution was never seen (G28).</summary>
    private static SafetyContext Fresh => SafetyContext.ForCurrentUser([]);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheProfileFoldersAreResolved()
    {
        var context = Fresh;
        Assert.AreEqual(Win32.FinalPath(Profile), context.ProfileFolder);
        Assert.AreEqual(Path.GetDirectoryName(Win32.FinalPath(Profile)), context.ProfilesFolder);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void NeverTouchHoldsTheProfileRows()
    {
        var list = Fresh.NeverTouchFolders;
        foreach (var relative in new[] { @".claude\projects", ".ssh", ".gnupg", @"Apple\MobileSync\Backup" })
        {
            Expect.ContainsPath(list, Path.Combine(Profile, relative), relative);
        }
        Expect.ContainsPath(list, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures (known folder)");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void NeverTouchHoldsTheCredentialMailStoreAndBackupRows()
    {
        var list = Fresh.NeverTouchFolders;
        foreach (var relative in new[] { @"Microsoft\Protect", @"Microsoft\Credentials", @"Microsoft\Crypto", @"Microsoft\SystemCertificates", @"Apple Computer\MobileSync\Backup" })
        {
            Expect.ContainsPath(list, Path.Combine(Roaming, relative), "%APPDATA%\\" + relative);
        }
        foreach (var relative in new[] { @"Microsoft\Credentials", @"Microsoft\Vault", @"Microsoft\Outlook", "Packages" })
        {
            Expect.ContainsPath(list, Path.Combine(Local, relative), "%LOCALAPPDATA%\\" + relative);
        }
    }

    /// <summary>Never touch: "File History (`%LOCALAPPDATA%\Microsoft\Windows\FileHistory`, ...)": listed, and refused
    /// when the folder exists on this machine (the volume-root form is in AdminSetupTests).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void FileHistoryUnderLocalAppDataIsProtected()
    {
        var fileHistory = Path.Combine(Local, @"Microsoft\Windows\FileHistory");
        Expect.ContainsPath(Fresh.NeverTouchFolders, fileHistory, "File History configuration folder");
        if (Directory.Exists(fileHistory))
        {
            Expect.Refused(Gates.Check(fileHistory), RefusalReason.Protected);
        }
    }

    /// <summary>Browser profiles and Thunderbird: the spec names the vendor locations, not their exact depth, so an
    /// entry must cover (equal or be above) the profile data folder.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void NeverTouchCoversBrowserProfilesAndThunderbird()
    {
        var list = Fresh.NeverTouchFolders;
        Expect.Covers(list, Path.Combine(Local, @"Google\Chrome\User Data"), "Chrome");
        Expect.Covers(list, Path.Combine(Local, @"Microsoft\Edge\User Data"), "Edge");
        Expect.Covers(list, Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"), "Brave");
        Expect.Covers(list, Path.Combine(Roaming, @"Mozilla\Firefox\Profiles"), "Firefox");
        Expect.Covers(list, Path.Combine(Roaming, @"Thunderbird\Profiles"), "Thunderbird");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void SystemLocationsHoldTheSpecsRows()
    {
        var list = Fresh.SystemLocations;
        Expect.ContainsPath(list, Environment.GetEnvironmentVariable("SystemRoot")!, "%SystemRoot%");
        Expect.ContainsPath(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files");
        Expect.ContainsPath(list, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)");
        Expect.ContainsPath(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData");
        Expect.ContainsPath(list, Path.Combine(Local, @"Microsoft\WindowsApps"), "WindowsApps");
    }

    /// <summary>"On every volume": the system volume's entries, which exist on every Windows installation.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void SystemLocationsHoldThePerVolumeRowsOfTheSystemVolume()
    {
        var list = Fresh.SystemLocations;
        Expect.ContainsPath(list, Path.Combine(Volumes.System, "$Recycle.Bin"), "$Recycle.Bin on C:");
        Expect.ContainsPath(list, Path.Combine(Volumes.System, "System Volume Information"), "System Volume Information on C:");
        Expect.ContainsPath(list, Path.Combine(Volumes.System, "pagefile.sys"), "pagefile.sys on C:");
        Expect.ContainsPath(list, Path.Combine(Volumes.System, "Recovery"), "Recovery on C:");
    }

    /// <summary>The user's class hive and its logs sit in the profile and are never touched.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheUsrClassHiveIsProtected()
    {
        var hive = Path.Combine(Local, @"Microsoft\Windows\UsrClass.dat");
        Assert.IsTrue(File.Exists(hive), hive);
        Expect.Refused(Gates.Check(hive), RefusalReason.Protected);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void EveryEntryIsACanonicalPlainPath()
    {
        var context = Fresh;
        foreach (var entry in context.NeverTouchFolders.Concat(context.SystemLocations).Concat(context.SyncRoots).Append(context.ProfileFolder).Append(context.ProfilesFolder))
        {
            Assert.IsTrue(Path.IsPathFullyQualified(entry), entry);
            Assert.IsFalse(entry.StartsWith(@"\\?\", StringComparison.Ordinal), entry);
            Assert.IsFalse(entry.EndsWith('\\'), entry);
            Assert.IsFalse(entry.Contains('~', StringComparison.Ordinal), entry + ": 8.3 names are expanded");
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AppFoldersAreAddedAndNothingIsRemoved()
    {
        using var folder = TempFolder.InProfile("ctx");
        var app = folder.Dir("app");
        var baseline = SafetyContext.ForCurrentUser([]);
        var extended = SafetyContext.ForCurrentUser([app]);
        foreach (var entry in baseline.NeverTouchFolders)
        {
            Expect.ContainsPath(extended.NeverTouchFolders, entry, "a built-in entry after adding the app's");
        }
        Expect.ContainsPath(extended.NeverTouchFolders, Win32.FinalPath(app), "the app's folder");
        Assert.HasCount(baseline.NeverTouchFolders.Count + 1, extended.NeverTouchFolders);
        Assert.AreEqual(baseline.SystemLocations.Count, extended.SystemLocations.Count);
    }
}
