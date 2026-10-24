using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Processes;

/// <summary>Cumulative usage counters and memory footprint of a readable process (docs/spec/process.md, "A process record").</summary>
public sealed class ProcessCounters
{
    /// <summary>Creates usage counters for a process.</summary>
    /// <param name="memoryBytes">Private working set in bytes (non-negative).</param>
    /// <param name="cpuTimeNanoseconds">Total user + kernel CPU time in nanoseconds (non-negative).</param>
    /// <param name="bytesRead">Total I/O bytes read (non-negative).</param>
    /// <param name="bytesWritten">Total I/O bytes written (non-negative).</param>
    /// <param name="idleWakeups">Idle wakeups, or null when unavailable (always null on Windows).</param>
    /// <param name="interruptWakeups">Interrupt wakeups, or null when unavailable (always null on Windows).</param>
    /// <param name="energyNanojoules">Energy in nanojoules, or null when unavailable (always null on Windows).</param>
    public ProcessCounters(
        long memoryBytes,
        long cpuTimeNanoseconds,
        long bytesRead,
        long bytesWritten,
        long? idleWakeups = null,
        long? interruptWakeups = null,
        long? energyNanojoules = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>Physical memory footprint (private working set on Windows) in bytes.</summary>
    public long MemoryBytes { get; }

    /// <summary>Cumulative user + kernel CPU time in nanoseconds.</summary>
    public long CpuTimeNanoseconds { get; }

    /// <summary>Cumulative I/O bytes read.</summary>
    public long BytesRead { get; }

    /// <summary>Cumulative I/O bytes written.</summary>
    public long BytesWritten { get; }

    /// <summary>Cumulative idle wakeups, or null when absent (always null on Windows).</summary>
    public long? IdleWakeups { get; }

    /// <summary>Cumulative interrupt wakeups, or null when absent (always null on Windows).</summary>
    public long? InterruptWakeups { get; }

    /// <summary>Cumulative energy in nanojoules, or null when absent (always null on Windows).</summary>
    public long? EnergyNanojoules { get; }
}

/// <summary>One process in a snapshot (docs/spec/process.md, "A process record").</summary>
public sealed class ProcessRecord
{
    private ProcessRecord()
    {
        Name = string.Empty;
    }

    /// <summary>The process identity (process id + creation time).</summary>
    public ProcessIdentity Identity { get; }

    /// <summary>The parent process id when a live parent with creation time at or before this process exists in the snapshot; otherwise null.</summary>
    public int? ParentProcessId { get; }

    /// <summary>Short image name from the snapshot.</summary>
    public string Name { get; }

    /// <summary>The Windows session id.</summary>
    public int SessionId { get; }

    /// <summary>True when the process belongs to the current user, is not elevated, and could be opened; false when unreadable.</summary>
    public bool IsReadable { get; }

    /// <summary>The owner SID string (<c>S-1-5-…</c>) when readable; null when unreadable.</summary>
    public string? OwnerSid { get; }

    /// <summary>Full DOS path of the executable file when readable and available; otherwise null.</summary>
    public string? ExecutablePath { get; }

    /// <summary>Normalized working directory (64-bit processes only; leading <c>\\?\</c> stripped, trailing <c>\</c> trimmed except on a drive root) when readable and available; null when unreadable or WOW64.</summary>
    public string? WorkingDirectory { get; }

    /// <summary>Raw command line string when readable and available; otherwise null.</summary>
    public string? CommandLine { get; }

    /// <summary>Arguments split by <c>CommandLineToArgvW</c> rules when <see cref="CommandLine"/> is non-null (empty list when empty or whitespace); null when <see cref="CommandLine"/> is null or the process is unreadable.</summary>
    public IReadOnlyList<string>? Arguments { get; }

    /// <summary>Usage counters when readable; null when unreadable.</summary>
    public ProcessCounters? Usage { get; }

