using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>Trees whose canonical paths have an exact length, for the 260-character rule.</summary>
internal static class LongPaths
{
    private const int ComponentLength = 200;

    /// <summary>Creates a file under <paramref name="canonicalRoot"/> whose canonical plain path (no prefix) has
    /// exactly <paramref name="totalLength"/> characters, and returns that path.</summary>
    public static string CreateFile(string canonicalRoot, int totalLength)
    {
        var path = canonicalRoot.TrimEnd('\\');
        var remaining = totalLength - path.Length;
        Assert.IsTrue(remaining >= 2, $"root {path} is too long for a {totalLength}-character path");
        while (remaining > ComponentLength + 1)
        {
            path += "\\" + new string('d', ComponentLength);
            Directory.CreateDirectory(path);
            remaining -= ComponentLength + 1;
        }
        path += "\\" + new string('f', remaining - 1);
        File.WriteAllBytes(path, new byte[64]);
        Assert.AreEqual(totalLength, path.Length);
        return path;
    }

    /// <summary>Under a short item, a chain of directories until one directory's own path is 260 characters or
    /// longer, and a file inside it (about 300 characters): what a walk without extended-length names never sees.
    /// Everything is created through the <c>\\?\</c> prefix. Returns the deep file's plain path.</summary>
    public static string CreateDeepFile(string canonicalItem)
    {
        var path = canonicalItem.TrimEnd('\\');
        while (path.Length < 262)
        {
            path += "\\" + new string('d', 60);
            Directory.CreateDirectory(@"\\?\" + path);
        }
        Assert.IsTrue(path.Length >= 260, "the deep directory's own path is not 260 characters or longer");
        var file = path + "\\" + new string('f', 300 - path.Length - 1 > 1 ? 300 - path.Length - 1 : 8);
        File.WriteAllBytes(@"\\?\" + file, new byte[64]);
        Assert.IsTrue(File.Exists(@"\\?\" + file), "the deep file was not created");
        return file;
    }

    /// <summary>The 8.3 spelling of a long path, without the extended prefix the query needs.</summary>
    public static string ShortSpelling(string path)
    {
        var shortPath = Win32.ShortPath(path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path);
        return shortPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? shortPath[4..] : shortPath;
    }
}
