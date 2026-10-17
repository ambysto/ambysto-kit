using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Ambysto.Kit.Safety.Native.NativeMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>One run of the gate, in the Windows order of docs/spec/safety.md. Every refusal ends the run; every error is
/// "Could not verify" (failing closed).</summary>
internal static class GateRun
{
    internal static GateResult Check(SafetyContext context, IAllowedRootRule rule, IEnvironmentFacts environment,
        IRecycleBinPolicy policy, GateRequest request)
    {
        Refusal.Verify("native layout", () => { Native.LayoutCheck.Verify(); return 0; });
        // 1. Environment.
        CheckEnvironment(environment);

        // 4 (on the given path, before step 2). Spelling.
        var plain = PathSpelling.Plain(request.Path) ?? throw new Refusal(RefusalReason.UnsupportedPathForm);

        // 2. Exists, without opening first; recall-on-open goes straight to the cloud refusal.
        var (attributes, error) = FileProbe.Attributes(plain);
        if (error != 0)
        {
            throw FileProbe.IsNotFound(error) ? new Refusal(RefusalReason.AlreadyGone) : new Refusal(RefusalReason.CouldNotVerify, "exists");
        }
        if ((attributes & FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0)
        {
            throw new Refusal(RefusalReason.CloudFile);
        }
        var (probe, openError) = FileProbe.Open(plain, readControl: false);
        if (probe is null)
        {
            if (FileProbe.IsNotFound(openError))
            {
                throw new Refusal(RefusalReason.AlreadyGone);
            }
            // Step 10 is a check on the path alone: a profile the user may not open (another user's, Public) is still
            // refused for what it is when its spelling already names it; anything else that cannot be opened fails closed.
            CheckRootOrProfileByName(context, plain);
            throw new Refusal(RefusalReason.CouldNotVerify, "open");
        }
        using (probe)
        {
            // 3. Not a link or special file.
            // Only an item whose attributes say "reparse point" has a tag to read (FAT and exFAT cannot answer the query).
            var (tagAttributes, tag) = (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0
                ? Refusal.Verify("reparse point", probe.AttributeTag)
                : (attributes, 0u);
            if (!FileProbe.TagAllowed(tagAttributes, tag))
            {
                throw new Refusal(RefusalReason.LinkOrSpecialFile);
            }

            // 4. Spelling of the canonical path.
            var final = Refusal.Verify("canonical path", probe.FinalPath);
            var canonical = PathSpelling.Plain(final) ?? throw new Refusal(RefusalReason.UnsupportedPathForm);

            // 5. Volume: a fixed local NTFS or ReFS volume.
            var fileSystem = Refusal.Verify("file system", probe.FileSystemName);
            if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase) && !string.Equals(fileSystem, "ReFS", StringComparison.OrdinalIgnoreCase))
            {
                throw new Refusal(RefusalReason.UnsupportedVolume);
            }
            var volumeRoot = Refusal.Verify("volume", () => FileProbe.VolumeRoot(canonical));
            if (GetDriveType(volumeRoot) != DRIVE_FIXED)
            {
                throw new Refusal(RefusalReason.UnsupportedVolume);
            }

            // 10 (a check on the canonical path alone, run right after step 5).
            CheckRootOrProfile(context, canonical, volumeRoot);

            // 6. Unchanged since shown.
            var stamp = Refusal.Verify("identity", probe.Stamp);
            if (stamp != request.ShownStamp)
            {
                throw new Refusal(RefusalReason.ChangedSinceScan);
            }

            // 7. Measure now.
            var tree = Refusal.Verify("measure", () => TreeWalk.Measure(canonical, tagAttributes));

            // 8. Recoverable: in the spec's order within step 8.
            var noRecycle = Refusal.Verify("Recycle Bin policy", () => policy.NoRecycleFiles ?? throw new InvalidOperationException("policy unreadable"));
            var bin = Refusal.Verify("Recycle Bin settings", () => RecycleBin.Read(volumeRoot));
            if (Refusal.Verify("Recycle Bin size policy", RecycleBin.SizePolicyPresent))
            {
                throw new Refusal(RefusalReason.CouldNotVerify, "Recycle Bin size policy");
            }
            if (noRecycle || bin.Off)
            {
                throw new Refusal(RefusalReason.RecycleBinOff);
            }
            if (tree.HoldsMountPoint || tree.HoldsUnknownSpecial)
            {
                throw new Refusal(RefusalReason.ContainsLinkOrSpecialFile);
            }
            if (tree.LongestPath >= 260)
            {
                throw new Refusal(RefusalReason.PathTooLong);
            }
            if (tree.LogicalBytes > bin.QuotaBytes)
            {
                throw new Refusal(RefusalReason.TooLargeForRecycleBin);
            }
            if (tree.LogicalBytes > bin.QuotaBytes - bin.UsedBytes)
            {
                throw new Refusal(RefusalReason.RecycleBinFull);
            }

            // 9. Cloud: placeholders not fully on this PC first, then cloud-sync roots.
            if ((attributes & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 || tree.HoldsCloudNotFullyLocal || tree.HoldsCloudFolder)
            {
                throw new Refusal(RefusalReason.CloudFile);
            }
            foreach (var file in tree.Files)
            {
                var length = tree.Lengths[file];
                if (Refusal.Verify("cloud", () => Cloud.NotFullyLocal(file, length)))
                {
                    throw new Refusal(RefusalReason.CloudFile);
                }
            }
            foreach (var root in context.SyncRoots)
            {
                if (Cloud.Overlaps(canonical, root))
                {
                    throw new Refusal(RefusalReason.CloudSyncFolder);
                }
            }
            if (Refusal.Verify("cloud-sync root", () => Cloud.UnderSyncRoot(canonical) || tree.Folders.Exists(Cloud.UnderSyncRoot)))
            {
                throw new Refusal(RefusalReason.CloudSyncFolder);
            }

            // 11. Protection: by path, name and identity of the item and every folder above it; then owners in the tree.
            Faults.Point(Faults.Query.BeforeProtectionIdentity, canonical);
            var ancestors = Refusal.Verify("owner", () => Ancestors(canonical, volumeRoot));
            if (ancestors[0].Identity != stamp.Identity)
            {
                throw new Refusal(RefusalReason.ChangedSinceScan); // replaced while the gate ran
            }
            CheckProtected(context, canonical, volumeRoot, ancestors, tree, itemIsDirectory: (tagAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0, alsoContains: true);
            foreach (var path in tree.Files.Concat(tree.Folders))
            {
                if (path != canonical && Protection.OwnedByWindows(Refusal.Verify("owner", () => OwnerOf(path))))
                {
                    throw new Refusal(RefusalReason.SystemOwned);
                }
            }
            if (Protection.OwnedByWindows(ancestors[0].Owner.OwnerSid))
            {
                throw new Refusal(RefusalReason.SystemOwned);
            }
            foreach (var file in tree.Files)
            {
                if (Refusal.Verify("Windows Resource Protection", () => Protection.SystemProtected(file)))
                {
                    throw new Refusal(RefusalReason.SystemOwned);
                }
            }

            // 12. Allowed roots: the app's rule, after protection passed.
            var facts = Facts(context, canonical, stamp.Identity.VolumeSerial, ancestors);
            var decision = rule.Evaluate(facts);
            if (!decision.IsAllowed)
            {
                throw new Refusal(RefusalReason.NotAllowed, decision.Message);
            }
            return new GateResult(new ApprovedItem(canonical, stamp, tree.AllocatedBytes, tree.LogicalBytes, tree.FileCount, facts));
        }
    }

    internal sealed record Ancestor(OwnerFact Owner, FileIdentity Identity);

    /// <summary>The item first, then every folder above it up to the volume root: identity and owner.</summary>
    private static List<Ancestor> Ancestors(string canonical, string volumeRoot)
    {
        var list = new List<Ancestor>();
        var current = canonical;
        while (true)
        {
            // Identity is required (protection compares it); the owner of a folder above the item is a fact for the app's
            // rule only: when it cannot be read it is reported as unknown and never as the current user's.
            var (probe, error) = FileProbe.Open(current, readControl: false);
            if (probe is null)
            {
                throw new System.ComponentModel.Win32Exception(error, "open " + current);
            }
            FileIdentity identity;
            using (probe)
            {
                identity = probe.Identity();
            }
            var isItem = list.Count == 0;
            var sid = isItem ? OwnerOf(current) : TryOwnerOf(current) ?? string.Empty;
            list.Add(new Ancestor(new OwnerFact(current, sid, string.Equals(sid, Owners.CurrentUserSid, StringComparison.Ordinal)), identity));
            if (Canonical.SamePath(current, volumeRoot))
            {
                return list;
            }
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                return list;
            }
            current = parent;
        }
    }

    private static string? TryOwnerOf(string path)
    {
        var (probe, _) = FileProbe.Open(path);
        if (probe is null)
        {
            return null;
        }
        using (probe)
        {
            try
            {
                return Owners.OwnerOf(probe);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }
    }

    private static string OwnerOf(string path)
    {
        var (probe, error) = FileProbe.Open(path);
        if (probe is null)
        {
            throw new System.ComponentModel.Win32Exception(error, "open " + path);
        }
        using (probe)
        {
            return Owners.OwnerOf(probe);
        }
    }

    private static void CheckProtected(SafetyContext context, string canonical, string volumeRoot, List<Ancestor> ancestors, TreeFacts? tree, bool itemIsDirectory, bool alsoContains)
    {
        var protectedEntries = context.NeverTouchFolders.Concat(context.SystemLocations).ToList();
        if (Protection.Hits(canonical, protectedEntries, alsoContains) || Protection.IsVolumeRootName(canonical, volumeRoot))
        {
            throw new Refusal(RefusalReason.Protected);
        }
        foreach (var ancestor in ancestors)
        {
            if (context.ProtectedIdentities.Contains(ancestor.Identity))
            {
                throw new Refusal(RefusalReason.Protected);
            }
            var name = Path.GetFileName(ancestor.Owner.Path);
            var isItem = string.Equals(ancestor.Owner.Path, canonical, StringComparison.OrdinalIgnoreCase);
            // The item itself: both name rules, whatever its kind (owner decision 2026-10-17); folders above it: the folder rule.
            if (name.Length > 0 && (Protection.ProtectedName(name, isDirectory: true) || (isItem && Protection.ProtectedName(name, isDirectory: false))))
            {
                throw new Refusal(RefusalReason.Protected);
            }
        }
        if (tree is not null)
        {
            foreach (var file in tree.Files)
            {
                if (Protection.ProtectedName(Path.GetFileName(file), isDirectory: false))
                {
                    throw new Refusal(RefusalReason.Protected);
                }
            }
            foreach (var folder in tree.Folders)
            {
                if (Protection.ProtectedName(Path.GetFileName(folder), isDirectory: true))
                {
                    throw new Refusal(RefusalReason.Protected);
                }
            }
        }
    }

    private static ItemFacts Facts(SafetyContext context, string canonical, ulong volumeSerial, List<Ancestor> ancestors)
    {
        var profileSerial = Refusal.Verify("profile volume", () =>
        {
            var (probe, error) = FileProbe.Open(context.ProfileFolder, readControl: false);
            if (probe is null)
            {
                throw new System.ComponentModel.Win32Exception(error, "open profile");
            }
            using (probe)
            {
                return probe.Identity().VolumeSerial;
            }
        });
        return new ItemFacts(canonical, context.ProfileFolder, context.ProfilesFolder, volumeSerial, profileSerial,
            ancestors.Select(a => a.Owner).ToList(), isSystemLocation: false); // an item in a system location never reaches the rule
    }

    /// <summary>Putting back (docs/spec/safety.md): environment first, then the destination's spelling, existence, folder,
    /// cloud-sync roots, protection, and the app's restore rule.</summary>
    internal static GateResult CheckRestore(SafetyContext context, IAllowedRootRule rule, IEnvironmentFacts environment, string originalPath)
    {
        Refusal.Verify("native layout", () => { Native.LayoutCheck.Verify(); return 0; });
        CheckEnvironment(environment);
        var plain = PathSpelling.Plain(originalPath) ?? throw new Refusal(RefusalReason.UnsupportedPathForm);
        var (_, error) = FileProbe.Attributes(plain);
        if (error == 0)
        {
            throw new Refusal(RefusalReason.RestoreDestinationExists);
        }
        if (error == ERROR_UNTRUSTED_MOUNT_POINT)
        {
            // Windows refuses to follow a link in the destination's path for anyone (a standard user's junction): links are
            // never followed, putting back included.
            throw new Refusal(RefusalReason.LinkOrSpecialFile);
        }
        if (!FileProbe.IsNotFound(error))
        {
            throw new Refusal(RefusalReason.CouldNotVerify, "destination");
        }
        var parentPlain = Path.GetDirectoryName(plain) ?? throw new Refusal(RefusalReason.UnsupportedPathForm);
        // A destination inside a cloud-sync root is refused as such even when its folder is gone: listed roots by path
        // first, then the system's answer for the nearest existing folder.
        foreach (var root in context.SyncRoots)
        {
            if (Cloud.IsInside(plain, root))
            {
                throw new Refusal(RefusalReason.CloudSyncFolder);
            }
        }
        var existing = parentPlain;
        while (existing is not null && FileProbe.Attributes(existing).Error != 0)
        {
            existing = Path.GetDirectoryName(existing);
        }
        if (existing is not null && Refusal.Verify("cloud-sync root", () => Cloud.UnderSyncRoot(existing)))
        {
            throw new Refusal(RefusalReason.CloudSyncFolder);
        }
        var (parentAttributes, parentError) = FileProbe.Attributes(parentPlain);
        if (parentError != 0)
        {
            throw FileProbe.IsNotFound(parentError) ? new Refusal(RefusalReason.RestoreFolderMissing) : new Refusal(RefusalReason.CouldNotVerify, "destination folder");
        }
        if ((parentAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            throw new Refusal(RefusalReason.RestoreFolderMissing);
        }
        var (probe, openError) = FileProbe.Open(parentPlain, readControl: false);
        if (probe is null)
        {
            throw FileProbe.IsNotFound(openError) ? new Refusal(RefusalReason.RestoreFolderMissing) : new Refusal(RefusalReason.CouldNotVerify, "destination folder");
        }
        string parent;
        FileStamp parentStamp;
        using (probe)
        {
            var (tagAttributes, tag) = (parentAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0 ? Refusal.Verify("reparse point", probe.AttributeTag) : (parentAttributes, 0u);
            if (!FileProbe.TagAllowed(tagAttributes, tag))
            {
                throw new Refusal(RefusalReason.LinkOrSpecialFile);
            }
            parent = PathSpelling.Plain(Refusal.Verify("canonical path", probe.FinalPath)) ?? throw new Refusal(RefusalReason.UnsupportedPathForm);
            var fileSystem = Refusal.Verify("file system", probe.FileSystemName);
            if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase) && !string.Equals(fileSystem, "ReFS", StringComparison.OrdinalIgnoreCase))
            {
                throw new Refusal(RefusalReason.UnsupportedVolume);
            }
            parentStamp = Refusal.Verify("identity", probe.Stamp);
        }
        var destination = parent + "\\" + Path.GetFileName(plain);
        var volumeRoot = Refusal.Verify("volume", () => FileProbe.VolumeRoot(parent));
        if (GetDriveType(volumeRoot) != DRIVE_FIXED)
        {
            throw new Refusal(RefusalReason.UnsupportedVolume);
        }
        foreach (var root in context.SyncRoots)
        {
            if (Canonical.SamePath(parent, root) || Cloud.IsInside(parent, root))
            {
                throw new Refusal(RefusalReason.CloudSyncFolder);
            }
        }
        if (Refusal.Verify("cloud-sync root", () => Cloud.UnderSyncRoot(parent)))
        {
            throw new Refusal(RefusalReason.CloudSyncFolder);
        }
        var ancestors = Refusal.Verify("owner", () => Ancestors(parent, volumeRoot));
        if (Protection.Hits(destination, context.NeverTouchFolders.Concat(context.SystemLocations).ToList(), alsoContains: false)
            || Protection.IsVolumeRootName(destination, volumeRoot)
            // The destination is given as a path only: both name rules apply, so a folder put back under a protected
            // folder name is refused like a file under a protected file name (owner decision 2026-10-17).
            || Protection.ProtectedName(Path.GetFileName(destination), isDirectory: false)
            || Protection.ProtectedName(Path.GetFileName(destination), isDirectory: true))
        {
            throw new Refusal(RefusalReason.Protected);
        }
        // The destination does not exist yet: only "inside" counts. A parent that merely holds a never-touch entry (the
        // profile holds .ssh, a drive root holds Windows) is a normal place to put an item back.
        CheckProtected(context, parent, volumeRoot, ancestors, tree: null, itemIsDirectory: true, alsoContains: false);
        var facts = Facts(context, parent, parentStamp.Identity.VolumeSerial, ancestors);
        var decision = rule.EvaluateRestore(facts);
        if (!decision.IsAllowed)
        {
            throw new Refusal(RefusalReason.NotAllowed, decision.Message);
        }
        return new GateResult(new ApprovedItem(destination, parentStamp, 0, 0, 0, facts));
    }

    private static void CheckEnvironment(IEnvironmentFacts environment)
    {
        var (elevated, system, session, interactive) = Refusal.Verify("environment",
            () => (environment.IsElevated, environment.IsSystem, environment.SessionId, environment.HasInteractiveSid));
        if (elevated || system)
        {
            throw new Refusal(RefusalReason.Elevated);
        }
        if (session == 0 || !interactive)
        {
            throw new Refusal(RefusalReason.NotInteractive);
        }
    }

    private static void CheckRootOrProfileByName(SafetyContext context, string plain)
    {
        var parent = Path.GetDirectoryName(plain);
        if (Canonical.SamePath(plain, context.ProfilesFolder) || (parent is not null && Canonical.SamePath(parent, context.ProfilesFolder)))
        {
            throw new Refusal(RefusalReason.RootOrProfile);
        }
    }

    private static void CheckRootOrProfile(SafetyContext context, string canonical, string volumeRoot)
    {
        var parent = Path.GetDirectoryName(canonical);
        if (Canonical.SamePath(canonical, volumeRoot)
            || Canonical.SamePath(canonical, context.ProfileFolder)
            || Canonical.SamePath(canonical, context.ProfilesFolder)
            || (parent is not null && Canonical.SamePath(parent, context.ProfilesFolder)))
        {
            throw new Refusal(RefusalReason.RootOrProfile);
        }
    }
}
