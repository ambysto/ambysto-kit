using System;
using System.Collections.Generic;
using Ambysto.Kit.Processes.Internal;

namespace Ambysto.Kit.Processes;

/// <summary>Per-interval counter changes between two samples of the same <see cref="ProcessIdentity"/> (docs/spec/process.md, "Sampling").</summary>
public sealed class UsageDeltas
{
    /// <summary>Creates a set of per-interval deltas.</summary>
    /// <param name="intervalNanoseconds">Elapsed monotonic time (excluding sleep) between the two samples in nanoseconds (positive).</param>
    /// <param name="cpuTimeNanoseconds">CPU time delta in nanoseconds (non-negative).</param>
    /// <param name="bytesRead">Bytes read delta (non-negative).</param>
    /// <param name="bytesWritten">Bytes written delta (non-negative).</param>
    /// <param name="idleWakeups">Idle wakeups delta when present in both samples; null on Windows.</param>
    /// <param name="interruptWakeups">Interrupt wakeups delta when present in both samples; null on Windows.</param>
    /// <param name="energyNanojoules">Energy delta in nanojoules when present in both samples (0 stays 0); null on Windows.</param>
    public UsageDeltas(
        long intervalNanoseconds,
        long cpuTimeNanoseconds,
        long bytesRead,
        long bytesWritten,
        long? idleWakeups = null,
        long? interruptWakeups = null,
        long? energyNanojoules = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalNanoseconds);
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

        IntervalNanoseconds = intervalNanoseconds;
        CpuTimeNanoseconds = cpuTimeNanoseconds;
        BytesRead = bytesRead;
        BytesWritten = bytesWritten;
        IdleWakeups = idleWakeups;
        InterruptWakeups = interruptWakeups;
        EnergyNanojoules = energyNanojoules;
    }

    /// <summary>Elapsed monotonic interval in nanoseconds (sleep excluded).</summary>
    public long IntervalNanoseconds { get; }

    /// <summary>CPU time used during the interval in nanoseconds.</summary>
    public long CpuTimeNanoseconds { get; }

    /// <summary>Bytes read during the interval.</summary>
    public long BytesRead { get; }

    /// <summary>Bytes written during the interval.</summary>
    public long BytesWritten { get; }

    /// <summary>Idle wakeups during the interval, or null when absent.</summary>
    public long? IdleWakeups { get; }

    /// <summary>Interrupt wakeups during the interval, or null when absent.</summary>
    public long? InterruptWakeups { get; }

    /// <summary>Energy used during the interval in nanojoules, or null when absent.</summary>
    public long? EnergyNanojoules { get; }
}

/// <summary>A readable process in a sample, with its deltas and rates when a valid previous sample of the same identity exists.</summary>
public sealed class SampledProcess
{
    /// <summary>Creates a sampled process record.</summary>
    /// <param name="process">The readable process record (must have <see cref="ProcessRecord.IsReadable"/> true and non-null <see cref="ProcessRecord.Usage"/>).</param>
    /// <param name="deltas">Deltas since the previous sample of the same identity, or null on the first sample, zero/negative interval, or when any counter decreased.</param>
    /// <param name="cpuPercent">CPU percentage per core (one busy core = 100.0), or null when <paramref name="deltas"/> is null.</param>
    /// <param name="bytesReadPerSecond">Bytes read per second, or null when <paramref name="deltas"/> is null.</param>
    /// <param name="bytesWrittenPerSecond">Bytes written per second, or null when <paramref name="deltas"/> is null.</param>
    /// <param name="idleWakeupsPerSecond">Idle wakeups per second, or null when <paramref name="deltas"/> or idle wakeups are null.</param>
    /// <param name="interruptWakeupsPerSecond">Interrupt wakeups per second, or null when <paramref name="deltas"/> or interrupt wakeups are null.</param>
    /// <param name="isZombie">Whether the process is a zombie (always false on Windows).</param>
    public SampledProcess(
        ProcessRecord process,
        UsageDeltas? deltas,
        double? cpuPercent,
        double? bytesReadPerSecond,
        double? bytesWrittenPerSecond,
        double? idleWakeupsPerSecond = null,
        double? interruptWakeupsPerSecond = null,
        bool isZombie = false)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!process.IsReadable || process.Usage is null)
        {
            throw new ArgumentException("SampledProcess requires a readable ProcessRecord with non-null Usage.", nameof(process));
        }

        Process = process;
        Deltas = deltas;
        CpuPercent = cpuPercent;
        BytesReadPerSecond = bytesReadPerSecond;
        BytesWrittenPerSecond = bytesWrittenPerSecond;
        IdleWakeupsPerSecond = idleWakeupsPerSecond;
        InterruptWakeupsPerSecond = interruptWakeupsPerSecond;
        IsZombie = isZombie;
    }

    /// <summary>The readable process record.</summary>
    public ProcessRecord Process { get; }

    /// <summary>Deltas since the previous sample of the same identity, or null.</summary>
    public UsageDeltas? Deltas { get; }

    /// <summary>CPU % per core (one fully busy core = 100.0), or null.</summary>
    public double? CpuPercent { get; }

    /// <summary>Bytes read per second, or null.</summary>
    public double? BytesReadPerSecond { get; }

    /// <summary>Bytes written per second, or null.</summary>
    public double? BytesWrittenPerSecond { get; }

    /// <summary>Idle wakeups per second, or null (always null on Windows).</summary>
    public double? IdleWakeupsPerSecond { get; }

    /// <summary>Interrupt wakeups per second, or null (always null on Windows).</summary>
    public double? InterruptWakeupsPerSecond { get; }

    /// <summary>Whether the process is a zombie (always false on Windows).</summary>
    public bool IsZombie { get; }
}

