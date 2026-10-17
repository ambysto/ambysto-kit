using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Ambysto.Kit.Safety.Native;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Windows step 11 (docs/spec/safety.md, "Never touch" and system locations): by path, by name, and by identity
/// of the item and every folder above it; then the owners of the item and everything in it.</summary>
internal static class Protection
{
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Names protected wherever they are (registry hives, password vaults, mail stores, image backups).</summary>
    internal static bool ProtectedName(string name, bool isDirectory)
    {
        if (isDirectory)
        {
            return string.Equals(name, "WindowsImageBackup", StringComparison.OrdinalIgnoreCase);
        }
        return name.StartsWith("NTUSER.DAT", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("UsrClass.dat", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pst", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Names protected directly at every volume root.</summary>
    internal static readonly string[] VolumeRootNames =
        ["$Recycle.Bin", "System Volume Information", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "Recovery", "Windows.old", "$WINDOWS.~BT", "FileHistory"];

    internal static bool IsVolumeRootName(string path, string volumeRoot)
    {
        if (!Cloud.IsInside(path, volumeRoot))
        {
            return false;
        }
        var first = path[volumeRoot.TrimEnd('\\').Length..].TrimStart('\\').Split('\\')[0];
        return Array.Exists(VolumeRootNames, n => string.Equals(n, first, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when <paramref name="path"/> is an entry, inside one, or (for the item itself) contains one.</summary>
    internal static bool Hits(string path, IReadOnlyList<string> entries, bool alsoContains)
    {
        foreach (var entry in entries)
        {
            if (Canonical.SamePath(path, entry) || Cloud.IsInside(path, entry) || (alsoContains && Cloud.IsInside(entry, path)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when the system's file protection covers <paramref name="file"/>; throws when it cannot tell.</summary>
    internal static bool SystemProtected(string file)
    {
        var covered = SfcMethods.SfcIsFileProtected(IntPtr.Zero, file);
        if (Faults.Ok(Faults.Query.ResourceProtection, Faults.Yes(Faults.Query.ResourceProtection, covered, file), file))
        {
            return true;
        }
        // Measured (2026-10-16, Windows 11 26100): a protected file returns TRUE; a file Windows Resource Protection does
        // not cover (or a missing one) returns FALSE with ERROR_ACCESS_DENIED, and a path of about 250 characters or more
        // returns FALSE with ERROR_INSUFFICIENT_BUFFER. The files it covers all lie in system locations, which step 11
        // refuses by path and identity before this backstop runs. Any other error fails closed.
        var error = Marshal.GetLastPInvokeError();
        if (error is 0 or NativeMethods.ERROR_FILE_NOT_FOUND or ERROR_ACCESS_DENIED or ERROR_INSUFFICIENT_BUFFER)
        {
            return false;
        }
        throw new System.ComponentModel.Win32Exception(error, "Windows Resource Protection");
    }

    internal static bool OwnedByWindows(string sid) =>
        string.Equals(sid, Owners.SystemSid, StringComparison.Ordinal) || string.Equals(sid, Owners.TrustedInstallerSid, StringComparison.Ordinal);

    /// <summary>Resolves a list of entries: existing ones to their canonical path; missing ones by their spelling and also
    /// by the canonical path of their deepest existing folder (so an entry given through a subst letter or a mapped drive
    /// still matches by path once it is created; it has no identity until then).</summary>
    internal static List<string> Resolve(IEnumerable<string> paths)
    {
        var resolved = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }
            foreach (var entry in Spellings(path))
            {
                if (!resolved.Exists(r => Canonical.SamePath(r, entry)))
                {
                    resolved.Add(entry);
                }
            }
        }
        return resolved;
    }

    /// <summary>The spellings an entry is compared by: the canonical path when it exists; otherwise the given spelling and
    /// the canonical spelling of its existing part, when that differs.</summary>
    internal static List<string> Spellings(string path)
    {
        var spellings = new List<string>(2);
        try
        {
            if (Path.Exists(path))
            {
                spellings.Add(Canonical.OfExisting(path));
                return spellings;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // an existing entry that cannot be resolved is still compared by the spellings below
        }
        spellings.Add(Path.GetFullPath(path).TrimEnd('\\'));
        if (Canonical.OfMaybeMissing(path) is { } canonical && !Canonical.SamePath(canonical, spellings[0]))
        {
            spellings.Add(canonical);
        }
        return spellings;
    }

    /// <summary>Identities of the entries that exist and can be opened (others are compared by path only).</summary>
    internal static HashSet<FileIdentity> Identities(IEnumerable<string> entries)
    {
        var set = new HashSet<FileIdentity>();
        foreach (var entry in entries)
        {
            var plain = PathSpelling.Plain(entry);
            if (plain is null)
            {
                continue;
            }
            var (probe, _) = FileProbe.Open(plain, readControl: false);
            if (probe is null)
            {
                continue;
            }
            using (probe)
            {
                try { set.Add(probe.Identity()); }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return set;
    }
}
