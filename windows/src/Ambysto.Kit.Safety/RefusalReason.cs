using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Safety;

/// <summary>Why the gate refused an item (docs/spec/safety.md, "Windows: refusal reasons").</summary>
public enum RefusalReason
{
    /// <summary>Not an interactive desktop session.</summary>
    NotInteractive,
    /// <summary>Running elevated (administrator) or as SYSTEM.</summary>
    Elevated,
    /// <summary>The item no longer exists.</summary>
    AlreadyGone,
    /// <summary>A link, junction, mount point or other special file.</summary>
    LinkOrSpecialFile,
    /// <summary>A folder that holds a link, junction, mount point or compressed (WOF) file.</summary>
    ContainsLinkOrSpecialFile,
    /// <summary>A network, device or stream path, or a name ending in a dot or a space.</summary>
    UnsupportedPathForm,
    /// <summary>Not on a fixed local NTFS or ReFS volume (removable, network, FAT, exFAT).</summary>
    UnsupportedVolume,
    /// <summary>Identity, last-write time or length differ from what was shown.</summary>
    ChangedSinceScan,
    /// <summary>The volume's Recycle Bin is off for this user or disallowed by policy.</summary>
    RecycleBinOff,
    /// <summary>The item fits the Recycle Bin's size but not the room left in it: moving it would purge older items.</summary>
    RecycleBinFull,
    /// <summary>The item is larger than the Recycle Bin's whole size on its volume.</summary>
    TooLargeForRecycleBin,
    /// <summary>A path in the item's tree is 260 characters or longer.</summary>
    PathTooLong,
    /// <summary>An online-only or partially downloaded cloud file, or a folder holding one.</summary>
    CloudFile,
    /// <summary>Inside, or containing, a cloud-sync root.</summary>
    CloudSyncFolder,
    /// <summary>A volume root, a profile folder or the profiles folder itself.</summary>
    RootOrProfile,
    /// <summary>Inside, or containing, a never-touch entry or a system location.</summary>
    Protected,
    /// <summary>Owned by TrustedInstaller or SYSTEM, or protected by Windows Resource Protection.</summary>
    SystemOwned,
    /// <summary>The app's allowed-root rule refused it.</summary>
    NotAllowed,
    /// <summary>A check could not establish its fact (failing closed).</summary>
    CouldNotVerify,
}

/// <summary>The spec's message for each <see cref="RefusalReason"/>.</summary>
public static class RefusalReasonExtensions
{
    /// <summary>The message from the spec's table. <see cref="RefusalReason.NotAllowed"/> carries the app's own text and
    /// <see cref="RefusalReason.CouldNotVerify"/> names its check, so <see cref="GateResult.Message"/> is what a user sees.</summary>
    /// <param name="reason">The refusal reason.</param>
    /// <returns>The fixed message, or the fixed part of it.</returns>
    public static string Message(this RefusalReason reason) => throw new NotImplementedException();
}