    /// <summary>Creates a readable process record.</summary>
    /// <param name="identity">The process identity.</param>
    /// <param name="parentProcessId">Validated parent process id, or null.</param>
    /// <param name="name">Short image name.</param>
    /// <param name="sessionId">Windows session id (non-negative).</param>
    /// <param name="ownerSid">Owner SID string (non-empty).</param>
    /// <param name="executablePath">Full DOS executable path, or null.</param>
    /// <param name="workingDirectory">Normalized working directory, or null.</param>
    /// <param name="commandLine">Raw command line, or null.</param>
    /// <param name="arguments">Parsed arguments (non-null when <paramref name="commandLine"/> is non-null; null when <paramref name="commandLine"/> is null).</param>
    /// <param name="usage">Usage counters.</param>
    /// <returns>The readable record.</returns>
    public static ProcessRecord Readable(
        ProcessIdentity identity,
        int? parentProcessId,
        string name,
        int sessionId,
        string ownerSid,
        string? executablePath,
        string? workingDirectory,
        string? commandLine,
        IReadOnlyList<string>? arguments,
        ProcessCounters usage)
    {
        throw new NotImplementedException();
    }

    /// <summary>Creates an unreadable process record (elevated process of the current user, or process of another user/SYSTEM).</summary>
    /// <param name="identity">The process identity from the snapshot.</param>
    /// <param name="parentProcessId">Validated parent process id, or null.</param>
    /// <param name="name">Short image name from the snapshot.</param>
    /// <param name="sessionId">Windows session id (non-negative).</param>
    /// <returns>The unreadable record.</returns>
    public static ProcessRecord Unreadable(
        ProcessIdentity identity,
        int? parentProcessId,
        string name,
        int sessionId)
    {
        throw new NotImplementedException();
    }
}

/// <summary>A snapshot of all live processes on the system.</summary>
public sealed class ProcessSnapshot
{
    /// <summary>Creates a process snapshot.</summary>
    /// <param name="processes">All surviving processes (readable and unreadable).</param>
    /// <param name="unreadableProcessCount">The count of unreadable processes in the snapshot (non-negative).</param>
    public ProcessSnapshot(IReadOnlyList<ProcessRecord> processes, int unreadableProcessCount)
    {
        throw new NotImplementedException();
    }

    /// <summary>All surviving processes (readable and unreadable).</summary>
    public IReadOnlyList<ProcessRecord> Processes { get; } = Array.Empty<ProcessRecord>();

    /// <summary>How many processes could not be read (elevated, SYSTEM, services or other users).</summary>
    public int UnreadableProcessCount { get; }
}

/// <summary>Reads the process table and re-checks single processes by identity (docs/spec/process.md).</summary>
public sealed class ProcessReader
{
    /// <summary>Creates a reader over the live Windows process table.</summary>
    public ProcessReader()
    {
        throw new NotImplementedException();
    }

    internal ProcessReader(IProcessSnapshotSource source)
    {
        throw new NotImplementedException();
    }

    /// <summary>Reads a snapshot of all live processes.</summary>
    /// <returns>The snapshot.</returns>
    public ProcessSnapshot ReadSnapshot()
    {
        throw new NotImplementedException();
    }

    /// <summary>Re-checks a single process by its identity: returns the readable record when the process is still alive with the same creation time, belongs to the current user and is not elevated; otherwise null.</summary>
    /// <param name="identity">The process identity to check.</param>
    /// <returns>The current readable record, or null if exited, reused or unreadable.</returns>
    public ProcessRecord? Read(ProcessIdentity identity)
    {
        throw new NotImplementedException();
    }

    /// <summary>Re-checks a process's executable path immediately before acting on it (docs/spec/process.md, "Identity").</summary>
    /// <param name="identity">The process identity to check.</param>
    /// <returns>The current executable path, or null if exited, reused, unreadable or unavailable.</returns>
    public string? ExecutablePath(ProcessIdentity identity)
    {
        throw new NotImplementedException();
    }
}
