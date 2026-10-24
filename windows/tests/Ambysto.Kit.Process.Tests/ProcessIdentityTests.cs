using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed class ProcessIdentityTests
{
    private const string CurrentSid = "S-1-5-21-1000-2000-3000-1001";
    private const long BaseStartTimeTicks = 133_700_000_000_000_000L;

    [TestMethod]
    public void ValueEqualityWithIdenticalProcessIdAndStartTimeTicksAreEqual()
    {
        var first = new ProcessIdentity(4242, BaseStartTimeTicks);
        var second = new ProcessIdentity(4242, BaseStartTimeTicks);

        Assert.AreEqual(4242, first.ProcessId);
        Assert.AreEqual(BaseStartTimeTicks, first.StartTimeTicks);
        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.IsFalse(first != second);
        Assert.IsTrue(first.Equals(second));
        Assert.IsTrue(first.Equals((object)second));
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void ValueEqualityWithDifferentProcessIdOrStartTimeTicksAreNotEqual()
    {
        var baseline = new ProcessIdentity(4242, BaseStartTimeTicks);
        var differentPid = new ProcessIdentity(4243, BaseStartTimeTicks);
        var reusedPid = new ProcessIdentity(4242, BaseStartTimeTicks + 1);

        Assert.AreNotEqual(baseline, differentPid);
        Assert.IsTrue(baseline != differentPid);
        Assert.IsFalse(baseline == differentPid);

        Assert.AreNotEqual(baseline, reusedPid);
        Assert.IsTrue(baseline != reusedPid);
        Assert.IsFalse(baseline == reusedPid);
    }

    [TestMethod]
    public void ConstructorWithNonPositiveProcessIdOrStartTimeTicksThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(0, BaseStartTimeTicks));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(-1, BaseStartTimeTicks));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(int.MinValue, BaseStartTimeTicks));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(1, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ProcessIdentity(1, long.MinValue));
    }

    [TestMethod]
    public void ProcessCountersWithNegativeValuesThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(-1, 0, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, -1, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, 0, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, 0, 0, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, 0, 0, 0, idleWakeups: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, 0, 0, 0, interruptWakeups: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new ProcessCounters(0, 0, 0, 0, energyNanojoules: -1));
    }

    [TestMethod]
    public void ReadMatchingReadableProcessWithValidParentReturnsRecordAndExecutablePath()
    {
        var parentEntry = CreateReadableRawEntry(
            processId: 100,
            inheritedFromProcessId: 4,
            name: "explorer.exe",
            startTimeTicks: BaseStartTimeTicks - 10_000,
            executablePath: @"C:\Windows\explorer.exe");
        var childEntry = CreateReadableRawEntry(
            processId: 200,
            inheritedFromProcessId: 100,
            name: "app.exe",
            startTimeTicks: BaseStartTimeTicks,
            executablePath: @"C:\App\app.exe");

        var source = new SingleLookupSnapshotSource(CurrentSid, parentEntry, childEntry);
        var reader = new ProcessReader(source);
        var identity = new ProcessIdentity(200, BaseStartTimeTicks);

        ProcessRecord? record = reader.Read(identity);

        Assert.IsNotNull(record);
        Assert.AreEqual(identity, record.Identity);
        Assert.AreEqual(100, record.ParentProcessId);
        Assert.IsTrue(record.IsReadable);
        Assert.AreEqual(@"C:\App\app.exe", record.ExecutablePath);
        Assert.AreEqual(@"C:\App\app.exe", reader.ExecutablePath(identity));
    }

    [TestMethod]
    public void ReadReusedParentPidInSingleLookupClearsParentProcessId()
    {
        var youngerParentEntry = CreateReadableRawEntry(
            processId: 100,
            inheritedFromProcessId: 4,
            name: "new-proc.exe",
            startTimeTicks: BaseStartTimeTicks + 500,
            executablePath: @"C:\App\new-proc.exe");
        var childEntry = CreateReadableRawEntry(
            processId: 200,
            inheritedFromProcessId: 100,
            name: "app.exe",
            startTimeTicks: BaseStartTimeTicks,
            executablePath: @"C:\App\app.exe");

        var source = new SingleLookupSnapshotSource(CurrentSid, youngerParentEntry, childEntry);
        var reader = new ProcessReader(source);
        var identity = new ProcessIdentity(200, BaseStartTimeTicks);

        ProcessRecord? record = reader.Read(identity);

        Assert.IsNotNull(record);
        Assert.IsNull(record.ParentProcessId);
    }

    [TestMethod]
    public void ReadMissingExitedReusedOrUnreadableProcessReturnsNull()
    {
        var identity = new ProcessIdentity(200, BaseStartTimeTicks);

        // 1. Missing from snapshot source
        var emptyReader = new ProcessReader(new SingleLookupSnapshotSource(CurrentSid));
        Assert.IsNull(emptyReader.Read(identity));
        Assert.IsNull(emptyReader.ExecutablePath(identity));

        // 2. Snapshot start time differs (PID reused)
        var reusedSnapshotEntry = CreateReadableRawEntry(
            processId: 200,
            inheritedFromProcessId: 0,
            name: "reused.exe",
            startTimeTicks: BaseStartTimeTicks + 1,
            executablePath: @"C:\App\reused.exe");
        var reusedReader = new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, reusedSnapshotEntry));
        Assert.IsNull(reusedReader.Read(identity));
        Assert.IsNull(reusedReader.ExecutablePath(identity));

        // 3. NumberOfThreads == 0 (terminated in snapshot)
        var zeroThreadsEntry = CreateReadableRawEntry(
            processId: 200,
            inheritedFromProcessId: 0,
            name: "zombie.exe",
            startTimeTicks: BaseStartTimeTicks,
            executablePath: @"C:\App\zombie.exe") with
        {
            NumberOfThreads = 0,
        };
        var zeroThreadsReader = new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, zeroThreadsEntry));
        Assert.IsNull(zeroThreadsReader.Read(identity));

        // 4. Handle open reported ExitedOrMissing
        var exitedHandleEntry = CreateReadableRawEntry(
            processId: 200,
            inheritedFromProcessId: 0,
            name: "exited.exe",
            startTimeTicks: BaseStartTimeTicks,
            executablePath: @"C:\App\exited.exe");
        exitedHandleEntry = exitedHandleEntry with
        {
            HandleResult = exitedHandleEntry.HandleResult with { ExitedOrMissing = true },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, exitedHandleEntry)).Read(identity));

        // 5. Handle exit time non-zero before or after metadata read
        var exitedBeforeEntry = exitedHandleEntry with
        {
            HandleResult = CreateReadableRawEntry(200, 0, "a.exe", BaseStartTimeTicks, @"C:\a.exe").HandleResult with
            {
                HandleExitTimeBeforeTicks = BaseStartTimeTicks + 100,
            },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, exitedBeforeEntry)).Read(identity));

        var exitedAfterEntry = exitedHandleEntry with
        {
            HandleResult = CreateReadableRawEntry(200, 0, "a.exe", BaseStartTimeTicks, @"C:\a.exe").HandleResult with
            {
                HandleExitTimeAfterTicks = BaseStartTimeTicks + 100,
            },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, exitedAfterEntry)).Read(identity));

        // 6. Pre-read or post-read creation time mismatch
        var mismatchBeforeEntry = exitedHandleEntry with
        {
            HandleResult = CreateReadableRawEntry(200, 0, "a.exe", BaseStartTimeTicks, @"C:\a.exe").HandleResult with
            {
                HandleStartTimeBeforeTicks = BaseStartTimeTicks + 1,
            },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, mismatchBeforeEntry)).Read(identity));

        var mismatchAfterEntry = exitedHandleEntry with
        {
            HandleResult = CreateReadableRawEntry(200, 0, "a.exe", BaseStartTimeTicks, @"C:\a.exe").HandleResult with
            {
                HandleStartTimeAfterTicks = BaseStartTimeTicks + 1,
            },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, mismatchAfterEntry)).Read(identity));

        // 7. Unreadable (elevated or other user or access denied) returns null on single-process Read
        var elevatedEntry = exitedHandleEntry with
        {
            HandleResult = CreateReadableRawEntry(200, 0, "a.exe", BaseStartTimeTicks, @"C:\a.exe").HandleResult with
            {
                IsElevated = true,
            },
        };
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, elevatedEntry)).Read(identity));
        Assert.IsNull(new ProcessReader(new SingleLookupSnapshotSource(CurrentSid, elevatedEntry)).ExecutablePath(identity));
    }

    [TestMethod]
    public void RealProcessCurrentProcessAndExitedChildWithOpenHandleVerifiesIdentityAndLiveness()
    {
        var reader = new ProcessReader();
        using var currentProcess = Process.GetCurrentProcess();
        long currentStartTicks = currentProcess.StartTime.ToFileTimeUtc();
        var currentIdentity = new ProcessIdentity(Environment.ProcessId, currentStartTicks);

        ProcessRecord? selfRecord = reader.Read(currentIdentity);
        Assert.IsNotNull(selfRecord);
        Assert.AreEqual(currentIdentity, selfRecord.Identity);
        Assert.IsTrue(selfRecord.IsReadable);
        Assert.IsNotNull(selfRecord.ExecutablePath);
        Assert.AreEqual(selfRecord.ExecutablePath, reader.ExecutablePath(currentIdentity));

        // Synthetic reused-PID identity for the current process must return null
        var mismatchedIdentity = new ProcessIdentity(Environment.ProcessId, currentStartTicks + 1);
        Assert.IsNull(reader.Read(mismatchedIdentity));
        Assert.IsNull(reader.ExecutablePath(mismatchedIdentity));

        // Spawn a short-lived child, wait for it to exit while holding its Process handle open,
        // and verify it is excluded from ReadSnapshot() and Read(identity).
        string systemCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var psi = new ProcessStartInfo(systemCmd, "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process? child = Process.Start(psi);
        Assert.IsNotNull(child);
        int childPid = child.Id;
        long childStartTicks = child.StartTime.ToFileTimeUtc();
        var childIdentity = new ProcessIdentity(childPid, childStartTicks);

        child.WaitForExit();
        // Handle to child is still open here because 'using Process? child' has not disposed yet.
        Assert.IsNull(reader.Read(childIdentity));
        Assert.IsNull(reader.ExecutablePath(childIdentity));

        ProcessSnapshot snapshot = reader.ReadSnapshot();
        foreach (ProcessRecord proc in snapshot.Processes)
        {
            Assert.AreNotEqual(childIdentity, proc.Identity);
        }
    }

    private static RawProcessEntry CreateReadableRawEntry(
        int processId,
        int inheritedFromProcessId,
        string name,
        long startTimeTicks,
        string executablePath)
    {
        var counters = new ProcessCounters(
            memoryBytes: 1024 * 1024,
            cpuTimeNanoseconds: 500_000_000,
            bytesRead: 1024,
            bytesWritten: 2048);

        var handleResult = new RawProcessHandleResult(
            ExitedOrMissing: false,
            AccessDenied: false,
            HandleStartTimeBeforeTicks: startTimeTicks,
            HandleExitTimeBeforeTicks: 0,
            OwnerSid: CurrentSid,
            IsElevated: false,
            IsWow64: false,
            ExecutablePath: executablePath,
            WorkingDirectory: @"C:\App\",
            CommandLine: $"\"{executablePath}\" --run",
            HandleStartTimeAfterTicks: startTimeTicks,
            HandleExitTimeAfterTicks: 0);

        return new RawProcessEntry(
            ProcessId: processId,
            InheritedFromProcessId: inheritedFromProcessId,
            Name: name,
            SessionId: 1,
            NumberOfThreads: 4,
            SnapshotStartTimeTicks: startTimeTicks,
            Counters: counters,
            HandleResult: handleResult);
    }

    private sealed class SingleLookupSnapshotSource : IProcessSnapshotSource
    {
        private readonly IReadOnlyList<RawProcessEntry> _entries;

        public SingleLookupSnapshotSource(string currentUserSid, params RawProcessEntry[] entries)
        {
            CurrentUserSid = currentUserSid;
            _entries = entries;
        }

        public string CurrentUserSid { get; }

        public IReadOnlyList<RawProcessEntry> ReadEntries() => _entries;

        public RawProcessEntry? ReadEntry(int processId)
        {
            foreach (RawProcessEntry entry in _entries)
            {
                if (entry.ProcessId == processId)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
