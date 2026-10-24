using System;
using System.Collections.Generic;
using Ambysto.Kit.Processes.Internal;
using Ambysto.Kit.Processes.Native;

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
        ArgumentOutOfRangeException.ThrowIfNegative(memoryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(cpuTimeNanoseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesRead);
        ArgumentOutOfRangeException.ThrowIfNegative(bytesWritten);
        if (idleWakeups is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(idleWakeups));
        }

        if (interruptWakeups is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interruptWakeups));
        }

        if (energyNanojoules is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(energyNanojoules));
        }

        MemoryBytes = memoryBytes;
        CpuTimeNanoseconds = cpuTimeNanoseconds;
        BytesRead = bytesRead;
        BytesWritten = bytesWritten;
        IdleWakeups = idleWakeups;
        InterruptWakeups = interruptWakeups;
        EnergyNanojoules = energyNanojoules;
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
    private ProcessRecord(
        ProcessIdentity identity,
        int? parentProcessId,
        string name,
        int sessionId,
        bool isReadable,
        string? ownerSid,
        string? executablePath,
        string? workingDirectory,
        string? commandLine,
        IReadOnlyList<string>? arguments,
        ProcessCounters? usage)
    {
        Identity = identity;
        ParentProcessId = parentProcessId;
        Name = name;
        SessionId = sessionId;
        IsReadable = isReadable;
        OwnerSid = ownerSid;
        ExecutablePath = executablePath;
        WorkingDirectory = workingDirectory;
        CommandLine = commandLine;
        Arguments = arguments;
        Usage = usage;
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
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSid);
        ArgumentNullException.ThrowIfNull(usage);

        if (commandLine is not null && arguments is null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        if (commandLine is null && arguments is not null)
        {
            throw new ArgumentException("Arguments must be null when CommandLine is null.", nameof(arguments));
        }

        return new ProcessRecord(
            identity: identity,
            parentProcessId: parentProcessId,
            name: name,
            sessionId: sessionId,
            isReadable: true,
            ownerSid: ownerSid,
            executablePath: executablePath,
            workingDirectory: workingDirectory,
            commandLine: commandLine,
            arguments: arguments,
            usage: usage);
    }

    /// <summary>Creates an unreadable process record (elevated process of the current user, or process of another user/SYSTEM).</summary>
    public static ProcessRecord Unreadable(
        ProcessIdentity identity,
        int? parentProcessId,
        string name,
        int sessionId)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionId);

        return new ProcessRecord(
            identity: identity,
            parentProcessId: parentProcessId,
            name: name,
            sessionId: sessionId,
            isReadable: false,
            ownerSid: null,
            executablePath: null,
            workingDirectory: null,
            commandLine: null,
            arguments: null,
            usage: null);
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
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentOutOfRangeException.ThrowIfNegative(unreadableProcessCount);

        Processes = processes;
        UnreadableProcessCount = unreadableProcessCount;
    }

    /// <summary>All surviving processes (readable and unreadable).</summary>
    public IReadOnlyList<ProcessRecord> Processes { get; }

    /// <summary>How many processes could not be read (elevated, SYSTEM, services or other users).</summary>
    public int UnreadableProcessCount { get; }
}

/// <summary>Reads the process table and re-checks single processes by identity (docs/spec/process.md).</summary>
public sealed class ProcessReader
{
    private readonly IProcessSnapshotSource _source;

    /// <summary>Creates a reader over the live Windows process table.</summary>
    public ProcessReader()
        : this(new SystemProcessSnapshotSource())
    {
    }

