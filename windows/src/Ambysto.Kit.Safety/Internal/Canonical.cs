using System;

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

    internal static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
