using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed partial class ProcessSamplerTests
{
    private const string CurrentUserSid = "S-1-5-21-1111-2222-3333-1001";
    private const long BaseStartTimeTicks = 133_700_000_000_000_000L;

    [TestMethod]
    public void SampleFirstSampleThenSecondSampleComputesDeltasAndRates()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(10_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        source.SetEntries(
            CreateReadableEntry(
                processId: 500,
                startTimeTicks: BaseStartTimeTicks,
                memoryBytes: 50 * 1024 * 1024L,
                cpuTimeNanoseconds: 100_000_000L,
                bytesRead: 1_000L,
                bytesWritten: 2_000L));

        ProcessSample first = sampler.Sample();
        Assert.AreEqual(10_000_000_000L, first.TimestampNanoseconds);
        Assert.AreEqual(0, first.UnreadableProcessCount);
        Assert.AreEqual(1, first.Processes.Count);
        Assert.AreEqual(1, first.AllProcesses.Count);

        SampledProcess firstProc = first.Processes[0];
        Assert.IsNull(firstProc.Deltas);
        Assert.IsNull(firstProc.CpuPercent);
        Assert.IsNull(firstProc.BytesReadPerSecond);
        Assert.IsNull(firstProc.BytesWrittenPerSecond);
        Assert.IsNull(firstProc.IdleWakeupsPerSecond);
        Assert.IsNull(firstProc.InterruptWakeupsPerSecond);
        Assert.IsFalse(firstProc.IsZombie);
        Assert.AreEqual(50 * 1024 * 1024L, firstProc.Process.Usage!.MemoryBytes);

        // Advance 1 second (1_000_000_000 ns) with +500_000_000 ns CPU (50% of 1 core), +4096 read, +2048 written
        clock.SetNow(11_000_000_000L);
        source.SetEntries(
            CreateReadableEntry(
                processId: 500,
                startTimeTicks: BaseStartTimeTicks,
                memoryBytes: 52 * 1024 * 1024L,
                cpuTimeNanoseconds: 600_000_000L,
                bytesRead: 5_096L,
                bytesWritten: 4_048L));

        ProcessSample second = sampler.Sample();
        Assert.AreEqual(11_000_000_000L, second.TimestampNanoseconds);
        SampledProcess secondProc = second.Processes[0];

        Assert.IsNotNull(secondProc.Deltas);
        Assert.AreEqual(1_000_000_000L, secondProc.Deltas.IntervalNanoseconds);
        Assert.AreEqual(500_000_000L, secondProc.Deltas.CpuTimeNanoseconds);
        Assert.AreEqual(4_096L, secondProc.Deltas.BytesRead);
        Assert.AreEqual(2_048L, secondProc.Deltas.BytesWritten);
        Assert.IsNull(secondProc.Deltas.IdleWakeups);
        Assert.IsNull(secondProc.Deltas.InterruptWakeups);
        Assert.IsNull(secondProc.Deltas.EnergyNanojoules);

        Assert.IsNotNull(secondProc.CpuPercent);
        Assert.AreEqual(50.0, secondProc.CpuPercent.Value, 1e-9);
        Assert.IsNotNull(secondProc.BytesReadPerSecond);
        Assert.AreEqual(4096.0, secondProc.BytesReadPerSecond.Value, 1e-9);
        Assert.IsNotNull(secondProc.BytesWrittenPerSecond);
        Assert.AreEqual(2048.0, secondProc.BytesWrittenPerSecond.Value, 1e-9);
        Assert.IsNull(secondProc.IdleWakeupsPerSecond);
        Assert.IsNull(secondProc.InterruptWakeupsPerSecond);
        Assert.IsFalse(secondProc.IsZombie);
    }

    [TestMethod]
    public void SampleMultiCoreCpuUsageExceedsOneHundredPercent()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(1_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        source.SetEntries(CreateReadableEntry(500, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 0, bytesRead: 0, bytesWritten: 0));
        _ = sampler.Sample();

        clock.Advance(1_000_000_000L);
        source.SetEntries(CreateReadableEntry(500, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 2_000_000_000L, bytesRead: 0, bytesWritten: 0));
        ProcessSample second = sampler.Sample();

        Assert.IsNotNull(second.Processes[0].CpuPercent);
        Assert.AreEqual(200.0, second.Processes[0].CpuPercent!.Value, 1e-9);
    }

    [TestMethod]
    public void SamplePidReuseAndExitedProcessEvictsOldStateAndStartsFresh()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(1_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        source.SetEntries(
            CreateReadableEntry(500, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 500_000_000L, bytesRead: 1000, bytesWritten: 1000),
            CreateReadableEntry(600, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 500_000_000L, bytesRead: 1000, bytesWritten: 1000));
        _ = sampler.Sample();

        // Sample 2: PID 500 is reused with a new StartTimeTicks; PID 600 exited
        clock.Advance(1_000_000_000L);
        source.SetEntries(
            CreateReadableEntry(500, BaseStartTimeTicks + 50, 1024, cpuTimeNanoseconds: 100_000_000L, bytesRead: 200, bytesWritten: 200));

        ProcessSample second = sampler.Sample();
        Assert.AreEqual(1, second.Processes.Count);
        Assert.AreEqual(new ProcessIdentity(500, BaseStartTimeTicks + 50), second.Processes[0].Process.Identity);
        Assert.IsNull(second.Processes[0].Deltas);
        Assert.IsNull(second.Processes[0].CpuPercent);

        // Sample 3: if original identities reappear, their prior state was evicted so they have no deltas
        clock.Advance(1_000_000_000L);
        source.SetEntries(
            CreateReadableEntry(500, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 900_000_000L, bytesRead: 2000, bytesWritten: 2000),
            CreateReadableEntry(600, BaseStartTimeTicks, 1024, cpuTimeNanoseconds: 900_000_000L, bytesRead: 2000, bytesWritten: 2000));

        ProcessSample third = sampler.Sample();
        Assert.AreEqual(2, third.Processes.Count);
        Assert.IsNull(third.Processes[0].Deltas);
        Assert.IsNull(third.Processes[1].Deltas);
    }

    [TestMethod]
    public void SampleUnreadableProcessesOmittedFromSampledProcessesButIncludedInAllProcessesAndCount()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(1_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        RawProcessEntry readable = CreateReadableEntry(500, BaseStartTimeTicks, 1024, 100, 10, 10);
        RawProcessEntry unreadableElevated = readable with
        {
            ProcessId = 600,
            Name = "elevated.exe",
            HandleResult = readable.HandleResult with { IsElevated = true },
        };

        source.SetEntries(readable, unreadableElevated);
        ProcessSample sample = sampler.Sample();

        Assert.AreEqual(1, sample.UnreadableProcessCount);
        Assert.AreEqual(1, sample.Processes.Count);
        Assert.AreEqual(500, sample.Processes[0].Process.Identity.ProcessId);
        Assert.AreEqual(2, sample.AllProcesses.Count);
    }

    [TestMethod]
    public void SampleZeroOrNegativeIntervalSuppressesDeltasAndRates()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(5_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        source.SetEntries(CreateReadableEntry(500, BaseStartTimeTicks, 1024, 100_000_000L, 100, 100));
        _ = sampler.Sample();

        // Zero interval (same timestamp)
        source.SetEntries(CreateReadableEntry(500, BaseStartTimeTicks, 1024, 200_000_000L, 200, 200));
        ProcessSample zeroIntervalSample = sampler.Sample();
        Assert.IsNull(zeroIntervalSample.Processes[0].Deltas);
        Assert.IsNull(zeroIntervalSample.Processes[0].CpuPercent);
        Assert.IsNull(zeroIntervalSample.Processes[0].BytesReadPerSecond);
        Assert.IsNull(zeroIntervalSample.Processes[0].BytesWrittenPerSecond);

        // Negative interval
        clock.SetNow(4_000_000_000L);
        source.SetEntries(CreateReadableEntry(500, BaseStartTimeTicks, 1024, 300_000_000L, 300, 300));
        ProcessSample negativeIntervalSample = sampler.Sample();
        Assert.IsNull(negativeIntervalSample.Processes[0].Deltas);
        Assert.IsNull(negativeIntervalSample.Processes[0].CpuPercent);
    }

    [TestMethod]
    public void SampleDecreasingAnySingleCounterSuppressesAllDeltasAndRatesForThatSample()
    {
        // Test each counter individually decreasing while the others increase (all-or-nothing rule).
        var baseline = new ProcessCounters(
            memoryBytes: 1024,
            cpuTimeNanoseconds: 500_000_000L,
            bytesRead: 5_000L,
            bytesWritten: 5_000L,
            idleWakeups: 100L,
            interruptWakeups: 50L,
            energyNanojoules: 1_000_000L);

        ProcessCounters[] decreasedVariants =
        [
            new ProcessCounters(1024, 499_999_999L, 6_000L, 6_000L, 110L, 60L, 1_100_000L), // CPU decreased
            new ProcessCounters(1024, 600_000_000L, 4_999L, 6_000L, 110L, 60L, 1_100_000L), // BytesRead decreased
            new ProcessCounters(1024, 600_000_000L, 6_000L, 4_999L, 110L, 60L, 1_100_000L), // BytesWritten decreased
            new ProcessCounters(1024, 600_000_000L, 6_000L, 6_000L, 99L, 60L, 1_100_000L),  // IdleWakeups decreased
            new ProcessCounters(1024, 600_000_000L, 6_000L, 6_000L, 110L, 49L, 1_100_000L), // InterruptWakeups decreased
            new ProcessCounters(1024, 600_000_000L, 6_000L, 6_000L, 110L, 60L, 999_999L),   // EnergyNanojoules decreased
        ];

        foreach (ProcessCounters decreased in decreasedVariants)
        {
            var source = new MutableSnapshotSource(CurrentUserSid);
            var clock = new FakeMonotonicClock(1_000_000_000L);
            var sampler = new ProcessSampler(new ProcessReader(source), clock);

            source.SetEntries(CreateReadableEntryWithCounters(500, BaseStartTimeTicks, baseline));
            _ = sampler.Sample();

            clock.Advance(1_000_000_000L);
            source.SetEntries(CreateReadableEntryWithCounters(500, BaseStartTimeTicks, decreased));
            ProcessSample second = sampler.Sample();

            SampledProcess proc = second.Processes[0];
            Assert.IsNull(proc.Deltas);
            Assert.IsNull(proc.CpuPercent);
            Assert.IsNull(proc.BytesReadPerSecond);
            Assert.IsNull(proc.BytesWrittenPerSecond);
            Assert.IsNull(proc.IdleWakeupsPerSecond);
            Assert.IsNull(proc.InterruptWakeupsPerSecond);

            // Sample 3: counters advance from the new baseline ('decreased') and produce deltas again
            clock.Advance(1_000_000_000L);
            var recovered = new ProcessCounters(
                memoryBytes: 1024,
                cpuTimeNanoseconds: decreased.CpuTimeNanoseconds + 100_000_000L,
                bytesRead: decreased.BytesRead + 1_000L,
                bytesWritten: decreased.BytesWritten + 500L,
                idleWakeups: decreased.IdleWakeups + 5L,
                interruptWakeups: decreased.InterruptWakeups + 5L,
                energyNanojoules: decreased.EnergyNanojoules + 10_000L);
            source.SetEntries(CreateReadableEntryWithCounters(500, BaseStartTimeTicks, recovered));

            ProcessSample third = sampler.Sample();
            SampledProcess thirdProc = third.Processes[0];
            Assert.IsNotNull(thirdProc.Deltas);
            Assert.AreEqual(100_000_000L, thirdProc.Deltas.CpuTimeNanoseconds);
            Assert.AreEqual(1_000L, thirdProc.Deltas.BytesRead);
            Assert.AreEqual(500L, thirdProc.Deltas.BytesWritten);
            Assert.AreEqual(10.0, thirdProc.CpuPercent!.Value, 1e-9);
        }
    }

    [TestMethod]
    public void SampleOptionalWakeupsAndZeroEnergyInSeamPreservesZeroEnergyAndComputesWakeupRates()
    {
        var source = new MutableSnapshotSource(CurrentUserSid);
        var clock = new FakeMonotonicClock(1_000_000_000L);
        var sampler = new ProcessSampler(new ProcessReader(source), clock);

        source.SetEntries(CreateReadableEntryWithCounters(
            500,
            BaseStartTimeTicks,
            new ProcessCounters(1024, 100_000_000L, 1000, 1000, idleWakeups: 10, interruptWakeups: 20, energyNanojoules: 0)));
        _ = sampler.Sample();

        clock.Advance(1_000_000_000L);
        source.SetEntries(CreateReadableEntryWithCounters(
            500,
            BaseStartTimeTicks,
            new ProcessCounters(1024, 200_000_000L, 1000, 1000, idleWakeups: 35, interruptWakeups: 70, energyNanojoules: 0)));

        ProcessSample second = sampler.Sample();
        SampledProcess proc = second.Processes[0];

        Assert.IsNotNull(proc.Deltas);
        Assert.AreEqual(25L, proc.Deltas.IdleWakeups);
        Assert.AreEqual(50L, proc.Deltas.InterruptWakeups);
        Assert.AreEqual(0L, proc.Deltas.EnergyNanojoules);
        Assert.IsNotNull(proc.IdleWakeupsPerSecond);
        Assert.AreEqual(25.0, proc.IdleWakeupsPerSecond.Value, 1e-9);
        Assert.IsNotNull(proc.InterruptWakeupsPerSecond);
        Assert.AreEqual(50.0, proc.InterruptWakeupsPerSecond.Value, 1e-9);
    }

    [TestMethod]
    public void RealProcessBusyLoopAndFileIoHelperReportsPositiveCpuAndIoDeltasAndMatchesUnbiasedInterruptTime()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"ambysto-kit-proc-io-{Guid.NewGuid():N}.bin");
        string psExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");

        string script =
            $"$p = '{tempFile.Replace("'", "''", StringComparison.Ordinal)}'; " +
            "$b = New-Object byte[] 65536; " +
            "while ($true) { " +
            "[System.IO.File]::WriteAllBytes($p, $b); " +
            "$_ = [System.IO.File]::ReadAllBytes($p); " +
            "$s = 0; for ($i = 0; $i -lt 50000; $i++) { $s += $i } " +
            "}";

        var psi = new ProcessStartInfo(psExe, $"-NoProfile -NonInteractive -Command \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process? helper = Process.Start(psi);
        Assert.IsNotNull(helper);
        try
        {
            var reader = new ProcessReader();
            // Wait until the helper has started its loop and accumulated both CPU and I/O bytes.
            var warmupWatch = Stopwatch.StartNew();
            while (warmupWatch.ElapsedMilliseconds < 10_000)
            {
                ProcessSnapshot snap = reader.ReadSnapshot();
                ProcessRecord? rec = snap.Processes.SingleOrDefault(p => p.Identity.ProcessId == helper.Id);
                if (rec?.Usage is { CpuTimeNanoseconds: > 0, BytesWritten: >= 65_536, BytesRead: >= 65_536 })
                {
                    break;
                }

                Thread.Sleep(50);
            }

            var sampler = new ProcessSampler();
            Assert.IsTrue(QueryUnbiasedInterruptTime(out ulong ticksBeforeFirst));
            ProcessSample first = sampler.Sample();
            Assert.IsTrue(QueryUnbiasedInterruptTime(out ulong ticksAfterFirst));

            Thread.Sleep(1_000);

            Assert.IsTrue(QueryUnbiasedInterruptTime(out ulong ticksBeforeSecond));
            ProcessSample second = sampler.Sample();
            Assert.IsTrue(QueryUnbiasedInterruptTime(out ulong ticksAfterSecond));

            SampledProcess? helperFirst = first.Processes.SingleOrDefault(p => p.Process.Identity.ProcessId == helper.Id);
            SampledProcess? helperSecond = second.Processes.SingleOrDefault(p => p.Process.Identity.ProcessId == helper.Id);
            Assert.IsNotNull(helperFirst);
            Assert.IsNull(helperFirst.Deltas);

            Assert.IsNotNull(helperSecond);
            Assert.IsNotNull(helperSecond.Deltas);
            Assert.IsTrue(helperSecond.Deltas.CpuTimeNanoseconds > 0);
            Assert.IsNotNull(helperSecond.CpuPercent);
            Assert.IsTrue(helperSecond.CpuPercent.Value > 0.0);
            Assert.IsTrue(helperSecond.Deltas.BytesWritten >= 65_536);
            Assert.IsTrue(helperSecond.Deltas.BytesRead >= 65_536);
            Assert.IsNotNull(helperSecond.BytesWrittenPerSecond);
            Assert.IsTrue(helperSecond.BytesWrittenPerSecond.Value > 0.0);
            Assert.IsNotNull(helperSecond.BytesReadPerSecond);
            Assert.IsTrue(helperSecond.BytesReadPerSecond.Value > 0.0);

            // Windows never invents 0 for wakeups or energy: must be null on both Usage and Deltas.
            Assert.IsNull(helperSecond.Process.Usage!.IdleWakeups);
            Assert.IsNull(helperSecond.Process.Usage.InterruptWakeups);
            Assert.IsNull(helperSecond.Process.Usage.EnergyNanojoules);
            Assert.IsNull(helperSecond.Deltas.IdleWakeups);
            Assert.IsNull(helperSecond.Deltas.InterruptWakeups);
            Assert.IsNull(helperSecond.Deltas.EnergyNanojoules);
            Assert.IsNull(helperSecond.IdleWakeupsPerSecond);
            Assert.IsNull(helperSecond.InterruptWakeupsPerSecond);

            ulong firstMidTicks = (ticksBeforeFirst + ticksAfterFirst) / 2;
            ulong secondMidTicks = (ticksBeforeSecond + ticksAfterSecond) / 2;
            long unbiasedElapsedNs = checked((long)(secondMidTicks - firstMidTicks) * 100L);
            long sampleIntervalNs = helperSecond.Deltas.IntervalNanoseconds;
            Assert.AreEqual(second.TimestampNanoseconds - first.TimestampNanoseconds, sampleIntervalNs);
            double relativeDiff = Math.Abs(sampleIntervalNs - unbiasedElapsedNs) / (double)unbiasedElapsedNs;
            Assert.IsTrue(
                relativeDiff <= 0.05,
                $"Expected sampleIntervalNs ({sampleIntervalNs}) to be within 5% of QueryUnbiasedInterruptTime ({unbiasedElapsedNs}), diff={relativeDiff:P2}.");
        }
        finally
        {
            try
            {
                helper.Kill(entireProcessTree: true);
                helper.WaitForExit(5_000);
            }
            catch (InvalidOperationException)
            {
            }

            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    private static RawProcessEntry CreateReadableEntry(
        int processId,
        long startTimeTicks,
        long memoryBytes,
        long cpuTimeNanoseconds,
        long bytesRead,
        long bytesWritten)
    {
        return CreateReadableEntryWithCounters(
            processId,
            startTimeTicks,
            new ProcessCounters(memoryBytes, cpuTimeNanoseconds, bytesRead, bytesWritten));
    }

    private static RawProcessEntry CreateReadableEntryWithCounters(
        int processId,
        long startTimeTicks,
        ProcessCounters counters)
    {
        return new RawProcessEntry(
            ProcessId: processId,
            InheritedFromProcessId: 0,
            Name: "sampled.exe",
            SessionId: 1,
            NumberOfThreads: 4,
            SnapshotStartTimeTicks: startTimeTicks,
            Counters: counters,
            HandleResult: new RawProcessHandleResult(
                ExitedOrMissing: false,
                AccessDenied: false,
                HandleStartTimeBeforeTicks: startTimeTicks,
                HandleExitTimeBeforeTicks: 0,
                OwnerSid: CurrentUserSid,
                IsElevated: false,
                IsWow64: false,
                ExecutablePath: @"C:\App\sampled.exe",
                WorkingDirectory: @"C:\App\",
                CommandLine: "sampled.exe",
                HandleStartTimeAfterTicks: startTimeTicks,
                HandleExitTimeAfterTicks: 0));
    }

    private sealed class MutableSnapshotSource : IProcessSnapshotSource
    {
        private IReadOnlyList<RawProcessEntry> _entries = Array.Empty<RawProcessEntry>();

        public MutableSnapshotSource(string currentUserSid)
        {
            CurrentUserSid = currentUserSid;
        }

        public string CurrentUserSid { get; }

        public void SetEntries(params RawProcessEntry[] entries) => _entries = entries;

        public IReadOnlyList<RawProcessEntry> ReadEntries() => _entries;

        public RawProcessEntry? ReadEntry(int processId) =>
            _entries.FirstOrDefault(e => e.ProcessId == processId);
    }

    private sealed class FakeMonotonicClock : IMonotonicClock
    {
        private long _nowNs;

        public FakeMonotonicClock(long initialNanoseconds)
        {
            _nowNs = initialNanoseconds;
        }

        public void SetNow(long nanoseconds) => _nowNs = nanoseconds;

        public void Advance(long deltaNanoseconds) => _nowNs += deltaNanoseconds;

        public long NowNanoseconds() => _nowNs;
    }
}
