using System;
using System.Collections.Generic;
using System.Linq;

namespace Ambysto.Kit.Safety;

/// <summary>What the app showed: the path and the stamp it recorded at scan time.</summary>
public sealed class GateRequest
{
    /// <summary>Creates a request.</summary>
    /// <param name="path">The path the app showed.</param>
    /// <param name="shownStamp">The stamp recorded when it was shown.</param>
    public GateRequest(string path, FileStamp shownStamp)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path; ShownStamp = shownStamp;
    }

    /// <summary>The path the app showed.</summary>
    public string Path { get; }
    /// <summary>The stamp recorded when it was shown.</summary>
    public FileStamp ShownStamp { get; }
}

/// <summary>An item the gate approved, measured just now.</summary>
public sealed class ApprovedItem
{
    internal ApprovedItem(string canonicalPath, FileStamp stamp, long allocatedBytes, long logicalBytes, int fileCount, ItemFacts facts)
    {
        CanonicalPath = canonicalPath; Stamp = stamp; AllocatedBytes = allocatedBytes; LogicalBytes = logicalBytes; FileCount = fileCount; Facts = facts;
    }

    /// <summary>The canonical path the gate approved; the app moves exactly this path.</summary>
    public string CanonicalPath { get; }
    /// <summary>The item's stamp now.</summary>
    public FileStamp Stamp { get; }
    /// <summary>Bytes the item's tree uses on disk.</summary>
    public long AllocatedBytes { get; }
    /// <summary>The sum of the lengths of the files in the item's tree.</summary>
    public long LogicalBytes { get; }
    /// <summary>The number of files in the item's tree (1 for a file).</summary>
    public int FileCount { get; }
    /// <summary>The facts the allowed-root rule saw.</summary>
    public ItemFacts Facts { get; }
}

/// <summary>The gate's answer: an approved item, or a refusal with its reason.</summary>
public sealed class GateResult
{
    internal GateResult(ApprovedItem approved) { Approved = approved; }

    internal GateResult(RefusalReason reason, string? detail) { Reason = reason; Detail = detail; }

    /// <summary>The approved item, or null when refused.</summary>
    public ApprovedItem? Approved { get; }
    /// <summary>The refusal reason, or null when approved.</summary>
    public RefusalReason? Reason { get; }
    /// <summary>For <see cref="RefusalReason.CouldNotVerify"/> the check's name; for <see cref="RefusalReason.NotAllowed"/> the app's text; otherwise optional detail.</summary>
    public string? Detail { get; }
    /// <summary>True when approved.</summary>
    public bool IsApproved => Approved is not null;
    /// <summary>What the user is told when refused (the spec's message), or null when approved.</summary>
    public string? Message => Reason switch
    {
        null => null,
        RefusalReason.NotAllowed => Detail ?? RefusalReason.NotAllowed.Message(),
        RefusalReason.CouldNotVerify => RefusalReason.CouldNotVerify.Message() + (Detail ?? "unknown check"),
        RefusalReason r => r.Message(),
    };
}

/// <summary>What the gate knows about this user's session: profile, known folders, never-touch entries, system locations, sync roots.</summary>
public sealed class SafetyContext
{
    private SafetyContext(string profileFolder, string profilesFolder, IReadOnlyList<string> neverTouch, IReadOnlyList<string> systemLocations,
        IReadOnlyList<string> syncRoots)
    {
        ProfileFolder = profileFolder; ProfilesFolder = profilesFolder; NeverTouchFolders = neverTouch; SystemLocations = systemLocations; SyncRoots = syncRoots;
        ProtectedIdentities = Internal.Protection.Identities(neverTouch.Concat(systemLocations));
    }

    /// <summary>The current user's profile folder (canonical).</summary>
    public string ProfileFolder { get; }
    /// <summary>The folder that holds every user's profile (canonical).</summary>
    public string ProfilesFolder { get; }
    /// <summary>Never-touch entries resolved to canonical paths: the spec's rows and the app's additions. Entries the spec
    /// protects by name wherever they are (registry hives, password vaults, mail stores, image backups) are rules, not rows.</summary>
    public IReadOnlyList<string> NeverTouchFolders { get; }
    /// <summary>System locations resolved to canonical paths, with the per-volume rows of the system volume (the gate applies
    /// the per-volume names to every volume).</summary>
    public IReadOnlyList<string> SystemLocations { get; }
    /// <summary>Cloud-sync roots that can be listed for this user: those registered through the system's sync-root
    /// manager, the OneDrive folders of the environment, and the known provider folders directly in the profile. A Cloud
    /// Files registration that no list shows is still found by the gate, which asks the system about each item.</summary>
    public IReadOnlyList<string> SyncRoots { get; }

    internal HashSet<FileIdentity> ProtectedIdentities { get; }

