using System;

namespace Ambysto.Kit.Processes;

/// <summary>Identifies a process by its process id and creation time (docs/spec/process.md, "Identity").</summary>
public readonly record struct ProcessIdentity
{
    /// <summary>Creates a process identity.</summary>
    /// <param name="processId">The process id (positive).</param>
    /// <param name="startTimeTicks">The Windows creation time in 100-ns FILETIME ticks since 1601-01-01 UTC (positive).</param>
    public ProcessIdentity(int processId, long startTimeTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startTimeTicks);

        ProcessId = processId;
        StartTimeTicks = startTimeTicks;
    }

    /// <summary>The process id.</summary>
    public int ProcessId { get; }

    /// <summary>The Windows creation time in 100-ns FILETIME ticks since 1601-01-01 UTC.</summary>
    public long StartTimeTicks { get; }
}
