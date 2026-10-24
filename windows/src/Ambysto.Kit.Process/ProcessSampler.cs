using System;
using System.Collections.Generic;

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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    /// <summary>The readable process record.</summary>
    public ProcessRecord Process { get; } = null!;

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
        throw new NotImplementedException();
    }

    /// <summary>Readable processes in this sample, each with its deltas and rates when available.</summary>
    public IReadOnlyList<SampledProcess> Processes { get; } = Array.Empty<SampledProcess>();

    /// <summary>All surviving processes (readable and unreadable) from this sample's snapshot.</summary>
    public IReadOnlyList<ProcessRecord> AllProcesses { get; } = Array.Empty<ProcessRecord>();

    /// <summary>How many processes could not be read in this sample.</summary>
    public int UnreadableProcessCount { get; }

    /// <summary>Monotonic clock timestamp of this sample in nanoseconds.</summary>
    public long TimestampNanoseconds { get; }
}

/// <summary>Samples readable processes over a monotonic clock (excluding sleep) and computes per-interval deltas and rates keyed by <see cref="ProcessIdentity"/> (docs/spec/process.md, "Sampling").</summary>
public sealed class ProcessSampler
{
    /// <summary>Creates a sampler using a default <see cref="ProcessReader"/> and the system unbiased interrupt-time clock.</summary>
    public ProcessSampler()
    {
        throw new NotImplementedException();
    }

    /// <summary>Creates a sampler using the given <paramref name="reader"/> and the system unbiased interrupt-time clock.</summary>
    /// <param name="reader">The process reader to take snapshots from.</param>
    public ProcessSampler(ProcessReader reader)
    {
        throw new NotImplementedException();
    }

    internal ProcessSampler(ProcessReader reader, IMonotonicClock clock)
    {
        throw new NotImplementedException();
    }

    /// <summary>Takes a process snapshot and computes deltas and rates against the immediately preceding sample.</summary>
    /// <returns>The sample.</returns>
    public ProcessSample Sample()
    {
        throw new NotImplementedException();
    }
}