/// <summary>One sampling tick from <see cref="ProcessSampler.Sample"/>.</summary>
public sealed class ProcessSample
{
    /// <summary>Creates a process sample.</summary>
    /// <param name="processes">Sampled readable processes.</param>
    /// <param name="allProcesses">All surviving processes (readable and unreadable) from the underlying snapshot.</param>
    /// <param name="unreadableProcessCount">How many processes could not be read (non-negative).</param>
    /// <param name="timestampNanoseconds">Monotonic timestamp of the sample in nanoseconds.</param>
    public ProcessSample(
        IReadOnlyList<SampledProcess> processes,
        IReadOnlyList<ProcessRecord> allProcesses,
        int unreadableProcessCount,
        long timestampNanoseconds)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(allProcesses);
        ArgumentOutOfRangeException.ThrowIfNegative(unreadableProcessCount);

        Processes = processes;
        AllProcesses = allProcesses;
        UnreadableProcessCount = unreadableProcessCount;
        TimestampNanoseconds = timestampNanoseconds;
    }

    /// <summary>Readable processes in this sample, each with its deltas and rates when available.</summary>
    public IReadOnlyList<SampledProcess> Processes { get; }

    /// <summary>All surviving processes (readable and unreadable) from this sample's snapshot.</summary>
    public IReadOnlyList<ProcessRecord> AllProcesses { get; }

    /// <summary>How many processes could not be read in this sample.</summary>
    public int UnreadableProcessCount { get; }

    /// <summary>Monotonic clock timestamp of this sample in nanoseconds.</summary>
    public long TimestampNanoseconds { get; }
}

/// <summary>Samples readable processes over a monotonic clock (excluding sleep) and computes per-interval deltas and rates keyed by <see cref="ProcessIdentity"/> (docs/spec/process.md, "Sampling").</summary>
public sealed class ProcessSampler
{
    private const double NanosecondsPerSecond = 1_000_000_000.0;

    private readonly ProcessReader _reader;
    private readonly IMonotonicClock _clock;
    private Dictionary<ProcessIdentity, ProcessCounters> _previousByIdentity = new();
    private long? _previousTimestampNanoseconds;

    /// <summary>Creates a sampler using a default <see cref="ProcessReader"/> and the system unbiased interrupt-time clock.</summary>
    public ProcessSampler()
        : this(new ProcessReader(), new SystemMonotonicClock())
    {
    }

    /// <summary>Creates a sampler using the given <paramref name="reader"/> and the system unbiased interrupt-time clock.</summary>
    /// <param name="reader">The process reader to take snapshots from.</param>
    public ProcessSampler(ProcessReader reader)
        : this(reader, new SystemMonotonicClock())
    {
    }

    internal ProcessSampler(ProcessReader reader, IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(clock);

        _reader = reader;
        _clock = clock;
    }