    internal ProcessReader(IProcessSnapshotSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Reads a snapshot of all live processes.</summary>
    /// <returns>The snapshot.</returns>
    public ProcessSnapshot ReadSnapshot()
    {
        IReadOnlyList<RawProcessEntry> rawEntries = _source.ReadEntries();
        var surviving = new List<RawProcessEntry>(rawEntries.Count);
        var maxStartByPid = new Dictionary<int, long>(rawEntries.Count);

        // Pass 1: filter surviving entries and record the greatest StartTimeTicks per PID for parent validation.
        foreach (RawProcessEntry entry in rawEntries)
        {
            if (!IsSurvivingEntry(entry))
            {
                continue;
            }

            surviving.Add(entry);
            _ = maxStartByPid.TryGetValue(entry.ProcessId, out long existingStart);
            if (entry.SnapshotStartTimeTicks > existingStart)
            {
                maxStartByPid[entry.ProcessId] = entry.SnapshotStartTimeTicks;
            }
        }

        // Pass 2: validate parent PIDs and build readable / unreadable ProcessRecord instances.
        var records = new List<ProcessRecord>(surviving.Count);
        int unreadableCount = 0;

        foreach (RawProcessEntry entry in surviving)
        {
            int? parentPid = ValidateParentFromSnapshot(entry, maxStartByPid);
            ProcessRecord record = BuildProcessRecord(entry, parentPid);
            if (!record.IsReadable)
            {
                unreadableCount++;
            }

            records.Add(record);
        }

        return new ProcessSnapshot(records, unreadableCount);
    }

    /// <summary>Re-checks a single process by its identity: returns the readable record when the process is still alive with the same creation time, belongs to the current user and is not elevated; otherwise null.</summary>
    /// <param name="identity">The process identity to check.</param>
    /// <returns>The current readable record, or null if exited, reused or unreadable.</returns>
    public ProcessRecord? Read(ProcessIdentity identity)
    {
        RawProcessEntry? entry = _source.ReadEntry(identity.ProcessId);
        if (entry is null ||
            entry.ProcessId != identity.ProcessId ||
            entry.SnapshotStartTimeTicks != identity.StartTimeTicks ||
            !IsSurvivingEntry(entry) ||
            !IsReadableEntry(entry))
        {
            return null;
        }

        int? parentPid = ValidateParentSingleLookup(entry);
        return BuildReadableRecord(entry, parentPid);
    }

    /// <summary>Re-checks a process's executable path immediately before acting on it (docs/spec/process.md, "Identity").</summary>
    /// <param name="identity">The process identity to check.</param>
    /// <returns>The current executable path, or null if exited, reused, unreadable or unavailable.</returns>
    public string? ExecutablePath(ProcessIdentity identity) =>
        Read(identity)?.ExecutablePath;

    private static bool IsSurvivingEntry(RawProcessEntry entry)
    {
        if (entry.ProcessId <= 0 ||
            entry.SnapshotStartTimeTicks <= 0 ||
            entry.NumberOfThreads == 0)
        {
            return false;
        }

        RawProcessHandleResult handle = entry.HandleResult;
        if (handle.ExitedOrMissing)
        {
            return false;
        }

        if (!handle.AccessDenied)
        {
            if (handle.HandleExitTimeBeforeTicks != 0 ||
                handle.HandleStartTimeBeforeTicks != entry.SnapshotStartTimeTicks ||
                handle.HandleExitTimeAfterTicks != 0 ||
                handle.HandleStartTimeAfterTicks != entry.SnapshotStartTimeTicks)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsReadableEntry(RawProcessEntry entry)
    {
        RawProcessHandleResult handle = entry.HandleResult;
        return !handle.AccessDenied &&
               !handle.IsElevated &&
               !string.IsNullOrEmpty(handle.OwnerSid) &&
               string.Equals(handle.OwnerSid, _source.CurrentUserSid, StringComparison.Ordinal);
    }

    private static int? ValidateParentFromSnapshot(
        RawProcessEntry entry,
        Dictionary<int, long> maxStartByPid)
    {
        int rawParent = entry.InheritedFromProcessId;
        if (rawParent <= 0 || rawParent == entry.ProcessId)
        {
            return null;
        }

        if (maxStartByPid.TryGetValue(rawParent, out long parentStartTicks) &&
            parentStartTicks <= entry.SnapshotStartTimeTicks)
        {
            return rawParent;
        }

        return null;
    }

    private int? ValidateParentSingleLookup(RawProcessEntry entry)
    {
        int rawParent = entry.InheritedFromProcessId;
        if (rawParent <= 0 || rawParent == entry.ProcessId)
        {
            return null;
        }

        RawProcessEntry? parentEntry = _source.ReadEntry(rawParent);
        if (parentEntry is not null &&
            parentEntry.ProcessId == rawParent &&
            IsSurvivingEntry(parentEntry) &&
            parentEntry.SnapshotStartTimeTicks <= entry.SnapshotStartTimeTicks)
        {
            return rawParent;
        }

        return null;
    }

    private ProcessRecord BuildProcessRecord(RawProcessEntry entry, int? parentPid)
    {
        if (!IsReadableEntry(entry))
        {
            return ProcessRecord.Unreadable(
                identity: new ProcessIdentity(entry.ProcessId, entry.SnapshotStartTimeTicks),
                parentProcessId: parentPid,
                name: entry.Name,
                sessionId: Math.Max(0, entry.SessionId));
        }

        return BuildReadableRecord(entry, parentPid);
    }

    private static ProcessRecord BuildReadableRecord(RawProcessEntry entry, int? parentPid)
    {
        RawProcessHandleResult handle = entry.HandleResult;
        string? normalizedCwd = handle.IsWow64 ? null : NormalizeWorkingDirectory(handle.WorkingDirectory);
        string? commandLine = handle.CommandLine;
        IReadOnlyList<string>? arguments = commandLine is null
            ? null
            : ProcessNativeMethods.ParseCommandLineArguments(commandLine);

        return ProcessRecord.Readable(
            identity: new ProcessIdentity(entry.ProcessId, entry.SnapshotStartTimeTicks),
            parentProcessId: parentPid,
            name: entry.Name,
            sessionId: Math.Max(0, entry.SessionId),
            ownerSid: handle.OwnerSid!,
            executablePath: string.IsNullOrEmpty(handle.ExecutablePath) ? null : handle.ExecutablePath,
            workingDirectory: normalizedCwd,
            commandLine: commandLine,
            arguments: arguments,
            usage: entry.Counters);
    }

    internal static string? NormalizeWorkingDirectory(string? rawWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawWorkingDirectory))
        {
            return null;
        }

        string path = rawWorkingDirectory.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? rawWorkingDirectory.Substring(4)
            : rawWorkingDirectory;

        if (path.Length == 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            path[2] == '\\')
        {
            return path;
        }

        string trimmed = path.TrimEnd('\\');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
