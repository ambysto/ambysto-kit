using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Safety;

/// <summary>The result of <see cref="FileFacts.Read(string)"/>.</summary>
public sealed class FileFactsResult
{
    internal FileFactsResult(string? canonicalPath, FileStamp? stamp, bool isCloudNotOpened, RefusalReason? failureReason, string? detail)
    {
        CanonicalPath = canonicalPath; Stamp = stamp; IsCloudNotOpened = isCloudNotOpened; FailureReason = failureReason; Detail = detail;
    }

    /// <summary>True when the path and stamp were read.</summary>
    public bool Succeeded => Stamp is not null;
    /// <summary>The canonical path, when read.</summary>
    public string? CanonicalPath { get; }
    /// <summary>The stamp, when read.</summary>
    public FileStamp? Stamp { get; }
    /// <summary>True when the item is marked "recall on open" and was therefore not opened (no stamp).</summary>
    public bool IsCloudNotOpened { get; }
    /// <summary>Why nothing was read (for example an unsupported path form or a gone item).</summary>
    public RefusalReason? FailureReason { get; }
    /// <summary>Optional detail.</summary>
    public string? Detail { get; }
}

/// <summary>Reads an item's canonical path and stamp without reading data or starting a cloud download. It decides nothing.</summary>
public static class FileFacts
{
    /// <summary>Reads the canonical path and stamp of a file or folder.</summary>
    /// <param name="path">A drive-letter path (also <c>\\?\</c> drive paths and mapped drives); network and device spellings are refused.</param>
    /// <remarks>Refused, with the gate's reason: network and device spellings (<see cref="RefusalReason.UnsupportedPathForm"/>),
    /// a link, junction, mount point or other non-cloud reparse point (<see cref="RefusalReason.LinkOrSpecialFile"/>, no stamp is read through it),
    /// FAT and exFAT volumes (<see cref="RefusalReason.UnsupportedVolume"/>: no stable identity), a gone item (<see cref="RefusalReason.AlreadyGone"/>).</remarks>
    /// <returns>The facts, or why none were read.</returns>
    public static FileFactsResult Read(string path) => throw new NotImplementedException();
}