    /// <summary>Takes a process snapshot and computes deltas and rates against the immediately preceding sample.</summary>
    /// <returns>The sample.</returns>
    public ProcessSample Sample()
    {
        long nowNanoseconds = _clock.NowNanoseconds();
        ProcessSnapshot snapshot = _reader.ReadSnapshot();

        long? intervalNanoseconds = _previousTimestampNanoseconds is long prevTimestamp && nowNanoseconds > prevTimestamp
            ? nowNanoseconds - prevTimestamp
            : null;

        var nextByIdentity = new Dictionary<ProcessIdentity, ProcessCounters>(snapshot.Processes.Count);
        var sampledList = new List<SampledProcess>(snapshot.Processes.Count);

        foreach (ProcessRecord record in snapshot.Processes)
        {
            if (!record.IsReadable || record.Usage is null)
            {
                continue;
            }

            ProcessCounters current = record.Usage;
            nextByIdentity[record.Identity] = current;

            if (intervalNanoseconds is long dtNs &&
                _previousByIdentity.TryGetValue(record.Identity, out ProcessCounters? previous) &&
                !HasAnyCounterDecreased(previous, current))
            {
                long cpuDelta = current.CpuTimeNanoseconds - previous.CpuTimeNanoseconds;
                long readDelta = current.BytesRead - previous.BytesRead;
                long writeDelta = current.BytesWritten - previous.BytesWritten;

                long? idleDelta = current.IdleWakeups is long curIdle && previous.IdleWakeups is long prevIdle
                    ? curIdle - prevIdle
                    : null;
                long? intDelta = current.InterruptWakeups is long curInt && previous.InterruptWakeups is long prevInt
                    ? curInt - prevInt
                    : null;
                long? energyDelta = current.EnergyNanojoules is long curEnergy && previous.EnergyNanojoules is long prevEnergy
                    ? curEnergy - prevEnergy
                    : null;

                var deltas = new UsageDeltas(
                    intervalNanoseconds: dtNs,
                    cpuTimeNanoseconds: cpuDelta,
                    bytesRead: readDelta,
                    bytesWritten: writeDelta,
                    idleWakeups: idleDelta,
                    interruptWakeups: intDelta,
                    energyNanojoules: energyDelta);

                double seconds = dtNs / NanosecondsPerSecond;
                double cpuPercent = (cpuDelta / (double)dtNs) * 100.0;
                double bytesReadPerSecond = readDelta / seconds;
                double bytesWrittenPerSecond = writeDelta / seconds;
                double? idleWakeupsPerSecond = idleDelta is long id ? id / seconds : null;
                double? interruptWakeupsPerSecond = intDelta is long intr ? intr / seconds : null;

                sampledList.Add(new SampledProcess(
                    process: record,
                    deltas: deltas,
                    cpuPercent: cpuPercent,
                    bytesReadPerSecond: bytesReadPerSecond,
                    bytesWrittenPerSecond: bytesWrittenPerSecond,
                    idleWakeupsPerSecond: idleWakeupsPerSecond,
                    interruptWakeupsPerSecond: interruptWakeupsPerSecond,
                    isZombie: false));
            }
            else
            {
                sampledList.Add(new SampledProcess(
                    process: record,
                    deltas: null,
                    cpuPercent: null,
                    bytesReadPerSecond: null,
                    bytesWrittenPerSecond: null,
                    idleWakeupsPerSecond: null,
                    interruptWakeupsPerSecond: null,
                    isZombie: false));
            }
        }

        _previousByIdentity = nextByIdentity;
        _previousTimestampNanoseconds = nowNanoseconds;

        return new ProcessSample(
            processes: sampledList,
            allProcesses: snapshot.Processes,
            unreadableProcessCount: snapshot.UnreadableProcessCount,
            timestampNanoseconds: nowNanoseconds);
    }

    private static bool HasAnyCounterDecreased(ProcessCounters previous, ProcessCounters current)
    {
        if (current.CpuTimeNanoseconds < previous.CpuTimeNanoseconds ||
            current.BytesRead < previous.BytesRead ||
            current.BytesWritten < previous.BytesWritten)
        {
            return true;
        }

        if (current.IdleWakeups is long curIdle &&
            previous.IdleWakeups is long prevIdle &&
            curIdle < prevIdle)
        {
            return true;
        }

        if (current.InterruptWakeups is long curInt &&
            previous.InterruptWakeups is long prevInt &&
            curInt < prevInt)
        {
            return true;
        }

        if (current.EnergyNanojoules is long curEnergy &&
            previous.EnergyNanojoules is long prevEnergy &&
            curEnergy < prevEnergy)
        {
            return true;
        }

        return false;
    }
}
