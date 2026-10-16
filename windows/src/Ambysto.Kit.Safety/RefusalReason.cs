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
    /// <summary>A folder that holds a volume mount point (the Recycle Bin would count the mounted volume) or another special file of unknown recycle behaviour; junctions, symbolic links, cloud placeholders and WOF files inside a folder are not this reason.</summary>
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
    /// <summary>Putting back: something already exists at the original location.</summary>
    RestoreDestinationExists,
    /// <summary>Putting back: the original folder no longer exists or moved.</summary>
    RestoreFolderMissing,
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
    public static string Message(this RefusalReason reason) => reason switch
    {
        RefusalReason.NotInteractive => "Moving is off outside your desktop session.",
        RefusalReason.Elevated => "Moving is off while running as administrator.",
        RefusalReason.AlreadyGone => "Already gone",
        RefusalReason.LinkOrSpecialFile => "A link or special file: never followed.",
        RefusalReason.ContainsLinkOrSpecialFile => "Holds a mounted drive or a special file the Recycle Bin may not keep.",
        RefusalReason.UnsupportedPathForm => "Network, device and stream paths are not supported.",
        RefusalReason.UnsupportedVolume => "Only fixed NTFS or ReFS drives are supported.",
        RefusalReason.ChangedSinceScan => "Changed since the scan",
        RefusalReason.RecycleBinOff => "The Recycle Bin is off for this drive.",
        RefusalReason.RecycleBinFull => "The Recycle Bin is full: make room in it or raise its size, then try again.",
        RefusalReason.TooLargeForRecycleBin => "Too large for the Recycle Bin on this drive.",
        RefusalReason.PathTooLong => "A path inside is 260 characters or longer; the Recycle Bin would not keep it.",
        RefusalReason.CloudFile => "Not fully on this PC: a cloud file that is online only or partly downloaded, or a folder holding one.",
        RefusalReason.CloudSyncFolder => "Inside a cloud-synced folder: deleting here deletes on every device.",
        RefusalReason.RootOrProfile => "A drive, a profile or the profiles folder itself is never moved.",
        RefusalReason.Protected => "Protected location",
        RefusalReason.SystemOwned => "Owned by Windows",
        RefusalReason.RestoreDestinationExists => "Something already exists at the original location",
        RefusalReason.RestoreFolderMissing => "The original folder no longer exists or moved",
        RefusalReason.NotAllowed => "Not allowed by this app",
        RefusalReason.CouldNotVerify => "Could not verify: ",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
