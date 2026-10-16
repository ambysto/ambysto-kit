using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static Ambysto.Kit.Safety.Native.CloudMethods;
using static Ambysto.Kit.Safety.Native.NativeMethods;


namespace Ambysto.Kit.Safety.Internal;

/// <summary>Windows step 9 (docs/spec/safety.md, "Windows: recoverable moves"): cloud placeholders that are not fully on
/// this PC, then cloud-sync roots. Nothing here reads data or starts a download.</summary>
internal static class Cloud
{
    private static readonly string[] ProviderFolders =
        ["Dropbox", "Google Drive", "My Drive", "OneDrive", "Box", "Box Sync", "pCloud Drive", "iCloudDrive", "iCloud Drive"];

    /// <summary>True when a file is a placeholder with fewer bytes on disk than its length (partly or not downloaded).</summary>
    internal static unsafe bool NotFullyLocal(string file, long length)
    {
        var (probe, error) = FileProbe.Open(file, readControl: false);
        if (probe is null)
        {
            throw new System.ComponentModel.Win32Exception(error, "open " + file);
        }
        using (probe)
        {
            // The fixed part is all this reads; a long file identity only makes the call report "more data", which still
            // fills the fixed part (ERROR_MORE_DATA is therefore accepted).
            var buffer = stackalloc byte[4096];
            new Span<byte>(buffer, 4096).Clear(); // explicit: nothing read below may come from an earlier use of the stack
            var hr = CfGetPlaceholderInfo(probe.Handle, CF_PLACEHOLDER_INFO_STANDARD, buffer, 4096, out var returned);
            if (hr == HRESULT_MORE_DATA)
            {
                hr = 0;
            }
            if (hr == HRESULT_NOT_A_CLOUD_FILE || hr == HRESULT_INVALID_FUNCTION)
            {
                return false;
            }
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
            if (returned < sizeof(long))
            {
                throw new InvalidOperationException("placeholder info shorter than its on-disk size field"); // fails closed
            }
            var info = (CF_PLACEHOLDER_STANDARD_INFO*)buffer;
            return info->OnDiskDataSize < length;
        }
    }

    /// <summary>This user's cloud-sync roots: the registered sync roots, the OneDrive environment folders, and the provider
    /// folders directly in the profile (whether or not they exist).</summary>
    internal static List<string> SyncRoots(string profile)
    {
        var roots = new List<string>();
        var sid = Owners.CurrentUserSid;
        using (var manager = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager", writable: false))
        {
            if (manager is not null)
            {
                foreach (var name in manager.GetSubKeyNames())
                {
                    using var user = manager.OpenSubKey(name + @"\UserSyncRoots", writable: false);
                    if (user?.GetValue(sid) is string path && path.Length > 0)
                    {
                        roots.Add(path);
                    }
                }
            }
        }
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
            {
                roots.Add(value);
            }
        }
        foreach (var folder in ProviderFolders)
        {
            roots.Add(Path.Combine(profile, folder));
        }
        if (Directory.Exists(profile))
        {
            foreach (var dir in Directory.EnumerateDirectories(profile, "OneDrive - *"))
            {
                roots.Add(dir);
            }
        }
        var canonical = new List<string>();
        foreach (var root in roots)
        {
            string resolved;
            try
            {
                resolved = Directory.Exists(root) ? Canonical.OfExisting(root) : root.TrimEnd('\\');
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                resolved = root.TrimEnd('\\'); // still compared by its spelling
            }
            if (!canonical.Exists(c => Canonical.SamePath(c, resolved)))
            {
                canonical.Add(resolved);
            }
        }
        return canonical;
    }

    /// <summary>True when the system says <paramref name="path"/> (an existing item) is a sync root or inside one. This finds
    /// every Cloud Files provider, including registrations no list shows (measured: a classic registration leaves no
    /// SyncRootManager entry). It reads registration facts only.</summary>
    internal static unsafe bool UnderSyncRoot(string path)
    {
        var buffer = stackalloc byte[8192];
        var hr = CfGetSyncRootInfoByPath(PathSpelling.Extended(path), CF_SYNC_ROOT_INFO_BASIC, buffer, 8192, out _);
        if (hr >= 0)
        {
            return true;
        }
        if (hr == HRESULT_NOT_UNDER_SYNC_ROOT || hr == HRESULT_INVALID_FUNCTION)
        {
            return false;
        }
        Marshal.ThrowExceptionForHR(hr);
        return false;
    }

    /// <summary>True when <paramref name="item"/> is inside, equal to, or contains <paramref name="root"/>.</summary>
    internal static bool Overlaps(string item, string root) =>
        Canonical.SamePath(item, root) || IsInside(item, root) || IsInside(root, item);

    internal static bool IsInside(string path, string folder) =>
        path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}
