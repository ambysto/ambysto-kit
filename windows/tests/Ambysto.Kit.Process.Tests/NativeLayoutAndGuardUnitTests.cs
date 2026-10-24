using System;
using System.Runtime.InteropServices;
using Ambysto.Kit.Processes.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed class NativeLayoutAndGuardUnitTests
{
    private const string ValidSid = "S-1-5-21-1000-2000-3000-1001";
    private const long ValidTicks = 133_700_000_000_000_000L;
    private static readonly string[] SingleArg = ["app.exe"];

    [TestMethod]
    public void SystemProcessInformationStructSizeAndFieldOffsetsMatchWinternlLayout()
    {
        Assert.AreEqual(256, Marshal.SizeOf<ProcessNativeMethods.SystemProcessInformation>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.NextEntryOffset)));
        Assert.AreEqual(4, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.NumberOfThreads)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.WorkingSetPrivateSize)));
        Assert.AreEqual(32, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.CreateTime)));
        Assert.AreEqual(40, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.UserTime)));
        Assert.AreEqual(48, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.KernelTime)));
        Assert.AreEqual(56, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.ImageName)));
        Assert.AreEqual(72, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.BasePriority)));
        Assert.AreEqual(80, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.UniqueProcessId)));
        Assert.AreEqual(88, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.InheritedFromUniqueProcessId)));
        Assert.AreEqual(96, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.HandleCount)));
        Assert.AreEqual(100, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.SessionId)));
        Assert.AreEqual(200, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.PrivatePageCount)));
        Assert.AreEqual(232, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.ReadTransferCount)));
        Assert.AreEqual(240, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.WriteTransferCount)));
        Assert.AreEqual(248, (int)Marshal.OffsetOf<ProcessNativeMethods.SystemProcessInformation>(nameof(ProcessNativeMethods.SystemProcessInformation.OtherTransferCount)));
    }

    [TestMethod]
    public void MibTcpRowStructsSizeAndFieldOffsetsMatchIphlpapiLayout()
    {
        Assert.AreEqual(24, Marshal.SizeOf<ProcessNativeMethods.MibTcpRowOwnerPid>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.State)));
        Assert.AreEqual(4, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.LocalAddr)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.LocalPort)));
        Assert.AreEqual(12, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.RemoteAddr)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.RemotePort)));
        Assert.AreEqual(20, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcpRowOwnerPid>(nameof(ProcessNativeMethods.MibTcpRowOwnerPid.OwningPid)));

        Assert.AreEqual(56, Marshal.SizeOf<ProcessNativeMethods.MibTcp6RowOwnerPid>());
        Assert.AreEqual(0, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.LocalAddr)));
        Assert.AreEqual(16, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.LocalScopeId)));
        Assert.AreEqual(20, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.LocalPort)));
        Assert.AreEqual(24, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.RemoteAddr)));
        Assert.AreEqual(40, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.RemoteScopeId)));
        Assert.AreEqual(44, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.RemotePort)));
        Assert.AreEqual(48, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.State)));
        Assert.AreEqual(52, (int)Marshal.OffsetOf<ProcessNativeMethods.MibTcp6RowOwnerPid>(nameof(ProcessNativeMethods.MibTcp6RowOwnerPid.OwningPid)));
    }

    [TestMethod]
    public void GuardClausesValidateProcessRecordsSnapshotsSocketsAndSamples()
    {
        var identity = new ProcessIdentity(100, ValidTicks);
        var counters = new ProcessCounters(0, 0, 0, 0, 0, 0, 0);

        Assert.AreEqual(0L, counters.MemoryBytes);
        Assert.AreEqual(0L, counters.IdleWakeups);
        Assert.AreEqual(0L, counters.InterruptWakeups);
        Assert.AreEqual(0L, counters.EnergyNanojoules);

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = ProcessRecord.Readable(identity, null, null!, 0, ValidSid, null, null, null, null, counters));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", -1, ValidSid, null, null, null, null, counters));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", 0, null!, null, null, null, null, counters));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", 0, "   ", null, null, null, null, counters));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", 0, ValidSid, null, null, null, null, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", 0, ValidSid, null, null, "app.exe", null, counters));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = ProcessRecord.Readable(identity, null, "app.exe", 0, ValidSid, null, null, null, SingleArg, counters));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = ProcessRecord.Unreadable(identity, null, null!, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = ProcessRecord.Unreadable(identity, null, "sys.exe", -1));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessSnapshot(null!, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessSnapshot(Array.Empty<ProcessRecord>(), -1));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessReader(null!));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ListeningSocket(0, null, null, false, 80, SocketAddressFamily.IPv4, "0.0.0.0", SocketAddressClass.Any));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ListeningSocket(-1, null, null, false, 80, SocketAddressFamily.IPv4, "0.0.0.0", SocketAddressClass.Any));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ListeningSocket(1, null, null, false, 80, SocketAddressFamily.IPv4, null!, SocketAddressClass.Any));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new ListeningSocket(1, null, null, false, 80, SocketAddressFamily.IPv4, "   ", SocketAddressClass.Any));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ListeningSockets(null!));

        var sockets = new ListeningSockets();
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = sockets.ReadForReadableProcesses(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = sockets.ReadAll(null!));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(0, 0, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(-1, 0, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, -1, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, 0, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, 0, 0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, 0, 0, 0, idleWakeups: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, 0, 0, 0, interruptWakeups: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new UsageDeltas(1, 0, 0, 0, energyNanojoules: -1));

        var zeroDeltas = new UsageDeltas(1, 0, 0, 0, idleWakeups: 0, interruptWakeups: 0, energyNanojoules: 0);
        Assert.AreEqual(0L, zeroDeltas.IdleWakeups);
        Assert.AreEqual(0L, zeroDeltas.InterruptWakeups);
        Assert.AreEqual(0L, zeroDeltas.EnergyNanojoules);

        var unreadable = ProcessRecord.Unreadable(identity, null, "sys.exe", 0);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new SampledProcess(null!, null, null, null, null));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new SampledProcess(unreadable, null, null, null, null));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessSample(null!, Array.Empty<ProcessRecord>(), 0, 1));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessSample(Array.Empty<SampledProcess>(), null!, 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessSample(Array.Empty<SampledProcess>(), Array.Empty<ProcessRecord>(), -1, 1));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessSampler(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            _ = new ProcessSampler(new ProcessReader(), null!));

        Assert.IsNull(ProcessReader.NormalizeWorkingDirectory(null));
        Assert.IsNull(ProcessReader.NormalizeWorkingDirectory(string.Empty));
        Assert.IsNull(ProcessReader.NormalizeWorkingDirectory("   "));
        Assert.IsNull(ProcessReader.NormalizeWorkingDirectory(@"\\?\   \"));
        Assert.AreEqual(@"C:\", ProcessReader.NormalizeWorkingDirectory(@"C:\"));
        Assert.AreEqual(@"C:\work", ProcessReader.NormalizeWorkingDirectory(@"C:\work"));
    }

    [TestMethod]
    public void SingleLookupParentEdgeCasesDuplicateSocketOwnersAndTwoSecondSamplingKillBoundaryMutants()
    {
        var handle = new RawProcessHandleResult(
            ExitedOrMissing: false,
            AccessDenied: false,
            HandleStartTimeBeforeTicks: ValidTicks,
            HandleExitTimeBeforeTicks: 0,
            OwnerSid: ValidSid,
            IsElevated: false,
            IsWow64: false,
            ExecutablePath: string.Empty,
            WorkingDirectory: null,
            CommandLine: null,
            HandleStartTimeAfterTicks: ValidTicks,
            HandleExitTimeAfterTicks: 0);

        var counters = new ProcessCounters(4096, 100, 200, 300, 10, 20, 500);

        // 1. Single-lookup Read: empty ExecutablePath -> null; self-parented, negative parent, missing parent, non-surviving parent -> null; same-tick parent -> kept.
        var selfParented = new RawProcessEntry(200, 200, "self.exe", 1, 2, ValidTicks, counters, handle);
        var negParented = new RawProcessEntry(201, -1, "neg.exe", 1, 2, ValidTicks, counters, handle);
        var missingParentChild = new RawProcessEntry(202, 999, "missing.exe", 1, 2, ValidTicks, counters, handle);
        var deadParent = new RawProcessEntry(103, 0, "dead.exe", 1, 0, ValidTicks, counters, handle);
        var deadParentChild = new RawProcessEntry(203, 103, "child-dead.exe", 1, 2, ValidTicks, counters, handle);
        var sameTickParent = new RawProcessEntry(104, 0, "same.exe", 1, 2, ValidTicks, counters, handle);
        var sameTickChild = new RawProcessEntry(204, 104, "child-same.exe", 1, 2, ValidTicks, counters, handle);

        var source = new CustomSnapshotSource(
            ValidSid,
            selfParented,
            negParented,
            missingParentChild,
            deadParent,
            deadParentChild,
            sameTickParent,
            sameTickChild);
        var reader = new ProcessReader(source);

        ProcessRecord? rSelf = reader.Read(new ProcessIdentity(200, ValidTicks));
        Assert.IsNotNull(rSelf);
        Assert.IsNull(rSelf.ParentProcessId);
        Assert.IsNull(rSelf.ExecutablePath);

        Assert.IsNull(reader.Read(new ProcessIdentity(201, ValidTicks))!.ParentProcessId);
        Assert.IsNull(reader.Read(new ProcessIdentity(202, ValidTicks))!.ParentProcessId);
        Assert.IsNull(reader.Read(new ProcessIdentity(203, ValidTicks))!.ParentProcessId);
        Assert.AreEqual(104, reader.Read(new ProcessIdentity(204, ValidTicks))!.ParentProcessId);

        // 2. ListeningSockets duplicate PID in snapshot picks greatest StartTimeTicks.
        var olderOwner = ProcessRecord.Readable(new ProcessIdentity(500, ValidTicks - 10), null, "old.exe", 1, ValidSid, null, null, null, null, counters);
        var newerOwner = ProcessRecord.Readable(new ProcessIdentity(500, ValidTicks + 10), null, "new.exe", 1, ValidSid, null, null, null, null, counters);
        var dupSnapshot = new ProcessSnapshot([newerOwner, olderOwner], 0);
        var tcpRow = new RawTcpListenerRow(SocketAddressFamily.IPv4, [127, 0, 0, 1], 0, 0x5000, 500);
        var sockets = new ListeningSockets(new SingleRowTcpSource(tcpRow)).ReadForReadableProcesses(dupSnapshot);
        Assert.AreEqual(1, sockets.Count);
        Assert.AreEqual(newerOwner.Identity, sockets[0].ProcessIdentity);
        Assert.AreEqual("new.exe", sockets[0].ProcessName);

        // 3. ProcessSampler: unchanged counters (delta == 0) produce zero deltas/rates; 2-second interval divides (not multiplies) by seconds and subtracts non-zero energy.
        var clock = new StepClock(1_000_000_000L);
        var samplerSource = new CustomSnapshotSource(ValidSid, sameTickParent);
        var sampler = new ProcessSampler(new ProcessReader(samplerSource), clock);

        _ = sampler.Sample();

        // Sample 2: 2 seconds later, unchanged counters -> all deltas 0
        clock.Now = 3_000_000_000L;
        ProcessSample unchangedSample = sampler.Sample();
        Assert.IsNotNull(unchangedSample.Processes[0].Deltas);
        Assert.AreEqual(0L, unchangedSample.Processes[0].Deltas!.CpuTimeNanoseconds);
        Assert.AreEqual(0L, unchangedSample.Processes[0].Deltas!.IdleWakeups);
        Assert.AreEqual(0L, unchangedSample.Processes[0].Deltas!.InterruptWakeups);
        Assert.AreEqual(0L, unchangedSample.Processes[0].Deltas!.EnergyNanojoules);

        // Sample 3: 2 seconds later (+2_000_000_000 ns), +400 read, +600 written, +20 idle, +40 interrupt, +250 energy
        clock.Now = 5_000_000_000L;
        var advancedCounters = new ProcessCounters(4096, 500_000_100L, 600, 900, 30, 60, 750);
        samplerSource.Entries = [sameTickParent with { Counters = advancedCounters }];
        ProcessSample twoSecSample = sampler.Sample();
        SampledProcess sp = twoSecSample.Processes[0];
        Assert.IsNotNull(sp.Deltas);
        Assert.AreEqual(250L, sp.Deltas.EnergyNanojoules);
        Assert.AreEqual(200.0, sp.BytesReadPerSecond!.Value, 1e-9);
        Assert.AreEqual(300.0, sp.BytesWrittenPerSecond!.Value, 1e-9);
        Assert.AreEqual(10.0, sp.IdleWakeupsPerSecond!.Value, 1e-9);
        Assert.AreEqual(20.0, sp.InterruptWakeupsPerSecond!.Value, 1e-9);
    }

    private sealed class CustomSnapshotSource : IProcessSnapshotSource
    {
        public CustomSnapshotSource(string currentUserSid, params RawProcessEntry[] entries)
        {
            CurrentUserSid = currentUserSid;
            Entries = entries;
        }

        public string CurrentUserSid { get; }

        public System.Collections.Generic.IReadOnlyList<RawProcessEntry> Entries { get; set; }

        public System.Collections.Generic.IReadOnlyList<RawProcessEntry> ReadEntries() => Entries;

        public RawProcessEntry? ReadEntry(int processId)
        {
            foreach (RawProcessEntry entry in Entries)
            {
                if (entry.ProcessId == processId)
                {
                    return entry;
                }
            }

            return null;
        }
    }

    private sealed class SingleRowTcpSource(RawTcpListenerRow row) : ITcpTableSource
    {
        public System.Collections.Generic.IReadOnlyList<RawTcpListenerRow> ReadListeners() => [row];
    }

    private sealed class StepClock(long now) : IMonotonicClock
    {
        public long Now { get; set; } = now;

        public long NowNanoseconds() => Now;
    }
}
