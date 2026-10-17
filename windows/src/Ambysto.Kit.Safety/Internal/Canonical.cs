using System;
using System.Collections.Generic;
using System.IO;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Canonical paths from an open handle (8.3 expanded, true case, subst and mapped drives resolved).</summary>
internal static class Canonical
{
    /// <summary>The canonical plain path of an existing item; throws when it cannot be resolved or is not a drive path.</summary>
    internal static string OfExisting(string path)
    {
        var plain = PathSpelling.Plain(path) ?? throw new ArgumentException("unsupported spelling: " + path, nameof(path));
        var (probe, error) = FileProbe.Open(plain, readControl: false);
        if (probe is null)
        {
            throw new System.ComponentModel.Win32Exception(error, "open " + path);
        }
        using (probe)
        {
            return PathSpelling.Plain(probe.FinalPath()) ?? throw new ArgumentException("resolves to an unsupported spelling: " + path, nameof(path));
        }
    }

    /// <summary>The canonical plain path of a path that may not exist yet: its deepest existing folder resolved through a
    /// handle (so a subst letter, a mapped drive, an 8.3 name or a junction above that folder is resolved; when the deepest
    /// existing component is itself a junction it is opened as the link and kept as spelled), the missing rest appended as
    /// given. Null when nothing above it exists or the existing part cannot be resolved.</summary>
    internal static string? OfMaybeMissing(string path)
    {
        try
        {
            var rest = new List<string>();
            var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            while (!Path.Exists(current))
            {
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }
                rest.Insert(0, Path.GetFileName(current));
                current = parent;
            }
            var resolved = OfExisting(current).TrimEnd('\\');
            return rest.Count == 0 ? resolved : resolved + "\\" + string.Join('\\', rest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
