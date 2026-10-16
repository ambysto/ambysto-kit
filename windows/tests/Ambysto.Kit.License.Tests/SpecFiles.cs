using System;
using System.IO;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>The spec files, read in place from the repository (never copied, so they cannot drift).</summary>
internal static class SpecFiles
{
    private const string Marker = "docs/spec/license-test-vectors.json";

    /// <summary>The repository root: the first folder above the test binary that holds the vectors file.</summary>
    public static string RepoRoot { get; } = FindRoot();

    public static string Path(string relative) => System.IO.Path.Combine(RepoRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, Marker.Replace('/', System.IO.Path.DirectorySeparatorChar))))
            {
                return dir.FullName;
            }
        }
        // A missing file fails the run; it never skips the tests that need it.
        throw new FileNotFoundException($"{Marker} not found above {AppContext.BaseDirectory}");
    }
}
