using System;
using System.ComponentModel;
using Microsoft.Win32;
using static Ambysto.Kit.Safety.Native.WalkMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>This user's Recycle Bin on one volume (docs/spec/safety.md, "Windows: recoverable moves"). Measured: sizes are
/// logical lengths, the size setting is in MiB, the bin is the user's own, and an absent size setting is written by the
/// shell on its first query of the volume.</summary>
internal static class RecycleBin
{
    private const string VolumesKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\";
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    internal sealed record Settings(bool Off, long QuotaBytes, long UsedBytes);

    internal static unsafe string VolumeGuid(string volumeRoot)
    {
        var buffer = stackalloc char[64];
        if (!GetVolumeNameForVolumeMountPoint(volumeRoot, buffer, 64))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "volume name");
        }
        var name = new string(buffer); // \\?\Volume{GUID}\
        var open = name.IndexOf('{', StringComparison.Ordinal);
        var close = name.IndexOf('}', StringComparison.Ordinal);
        if (open < 0 || close < open)
        {
            throw new InvalidOperationException("unexpected volume name " + name);
        }
        return name[open..(close + 1)];
    }

    internal static (long Bytes, long Items) Used(string volumeRoot)
    {
        var info = new SHQUERYRBINFO { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(volumeRoot, ref info);
        if (hr < 0)
        {
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(hr);
        }
        return (info.TotalBytes, info.ItemCount);
    }

    /// <summary>Reads this user's settings for the volume's bin; throws when one cannot be read (failing closed).</summary>
    internal static Settings Read(string volumeRoot)
    {
        var guid = VolumeGuid(volumeRoot);
        // The shell's own query comes first: it writes the default size setting when there is none (measured).
        var (used, _) = Used(volumeRoot);
        using var key = Registry.CurrentUser.OpenSubKey(VolumesKey + guid, writable: false);
        if (key is null)
        {
            throw new InvalidOperationException("no Recycle Bin settings for " + volumeRoot);
        }
        var nuke = key.GetValue("NukeOnDelete");
        var capacity = key.GetValue("MaxCapacity") ?? throw new InvalidOperationException("no Recycle Bin size for " + volumeRoot);
        var off = nuke is int n && n != 0;
        if (nuke is not null and not int)
        {
            throw new InvalidOperationException("unexpected NukeOnDelete value");
        }
        if (capacity is not int megabytes || megabytes < 0)
        {
            throw new InvalidOperationException("unexpected MaxCapacity value");
        }
        return new Settings(off, (long)megabytes * 1024 * 1024, used);
    }

    /// <summary>The "do not move files to the Recycle Bin" policy, machine or user; null when it cannot be read.</summary>
    internal static bool? NoRecycleFilesPolicy()
    {
        try
        {
            return IsSet(Registry.LocalMachine) || IsSet(Registry.CurrentUser);
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.IO.IOException) { return null; }

        static bool IsSet(RegistryKey hive)
        {
            using var key = hive.OpenSubKey(PolicyKey, writable: false);
            var value = key?.GetValue("NoRecycleFiles");
            return value is int v && v != 0;
        }
    }

    /// <summary>A Recycle Bin size policy (percent of the drive) overrides the user's setting with semantics this code does
    /// not model, so its presence fails closed.</summary>
    internal static bool SizePolicyPresent()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(PolicyKey, writable: false);
            if (key?.GetValue("RecycleBinSize") is not null)
            {
                return true;
            }
        }
        return false;
    }
}
