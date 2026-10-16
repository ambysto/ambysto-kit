using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Safety;

/// <summary>A process that holds something inside an item.</summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="ImageName">The image name when readable.</param>
/// <param name="Method">Which method named it.</param>
public readonly record struct InUseHolder(int ProcessId, string? ImageName, string Method);

/// <summary>The result of an in-use check.</summary>
public sealed class InUseResult
{
    internal InUseResult(IReadOnlyList<InUseHolder> holders, int unreadableProcessCount)
    {
        Holders = holders; UnreadableProcessCount = unreadableProcessCount;
    }

    /// <summary>Every holder any method named.</summary>
    public IReadOnlyList<InUseHolder> Holders { get; }
    /// <summary>Processes that could not be read at all (reported, never blocking).</summary>
    public int UnreadableProcessCount { get; }
    /// <summary>True when any method named a holder.</summary>
    public bool IsInUse => Holders.Count > 0;
}

/// <summary>The in-use check (docs/spec/safety.md, "In use", Windows note).</summary>
public sealed class InUseChecker
{
    /// <summary>Checks every file and folder in the approved item's tree.</summary>
    /// <param name="item">An item the gate approved.</param>
    /// <returns>The holders found.</returns>
#pragma warning disable CA1822 // the public API is an instance method (APS-24 plan, Design)
    public InUseResult Check(ApprovedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Internal.InUse.Check(item);
    }
#pragma warning restore CA1822
}
