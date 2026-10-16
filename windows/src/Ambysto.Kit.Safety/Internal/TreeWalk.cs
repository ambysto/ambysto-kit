using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Ambysto.Kit.Safety.Native;
using static Ambysto.Kit.Safety.Native.NativeMethods;
using static Ambysto.Kit.Safety.Native.WalkMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>What Windows step 7's walk saw. The walk enters no reparse point and opens no file for data.</summary>
internal sealed class TreeFacts
{
    internal long LogicalBytes { get; set; }
    internal long AllocatedBytes { get; set; }
    internal int FileCount { get; set; }
    internal int LongestPath { get; set; }
    internal bool HoldsMountPoint { get; set; }
    internal bool HoldsUnknownSpecial { get; set; }
    internal bool HoldsCloudNotFullyLocal { get; set; }
    internal bool HoldsCloudFolder { get; set; }
    internal List<string> Files { get; } = [];
    internal Dictionary<string, long> Lengths { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<string> Folders { get; } = [];
}

internal static class TreeWalk
{
    private const uint TagMountPoint = 0xA0000003;
    private const uint TagSymlink = 0xA000000C;

    /// <summary>Measures an item: a file, or a folder and everything below it that is not behind a reparse point.</summary>
    internal static TreeFacts Measure(string canonical, uint itemAttributes)
    {
        var facts = new TreeFacts { LongestPath = canonical.Length };
        if ((itemAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            AddFile(facts, canonical, itemAttributes, SizeOf(canonical));
            return facts;
        }
        facts.Folders.Add(canonical);
        var pending = new Stack<string>();
        pending.Push(canonical);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            foreach (var (name, attributes, tag, length) in List(folder))
            {
                var path = folder + "\\" + name;
                facts.LongestPath = Math.Max(facts.LongestPath, path.Length);
                var isDirectory = (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                var cloudAttributes = (attributes & (FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)) != 0;
                if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                {
                    if (FileProbe.IsCloudTag(tag))
                    {
                        if (isDirectory) { facts.HoldsCloudFolder = true; } else { AddFile(facts, path, attributes, length); }
                        continue; // a cloud folder is never entered (spec, step 7)
                    }
                    if (tag == TagMountPoint && isDirectory && IsVolumeMountPoint(path))
                    {
                        facts.HoldsMountPoint = true;
                        continue;
                    }
                    if (tag == TagMountPoint || tag == TagSymlink)
                    {
                        continue; // a junction or symbolic link inside: not entered, recycled as a link (measured)
                    }
                    if (tag == FileProbe.TagWof && !isDirectory)
                    {
                        AddFile(facts, path, attributes, length);
                        continue;
                    }
                    facts.HoldsUnknownSpecial = true;
                    continue;
                }
                if (isDirectory)
                {
                    if (cloudAttributes) { facts.HoldsCloudFolder = true; continue; }
                    facts.Folders.Add(path);
                    pending.Push(path);
                }
                else
                {
                    AddFile(facts, path, attributes, length);
                }
            }
        }
        return facts;
    }

    private static void AddFile(TreeFacts facts, string path, uint attributes, long length)
    {
        facts.FileCount++;
        facts.Files.Add(path);
        facts.Lengths[path] = length;
        facts.LogicalBytes += length;
        if ((attributes & (FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)) != 0)
        {
            facts.HoldsCloudNotFullyLocal = true; // refused at step 9; never opened, not even for its allocated size
            return;
        }
        facts.AllocatedBytes += Allocated(path);
    }

    private static long SizeOf(string path)
    {
        if (!GetFileAttributesEx(PathSpelling.Extended(path), 0, out var data))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "size of " + path);
        }
        return ((long)data.FileSizeHigh << 32) | data.FileSizeLow;
    }

    private static long Allocated(string path)
    {
        var low = GetCompressedFileSize(PathSpelling.Extended(path), out var high);
        if (low == INVALID_FILE_SIZE)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 0)
            {
                throw new Win32Exception(error, "allocated size of " + path);
            }
        }
        return ((long)high << 32) | low;
    }

    /// <summary>True for a volume mount point, false for a junction to a folder. Measured (2026-10-16): a junction answers
    /// ERROR_INVALID_PARAMETER and a plain folder ERROR_NOT_A_REPARSE_POINT; any other failure (an offline or unreadable
    /// volume) counts as a mount point, so the folder is refused (failing closed).</summary>
    private static unsafe bool IsVolumeMountPoint(string path)
    {
        var buffer = stackalloc char[64];
        if (GetVolumeNameForVolumeMountPoint(PathSpelling.Extended(path) + "\\", buffer, 64))
        {
            return true;
        }
        var error = Marshal.GetLastPInvokeError();
        return error is not (ERROR_INVALID_PARAMETER or ERROR_NOT_A_REPARSE_POINT);
    }

    private const int ERROR_INVALID_PARAMETER = 87;
    private const int ERROR_NOT_A_REPARSE_POINT = 4390;

    /// <summary>One folder's entries; a folder that cannot be listed throws (failing closed).</summary>
    private static unsafe List<(string Name, uint Attributes, uint Tag, long Length)> List(string folder)
    {
        var entries = new List<(string, uint, uint, long)>();
        var handle = FindFirstFileEx(PathSpelling.Extended(folder) + "\\*", FindExInfoBasic, out var data, FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
        if (handle == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "list " + folder);
        }
        try
        {
            while (true)
            {
                var name = new string(data.FileName);
                if (name is not "." and not "..")
                {
                    var tag = (data.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ? data.Reserved0 : 0u;
                    entries.Add((name, data.FileAttributes, tag, ((long)data.FileSizeHigh << 32) | data.FileSizeLow));
                }
                if (!FindNextFile(handle, out data))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != ERROR_NO_MORE_FILES)
                    {
                        throw new Win32Exception(error, "list " + folder);
                    }
                    break;
                }
            }
        }
        finally
        {
            FindClose(handle);
        }
        return entries;
    }
}
