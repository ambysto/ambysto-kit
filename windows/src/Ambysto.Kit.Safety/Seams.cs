namespace Ambysto.Kit.Safety;

/// <summary>Facts about the calling process's environment (Windows step 1). Internal seam: only the Kit's tests replace it.</summary>
internal interface IEnvironmentFacts
{
    bool IsElevated { get; }
    bool IsSystem { get; }
    int SessionId { get; }
    bool HasInteractiveSid { get; }
}

/// <summary>Machine and user policy a standard user cannot set (NoRecycleFiles). Internal seam: only the Kit's tests replace it.</summary>
internal interface IRecycleBinPolicy
{
    /// <summary>True when policy disallows the Recycle Bin; null when the policy cannot be read (fails closed).</summary>
    bool? NoRecycleFiles { get; }
}

internal sealed class SystemEnvironmentFacts : IEnvironmentFacts
{
    internal static readonly SystemEnvironmentFacts Instance = new();
    public bool IsElevated => throw new NotImplementedException();
    public bool IsSystem => throw new NotImplementedException();
    public int SessionId => throw new NotImplementedException();
    public bool HasInteractiveSid => throw new NotImplementedException();
}

internal sealed class SystemRecycleBinPolicy : IRecycleBinPolicy
{
    internal static readonly SystemRecycleBinPolicy Instance = new();
    public bool? NoRecycleFiles => throw new NotImplementedException();
}
