using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>This user's Recycle Bin settings for one volume (the brief: the standard user may set its own
/// <c>HKCU\...\Explorer\BitBucket\Volume\{GUID}</c> values and its key's ACL). Everything is restored on dispose,
/// including a key that did not exist.</summary>
internal sealed class RecycleBinVolumeSettings : IDisposable
{
    private const string BitBucket = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume";
    public const string MaxCapacityName = "MaxCapacity";
    public const string NukeOnDeleteName = "NukeOnDelete";

    private readonly string keyPath;
    private readonly bool existed;
    private readonly Dictionary<string, (object Value, RegistryValueKind Kind)> original = new(StringComparer.OrdinalIgnoreCase);
    private bool denied;

    public RecycleBinVolumeSettings(string driveRoot)
    {
        DriveRoot = driveRoot;
        keyPath = BitBucket + "\\" + Win32.VolumeGuid(driveRoot);
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        existed = key is not null;
        if (key is not null)
        {
            foreach (var name in key.GetValueNames())
            {
                original[name] = (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(name));
            }
        }
    }

    public string DriveRoot { get; }

    public string KeyPath => @"HKCU\" + keyPath;

    public void SetMaxCapacityMegabytes(int megabytes) => Set(MaxCapacityName, megabytes);

    public void SetNukeOnDelete(bool on) => Set(NukeOnDeleteName, on ? 1 : 0);

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Removes the volume's key altogether (no setting at all); dispose recreates it when it existed.</summary>
    public void DeleteKey()
    {
        AllowRead();
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    public bool KeyExists
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            return key is not null;
        }
    }

    private void Set(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Makes the key unreadable for this user (deny query, enumerate and notify; permissions stay changeable).</summary>
    public void DenyRead()
    {
        Registry.CurrentUser.CreateSubKey(keyPath, writable: true).Dispose();
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadPermissions);
        var security = key!.GetAccessControl();
        security.AddAccessRule(new RegistryAccessRule(Acl.CurrentUser, RegistryRights.QueryValues | RegistryRights.EnumerateSubKeys | RegistryRights.Notify, AccessControlType.Deny));
        key.SetAccessControl(security);
        denied = true;
    }

    public void AllowRead()
    {
        if (!denied)
        {
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadPermissions);
        var security = key!.GetAccessControl();
        security.RemoveAccessRuleAll(new RegistryAccessRule(Acl.CurrentUser, RegistryRights.FullControl, AccessControlType.Deny));
        key.SetAccessControl(security);
        denied = false;
    }

    public void Dispose()
    {
        AllowRead();
        if (!existed)
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        foreach (var name in key.GetValueNames().Where(n => !original.ContainsKey(n)).ToList())
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        foreach (var (name, (value, kind)) in original)
        {
            key.SetValue(name, value, kind);
        }
    }
}

/// <summary>One of the tests' own files moved to the Recycle Bin, to make room matter; removed from the bin on dispose
/// (its <c>$R</c>/<c>$I</c> pair in this user's bin folder, which the shell created for this move and nothing else).</summary>
internal sealed class RecycledFixture : IDisposable
{
    private readonly string binFolder;
    private readonly List<string> added;

    private RecycledFixture(string binFolder, List<string> added)
    {
        this.binFolder = binFolder;
        this.added = added;
    }

    public static string BinFolderFor(string driveRoot) => Path.Combine(driveRoot, "$Recycle.Bin", Acl.CurrentUser.Value);

    /// <summary>Recycles <paramref name="path"/> (a file the test made) and checks the bin grew by about its size.</summary>
    public static RecycledFixture Recycle(string path)
    {
        var driveRoot = Path.GetPathRoot(path)!;
        var folder = BinFolderFor(driveRoot);
        var before = Snapshot(folder);
        var size = new FileInfo(path).Length;
        var bytesBefore = Win32.RecycleBinBytes(driveRoot);
        Win32.RecycleOwnFile(path);
        Assert.IsFalse(File.Exists(path), "the fixture was not moved");
        var bytesAfter = Win32.RecycleBinBytes(driveRoot);
        Assert.IsTrue(bytesAfter >= bytesBefore + size - 4096, $"the fixture did not land in the Recycle Bin: {bytesBefore} -> {bytesAfter}, size {size}");
        var after = Snapshot(folder);
        return new RecycledFixture(folder, after.Except(before, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static HashSet<string> Snapshot(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return new HashSet<string>(Directory.EnumerateFiles(folder).Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        foreach (var name in added)
        {
            var path = Path.Combine(binFolder, name);
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left for the user's "Empty Recycle Bin"; it is the test's own file.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }
}
