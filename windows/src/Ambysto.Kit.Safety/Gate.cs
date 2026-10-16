using System;
using System.Collections.Generic;

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
    public string? Message => throw new NotImplementedException();
}

/// <summary>What the gate knows about this user's session: profile, known folders, never-touch entries, system locations, sync roots.</summary>
public sealed class SafetyContext
{
    private SafetyContext() { }

    /// <summary>The current user's profile folder.</summary>
    public string ProfileFolder => throw new NotImplementedException();
    /// <summary>The folder that holds every user's profile.</summary>
    public string ProfilesFolder => throw new NotImplementedException();
    /// <summary>Never-touch entries resolved to canonical paths: the spec's rows and the app's additions.</summary>
    public IReadOnlyList<string> NeverTouchFolders => throw new NotImplementedException();
    /// <summary>System locations resolved to canonical paths.</summary>
    public IReadOnlyList<string> SystemLocations => throw new NotImplementedException();
    /// <summary>Cloud-sync roots found for this user.</summary>
    public IReadOnlyList<string> SyncRoots => throw new NotImplementedException();

    /// <summary>Resolves the context for the current user once per session.</summary>
    /// <param name="appProtectedFolders">The app's own data folders, added to never-touch (entries can only be added).</param>
    /// <returns>The context.</returns>
    public static SafetyContext ForCurrentUser(IEnumerable<string> appProtectedFolders) => throw new NotImplementedException();
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
    public GateResult Check(GateRequest request) => throw new NotImplementedException();

    /// <summary>Checks a put-back destination (docs/spec/safety.md, "Putting back").</summary>
    /// <param name="originalPath">Where the item was before it was moved.</param>
    /// <returns>Approved (with the destination's parent facts) or a refusal.</returns>
    public GateResult CheckRestoreDestination(string originalPath) => throw new NotImplementedException();
}
