using System;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Windows step 4's spelling rules (docs/spec/safety.md): only drive-letter paths, with or without the
/// <c>\\?\</c> prefix; no network, device or stream spelling; no component ending in a dot or a space.</summary>
internal static class PathSpelling
{
    /// <summary>Returns the plain drive-letter form (no <c>\\?\</c> prefix) when <paramref name="path"/> is a supported
    /// spelling, or null when it is not.</summary>
    internal static string? Plain(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        var p = path;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            p = p[4..];
        }
        else if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
        {
            return null; // UNC, \\.\ device paths
        }
        // X:\... only (a drive letter, a colon, a backslash); this also refuses UNC\, GLOBALROOT, Volume{...} after \\?\.
        if (p.Length < 3 || !char.IsAsciiLetter(p[0]) || p[1] != ':' || p[2] != '\\')
        {
            return null;
        }
        if (p.IndexOf(':', 2) >= 0 || p.Contains('/', StringComparison.Ordinal))
        {
            return null; // an alternate data stream, or a mixed separator a \\?\ path would not normalise
        }
        foreach (var c in p)
        {
            if (c < 32 || c is '<' or '>' or '"' or '|' or '?' or '*')
            {
                return null;
            }
        }
        var components = p[3..].Split('\\');
        for (var i = 0; i < components.Length; i++)
        {
            var part = components[i];
            if (part.Length == 0)
            {
                if (i == components.Length - 1)
                {
                    continue; // one trailing separator
                }
                return null; // an empty component (\\ inside the path)
            }
            if (part is "." or ".." || part.EndsWith('.') || part.EndsWith(' '))
            {
                return null;
            }
        }
        var plain = char.ToUpperInvariant(p[0]) + p[1..];
        return plain.Length > 3 ? plain.TrimEnd('\\') : plain;
    }

    /// <summary>The extended-length spelling of a plain drive-letter path, so long paths reach the file system.</summary>
    internal static string Extended(string plain) => @"\\?\" + plain;
}
