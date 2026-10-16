using System;
using System.Diagnostics;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

// Tests change per-user state (Recycle Bin settings, environment variables, the Pictures known folder, DOS devices,
// sync roots): never two at once.
[assembly: DoNotParallelize]

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Before any test: undo what a previous run that was killed may have left behind in this user's profile.
/// The AdminSetup fixtures under %PUBLIC% are the admin scripts' business, not touched here.</summary>
[TestClass]
public sealed class AssemblySweep
{
    private const string SyncRootManager = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

    [AssemblyInitialize]
    public static void Sweep(TestContext _)
    {
        // A leftover the sweep cannot remove (an item a killed fail-closed test left under an OWNER RIGHTS deny, which
        // only an administrator can take back: windows/tests/admin/teardown.ps1 does) must never abort a suite: every
        // step is guarded, and every test uses a fresh folder of its own anyway.
        Guarded("Pictures redirect", RestorePicturesLeftRedirected);
        Guarded("sync roots", UnregisterSyncRootsLeftUnderTheTestFolder);
        Guarded("profile test folder", () => DeleteTreeOrLeave(TempFolder.ProfileRoot));
        Guarded("test data folder", SweepTestDataFolder);
    }

    private static void Guarded(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"sweep: {what} left as it is: {e.Message}");
        }
    }

    /// <summary>Deletes the tree; what the standard user cannot delete (see above) is left for the administrator's script.</summary>
    private static void DeleteTreeOrLeave(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        try
        {
            TempFolder.DeleteTree(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in new[] { "/c", "rmdir", "/s", "/q", @"\\?\" + root })
            {
                info.ArgumentList.Add(a);
            }
            using var process = Process.Start(info)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(60000);
            if (Directory.Exists(root))
            {
                Console.WriteLine($"sweep: {root} still holds items this user cannot delete: {e.Message}");
            }
        }
    }

    /// <summary>The tests' own folders under %AMBYSTO_TESTDATA% (named "hint-xxxxxxxx" by TempFolder), left by a
    /// killed run; the folder itself and anything else in it stay.</summary>
    private static void SweepTestDataFolder()
    {
        var testData = Environment.GetEnvironmentVariable("AMBYSTO_TESTDATA");
        if (string.IsNullOrEmpty(testData) || !Directory.Exists(testData))
        {
            return;
        }
        foreach (var directory in Directory.EnumerateDirectories(testData))
        {
            if (TempFolder.IsTestFolderName(Path.GetFileName(directory)))
            {
                TempFolder.DeleteTree(directory);
            }
        }
    }

    private static bool IsUnderTestFolders(string path)
    {
        var testData = Environment.GetEnvironmentVariable("AMBYSTO_TESTDATA");
        return path.StartsWith(TempFolder.ProfileRoot + "\\", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(testData) && path.StartsWith(testData.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && TempFolder.IsTestFolderName(path[(testData.TrimEnd('\\').Length + 1)..].Split('\\')[0]));
    }

    /// <summary>A Pictures redirect into the tests' folder is a leftover: back to the default location.</summary>
    private static void RestorePicturesLeftRedirected()
    {
        var current = Win32.KnownFolderPath(Win32.PicturesFolderId);
        if (current.StartsWith(TempFolder.ProfileRoot + "\\", StringComparison.OrdinalIgnoreCase))
        {
            Win32.SetKnownFolderPath(Win32.PicturesFolderId, Win32.KnownFolderDefaultPath(Win32.PicturesFolderId));
        }
    }

    /// <summary>Sync roots registered by an earlier run under the tests' folder are unregistered through the fixture.</summary>
    private static void UnregisterSyncRootsLeftUnderTheTestFolder()
    {
        using var manager = Registry.LocalMachine.OpenSubKey(SyncRootManager);
        if (manager is null)
        {
            return;
        }
        foreach (var name in manager.GetSubKeyNames())
        {
            using var roots = manager.OpenSubKey(name + @"\UserSyncRoots");
            if (roots is null)
            {
                continue;
            }
            foreach (var valueName in roots.GetValueNames())
            {
                if (roots.GetValue(valueName) is string path && IsUnderTestFolders(path))
                {
                    Unregister(path);
                }
            }
        }
    }

    private static void Unregister(string root)
    {
        if (!File.Exists(CloudRoot.Exe))
        {
            return;
        }
        var info = new ProcessStartInfo(CloudRoot.Exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "unregister", "--root", root })
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
    }
}