    /// <summary>Resolves the context for the current user once per session.</summary>
    /// <param name="appProtectedFolders">The app's own data folders, added to never-touch (entries can only be added).</param>
    /// <returns>The context.</returns>
    public static SafetyContext ForCurrentUser(IEnumerable<string> appProtectedFolders)
    {
        ArgumentNullException.ThrowIfNull(appProtectedFolders);
        var additions = new List<string>();
        foreach (var folder in appProtectedFolders)
        {
            ArgumentNullException.ThrowIfNull(folder);
            additions.Add(folder);
        }
        var profile = Internal.Canonical.OfExisting(Internal.KnownFolders.Get(Internal.KnownFolders.Profile));
        var profiles = Internal.Canonical.OfExisting(Internal.KnownFolders.Get(Internal.KnownFolders.UserProfiles));
        var roaming = Internal.KnownFolders.Get(Internal.KnownFolders.RoamingAppData);
        var local = Internal.KnownFolders.Get(Internal.KnownFolders.LocalAppData);
        var neverTouch = new List<string>
        {
            System.IO.Path.Combine(profile, ".claude", "projects"), System.IO.Path.Combine(profile, ".ssh"), System.IO.Path.Combine(profile, ".gnupg"),
            Internal.KnownFolders.Get(Internal.KnownFolders.Pictures),
            System.IO.Path.Combine(roaming, "Microsoft", "Protect"), System.IO.Path.Combine(roaming, "Microsoft", "Credentials"),
            System.IO.Path.Combine(roaming, "Microsoft", "Crypto"), System.IO.Path.Combine(roaming, "Microsoft", "SystemCertificates"),
            System.IO.Path.Combine(local, "Microsoft", "Credentials"), System.IO.Path.Combine(local, "Microsoft", "Vault"),
            System.IO.Path.Combine(local, "Microsoft", "Outlook"), System.IO.Path.Combine(roaming, "Thunderbird"),
            System.IO.Path.Combine(local, "Packages"),
            System.IO.Path.Combine(roaming, "Apple Computer", "MobileSync", "Backup"), System.IO.Path.Combine(profile, "Apple", "MobileSync", "Backup"),
            System.IO.Path.Combine(local, "Microsoft", "Windows", "FileHistory"),
            System.IO.Path.Combine(local, "Google", "Chrome", "User Data"), System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data"),
            System.IO.Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), System.IO.Path.Combine(roaming, "Mozilla", "Firefox"),
        };
        var systemRoot = Internal.FileProbe.VolumeRoot(profile);
        var systemLocations = new List<string>
        {
            Internal.KnownFolders.Get(Internal.KnownFolders.Windows),
            Internal.KnownFolders.Get(Internal.KnownFolders.ProgramFiles),
            Internal.KnownFolders.TryGet(Internal.KnownFolders.ProgramFilesX86) ?? Internal.KnownFolders.Get(Internal.KnownFolders.ProgramFiles),
            Internal.KnownFolders.Get(Internal.KnownFolders.ProgramData),
            System.IO.Path.Combine(local, "Microsoft", "WindowsApps"),
        };
        foreach (var name in Internal.Protection.VolumeRootNames)
        {
            if (!name.Contains('~', StringComparison.Ordinal))
            {
                systemLocations.Add(System.IO.Path.Combine(systemRoot, name));
            }
        }
        var builtIn = Internal.Protection.Resolve(neverTouch);
        var all = new List<string>(builtIn);
        foreach (var entry in Internal.Protection.Resolve(additions))
        {
            all.Add(entry); // added even when it equals a built-in: entries are only ever added
        }
        return new SafetyContext(profile, profiles, all, Internal.Protection.Resolve(systemLocations), Internal.Cloud.SyncRoots(profile));
    }
}

/// <summary>The file-safety gate (docs/spec/safety.md, "The gate", Windows order). It decides; it never changes anything.</summary>
public sealed class SafetyGate
{
    private readonly SafetyContext context;
    private readonly IAllowedRootRule rule;
    private readonly IEnvironmentFacts environment;
    private readonly IRecycleBinPolicy recycleBinPolicy;

    /// <summary>Creates a gate for this session.</summary>
    /// <param name="context">The session's context.</param>
    /// <param name="rule">The app's allowed-root rule.</param>
    public SafetyGate(SafetyContext context, IAllowedRootRule rule)
        : this(context, rule, SystemEnvironmentFacts.Instance, SystemRecycleBinPolicy.Instance) { }

    internal SafetyGate(SafetyContext context, IAllowedRootRule rule, IEnvironmentFacts environment, IRecycleBinPolicy recycleBinPolicy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rule);
        this.context = context; this.rule = rule; this.environment = environment; this.recycleBinPolicy = recycleBinPolicy;
    }

    /// <summary>Runs every check, in the spec's order, immediately before one item's move.</summary>
    /// <param name="request">What the app showed.</param>
    /// <returns>The approved item or the first refusal.</returns>
    public GateResult Check(GateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Internal.GateRun.Check(context, rule, environment, recycleBinPolicy, request);
        }
        catch (Internal.Refusal refusal)
        {
            return new GateResult(refusal.Reason, refusal.Detail);
        }
    }

    /// <summary>Checks a put-back destination (docs/spec/safety.md, "Putting back").</summary>
    /// <param name="originalPath">Where the item was before it was moved.</param>
    /// <returns>Approved (with the destination's parent facts) or a refusal.</returns>
    public GateResult CheckRestoreDestination(string originalPath)
    {
        ArgumentNullException.ThrowIfNull(originalPath);
        try
        {
            return Internal.GateRun.CheckRestore(context, rule, environment, originalPath);
        }
        catch (Internal.Refusal refusal)
        {
            return new GateResult(refusal.Reason, refusal.Detail);
        }
    }
}
