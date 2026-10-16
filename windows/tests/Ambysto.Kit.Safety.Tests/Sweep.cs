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
        RestorePicturesLeftRedirected();
        UnregisterSyncRootsLeftUnderTheTestFolder();
        TempFolder.DeleteTree(TempFolder.ProfileRoot);
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
                if (roots.GetValue(valueName) is string path && path.StartsWith(TempFolder.ProfileRoot + "\\", StringComparison.OrdinalIgnoreCase))
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
