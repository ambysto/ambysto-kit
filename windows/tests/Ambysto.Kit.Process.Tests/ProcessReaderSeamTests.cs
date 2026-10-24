using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed class ProcessReaderSeamTests
{
    private const string CurrentUserSid = "S-1-5-21-1111-2222-3333-1001";
    private const string SystemSid = "S-1-5-18";
    private const string OtherUserSid = "S-1-5-21-1111-2222-3333-1002";
    private const long BaseTicks = 133_700_000_000_000_000L;
    private static readonly string[] ExpectedWorkerArguments =
    [
        @"C:\Program Files\Demo\worker.exe",
        "--port",
        "8080",
        "hello world",
    ];

    [TestMethod]
    public void ReadSnapshotCurrentUserNonElevated64BitProcessPopulatesAllReadableFieldsAndNormalizesWorkingDirectory()
    {
        var counters = new ProcessCounters(
            memoryBytes: 64L * 1024 * 1024,
            cpuTimeNanoseconds: 750_000_000L,
            bytesRead: 16_384L,
            bytesWritten: 8_192L);

        var entry = new RawProcessEntry(
            ProcessId: 1234,
            InheritedFromProcessId: 0,
            Name: "worker.exe",
            SessionId: 1,
            NumberOfThreads: 8,
            SnapshotStartTimeTicks: BaseTicks,
            Counters: counters,
            HandleResult: new RawProcessHandleResult(
                ExitedOrMissing: false,
                AccessDenied: false,
                HandleStartTimeBeforeTicks: BaseTicks,
                HandleExitTimeBeforeTicks: 0,
                OwnerSid: CurrentUserSid,
                IsElevated: false,
                IsWow64: false,
                ExecutablePath: @"C:\Program Files\Demo\worker.exe",
                WorkingDirectory: @"\\?\C:\Users\alice\work\",
                CommandLine: "\"C:\\Program Files\\Demo\\worker.exe\" --port 8080 \"hello world\"",
                HandleStartTimeAfterTicks: BaseTicks,
                HandleExitTimeAfterTicks: 0));

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, entry));
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(0, snapshot.UnreadableProcessCount);
        Assert.AreEqual(1, snapshot.Processes.Count);

        ProcessRecord record = snapshot.Processes[0];
        Assert.AreEqual(new ProcessIdentity(1234, BaseTicks), record.Identity);
        Assert.IsNull(record.ParentProcessId);
        Assert.AreEqual("worker.exe", record.Name);
        Assert.AreEqual(1, record.SessionId);
        Assert.IsTrue(record.IsReadable);
        Assert.AreEqual(CurrentUserSid, record.OwnerSid);
        Assert.AreEqual(@"C:\Program Files\Demo\worker.exe", record.ExecutablePath);
        Assert.AreEqual(@"C:\Users\alice\work", record.WorkingDirectory);
        Assert.AreEqual("\"C:\\Program Files\\Demo\\worker.exe\" --port 8080 \"hello world\"", record.CommandLine);
        Assert.IsNotNull(record.Arguments);
        CollectionAssert.AreEqual(ExpectedWorkerArguments, record.Arguments.ToArray());

        Assert.IsNotNull(record.Usage);
        Assert.AreEqual(64L * 1024 * 1024, record.Usage.MemoryBytes);
        Assert.AreEqual(750_000_000L, record.Usage.CpuTimeNanoseconds);
        Assert.AreEqual(16_384L, record.Usage.BytesRead);
        Assert.AreEqual(8_192L, record.Usage.BytesWritten);
        Assert.IsNull(record.Usage.IdleWakeups);
        Assert.IsNull(record.Usage.InterruptWakeups);
        Assert.IsNull(record.Usage.EnergyNanojoules);
    }

    [TestMethod]
    public void ReadSnapshotDriveRootWorkingDirectoryPreservesTrailingBackslash()
    {
        RawProcessEntry rootEntry = CreateReadableEntry(100, 0, "root.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with
            {
                WorkingDirectory = @"\\?\C:\",
            },
        };

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, rootEntry));
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(1, snapshot.Processes.Count);
        Assert.AreEqual(@"C:\", snapshot.Processes[0].WorkingDirectory);
    }

    [TestMethod]
    public void ReadSnapshotCurrentUserElevatedProcessIsUnreadableAndCounted()
    {
        RawProcessEntry parentEntry = CreateReadableEntry(100, 0, "explorer.exe", BaseTicks - 1_000);
        RawProcessEntry elevatedChild = CreateReadableEntry(200, 100, "admin-tool.exe", BaseTicks) with
        {
            SessionId = 2,
            HandleResult = CreateReadableHandle(BaseTicks) with
            {
                OwnerSid = CurrentUserSid,
                IsElevated = true,
            },
        };

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, parentEntry, elevatedChild));
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(1, snapshot.UnreadableProcessCount);
        Assert.AreEqual(2, snapshot.Processes.Count);

        ProcessRecord record = snapshot.Processes.Single(p => p.Identity.ProcessId == 200);
        Assert.AreEqual(new ProcessIdentity(200, BaseTicks), record.Identity);
        Assert.AreEqual(100, record.ParentProcessId);
        Assert.AreEqual("admin-tool.exe", record.Name);
        Assert.AreEqual(2, record.SessionId);
        Assert.IsFalse(record.IsReadable);
        Assert.IsNull(record.OwnerSid);
        Assert.IsNull(record.ExecutablePath);
        Assert.IsNull(record.WorkingDirectory);
        Assert.IsNull(record.CommandLine);
        Assert.IsNull(record.Arguments);
        Assert.IsNull(record.Usage);
    }

    [TestMethod]
    public void ReadSnapshotOtherUserSystemAndAccessDeniedProcessesAreUnreadableAndCounted()
    {
        RawProcessEntry systemPid4 = CreateReadableEntry(4, 0, "System", BaseTicks - 50_000) with
        {
            SessionId = 0,
            HandleResult = new RawProcessHandleResult(
                ExitedOrMissing: false,
                AccessDenied: true,
                HandleStartTimeBeforeTicks: 0,
                HandleExitTimeBeforeTicks: 0,
                OwnerSid: null,
                IsElevated: false,
                IsWow64: false,
                ExecutablePath: null,
                WorkingDirectory: null,
                CommandLine: null,
                HandleStartTimeAfterTicks: 0,
                HandleExitTimeAfterTicks: 0),
        };

        RawProcessEntry serviceEntry = CreateReadableEntry(500, 4, "svchost.exe", BaseTicks - 10_000) with
        {
            SessionId = 0,
            HandleResult = CreateReadableHandle(BaseTicks - 10_000) with
            {
                OwnerSid = SystemSid,
            },
        };

        RawProcessEntry otherUserEntry = CreateReadableEntry(600, 500, "other.exe", BaseTicks) with
        {
            SessionId = 3,
            HandleResult = CreateReadableHandle(BaseTicks) with
            {
                OwnerSid = OtherUserSid,
            },
        };

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, systemPid4, serviceEntry, otherUserEntry));
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(3, snapshot.UnreadableProcessCount);
        Assert.AreEqual(3, snapshot.Processes.Count);

        foreach (ProcessRecord record in snapshot.Processes)
        {
            Assert.IsFalse(record.IsReadable);
            Assert.IsNull(record.OwnerSid);
            Assert.IsNull(record.ExecutablePath);
            Assert.IsNull(record.WorkingDirectory);
            Assert.IsNull(record.CommandLine);
            Assert.IsNull(record.Arguments);
            Assert.IsNull(record.Usage);
        }

        ProcessRecord systemRecord = snapshot.Processes.Single(p => p.Identity.ProcessId == 4);
        Assert.IsNull(systemRecord.ParentProcessId);
        Assert.AreEqual("System", systemRecord.Name);

        ProcessRecord serviceRecord = snapshot.Processes.Single(p => p.Identity.ProcessId == 500);
        Assert.AreEqual(4, serviceRecord.ParentProcessId);

        ProcessRecord otherRecord = snapshot.Processes.Single(p => p.Identity.ProcessId == 600);
        Assert.AreEqual(500, otherRecord.ParentProcessId);
    }

    [TestMethod]
    public void ReadSnapshotPidZeroZeroThreadsExitedOrMismatchedStartTimesAreSkippedWithoutCountingUnreadable()
    {
        RawProcessEntry idlePid0 = CreateReadableEntry(0, 0, "System Idle Process", BaseTicks);
        RawProcessEntry nonPositiveStart = CreateReadableEntry(10, 0, "bad-time.exe", 0);
        RawProcessEntry zeroThreads = CreateReadableEntry(20, 0, "terminated.exe", BaseTicks) with
        {
            NumberOfThreads = 0,
        };
        RawProcessEntry exitedOnOpen = CreateReadableEntry(30, 0, "gone.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { ExitedOrMissing = true },
        };
        RawProcessEntry exitedBeforeRead = CreateReadableEntry(40, 0, "exited-before.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { HandleExitTimeBeforeTicks = BaseTicks + 10 },
        };
        RawProcessEntry reusedBeforeRead = CreateReadableEntry(50, 0, "reused-before.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { HandleStartTimeBeforeTicks = BaseTicks + 1 },
        };
        RawProcessEntry exitedDuringRead = CreateReadableEntry(60, 0, "exited-after.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { HandleExitTimeAfterTicks = BaseTicks + 20 },
        };
        RawProcessEntry reusedDuringRead = CreateReadableEntry(70, 0, "reused-after.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { HandleStartTimeAfterTicks = BaseTicks + 1 },
        };
        RawProcessEntry surviving = CreateReadableEntry(80, 0, "alive.exe", BaseTicks);

        var reader = new ProcessReader(new FakeSnapshotSource(
            CurrentUserSid,
            idlePid0,
            nonPositiveStart,
            zeroThreads,
            exitedOnOpen,
            exitedBeforeRead,
            reusedBeforeRead,
            exitedDuringRead,
            reusedDuringRead,
            surviving));

        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(0, snapshot.UnreadableProcessCount);
        Assert.AreEqual(1, snapshot.Processes.Count);
        Assert.AreEqual(80, snapshot.Processes[0].Identity.ProcessId);
    }

    [TestMethod]
    public void ReadSnapshotWow64CurrentUserNonElevatedProcessIsReadableWithNullWorkingDirectory()
    {
        RawProcessEntry wow64Entry = CreateReadableEntry(320, 0, "legacy32.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with
            {
                IsWow64 = true,
                ExecutablePath = @"C:\Windows\SysWOW64\cmd.exe",
                WorkingDirectory = @"C:\ShouldBeIgnoredForWow64\",
                CommandLine = @"C:\Windows\SysWOW64\cmd.exe /c echo hi",
            },
        };

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, wow64Entry));
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.AreEqual(0, snapshot.UnreadableProcessCount);
        Assert.AreEqual(1, snapshot.Processes.Count);

        ProcessRecord record = snapshot.Processes[0];
        Assert.IsTrue(record.IsReadable);
        Assert.AreEqual(CurrentUserSid, record.OwnerSid);
        Assert.AreEqual(@"C:\Windows\SysWOW64\cmd.exe", record.ExecutablePath);
        Assert.IsNull(record.WorkingDirectory);
        Assert.AreEqual(@"C:\Windows\SysWOW64\cmd.exe /c echo hi", record.CommandLine);
        Assert.IsNotNull(record.Arguments);
        Assert.IsNotNull(record.Usage);
    }

    [TestMethod]
    public void ReadSnapshotParentProcessIdValidationEnforcesLivenessAndCreationTimeOrdering()
    {
        RawProcessEntry olderParent = CreateReadableEntry(100, 0, "parent.exe", BaseTicks);
        RawProcessEntry sameTickParent = CreateReadableEntry(101, 0, "same-tick-parent.exe", BaseTicks);
        RawProcessEntry youngerReusedParent = CreateReadableEntry(102, 0, "reused-parent.exe", BaseTicks + 1);
        RawProcessEntry skippedZeroThreadParent = CreateReadableEntry(103, 0, "dead-parent.exe", BaseTicks - 100) with
        {
            NumberOfThreads = 0,
        };

        RawProcessEntry childOfOlder = CreateReadableEntry(200, 100, "child-older.exe", BaseTicks + 10);
        RawProcessEntry childOfSameTick = CreateReadableEntry(201, 101, "child-same.exe", BaseTicks);
        RawProcessEntry childOfReused = CreateReadableEntry(202, 102, "child-reused.exe", BaseTicks);
        RawProcessEntry childOfMissing = CreateReadableEntry(203, 999, "child-missing.exe", BaseTicks);
        RawProcessEntry childOfSkipped = CreateReadableEntry(204, 103, "child-skipped.exe", BaseTicks);
        RawProcessEntry selfParented = CreateReadableEntry(205, 205, "self-parent.exe", BaseTicks);
        RawProcessEntry negativeParent = CreateReadableEntry(206, -5, "neg-parent.exe", BaseTicks);

        var reader = new ProcessReader(new FakeSnapshotSource(
            CurrentUserSid,
            olderParent,
            sameTickParent,
            youngerReusedParent,
            skippedZeroThreadParent,
            childOfOlder,
            childOfSameTick,
            childOfReused,
            childOfMissing,
            childOfSkipped,
            selfParented,
            negativeParent));

        ProcessSnapshot snapshot = reader.ReadSnapshot();
        Dictionary<int, ProcessRecord> byPid = snapshot.Processes.ToDictionary(p => p.Identity.ProcessId);

        Assert.AreEqual(100, byPid[200].ParentProcessId);
        Assert.AreEqual(101, byPid[201].ParentProcessId);
        Assert.IsNull(byPid[202].ParentProcessId);
        Assert.IsNull(byPid[203].ParentProcessId);
        Assert.IsNull(byPid[204].ParentProcessId);
        Assert.IsNull(byPid[205].ParentProcessId);
        Assert.IsNull(byPid[206].ParentProcessId);
    }

    [TestMethod]
    public void ReadSnapshotDuplicateSurvivingProcessIdUsesGreatestStartTimeTicksForParentValidation()
    {
        RawProcessEntry olderDuplicateParent = CreateReadableEntry(100, 0, "old-parent.exe", BaseTicks - 100);
        RawProcessEntry newerDuplicateParent = CreateReadableEntry(100, 0, "new-parent.exe", BaseTicks + 100);
        RawProcessEntry childBetween = CreateReadableEntry(200, 100, "child-between.exe", BaseTicks);
        RawProcessEntry childAfterNewer = CreateReadableEntry(201, 100, "child-after.exe", BaseTicks + 200);

        var reader = new ProcessReader(new FakeSnapshotSource(
            CurrentUserSid,
            olderDuplicateParent,
            newerDuplicateParent,
            childBetween,
            childAfterNewer));

        ProcessSnapshot snapshot = reader.ReadSnapshot();
        ProcessRecord betweenRecord = snapshot.Processes.Single(p => p.Identity.ProcessId == 200);
        ProcessRecord afterRecord = snapshot.Processes.Single(p => p.Identity.ProcessId == 201);

        Assert.IsNull(betweenRecord.ParentProcessId);
        Assert.AreEqual(100, afterRecord.ParentProcessId);
    }

    [TestMethod]
    public void ReadSnapshotEmptyWhitespaceOrNullCommandLineDoesNotLeakCallerExecutableIntoArguments()
    {
        RawProcessEntry emptyCmd = CreateReadableEntry(301, 0, "empty.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { CommandLine = string.Empty },
        };
        RawProcessEntry whitespaceCmd = CreateReadableEntry(302, 0, "spaces.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { CommandLine = "   \t  " },
        };
        RawProcessEntry nullCmd = CreateReadableEntry(303, 0, "nullcmd.exe", BaseTicks) with
        {
            HandleResult = CreateReadableHandle(BaseTicks) with { CommandLine = null },
        };

        var reader = new ProcessReader(new FakeSnapshotSource(CurrentUserSid, emptyCmd, whitespaceCmd, nullCmd));
        ProcessSnapshot snapshot = reader.ReadSnapshot();
        Dictionary<int, ProcessRecord> byPid = snapshot.Processes.ToDictionary(p => p.Identity.ProcessId);

        Assert.AreEqual(string.Empty, byPid[301].CommandLine);
        IReadOnlyList<string>? emptyArgs = byPid[301].Arguments;
        Assert.IsNotNull(emptyArgs);
        Assert.AreEqual(0, emptyArgs.Count);

        Assert.AreEqual("   \t  ", byPid[302].CommandLine);
        IReadOnlyList<string>? whitespaceArgs = byPid[302].Arguments;
        Assert.IsNotNull(whitespaceArgs);
        Assert.AreEqual(0, whitespaceArgs.Count);

        Assert.IsNull(byPid[303].CommandLine);
        Assert.IsNull(byPid[303].Arguments);
        Assert.IsTrue(byPid[303].IsReadable);
    }

    private static RawProcessEntry CreateReadableEntry(
        int processId,
        int inheritedFromProcessId,
        string name,
        long startTimeTicks)
    {
        return new RawProcessEntry(
            ProcessId: processId,
            InheritedFromProcessId: inheritedFromProcessId,
            Name: name,
            SessionId: 1,
            NumberOfThreads: 2,
            SnapshotStartTimeTicks: startTimeTicks,
            Counters: new ProcessCounters(4096, 100_000, 512, 256),
            HandleResult: CreateReadableHandle(startTimeTicks));
    }

    private static RawProcessHandleResult CreateReadableHandle(long startTimeTicks)
    {
        return new RawProcessHandleResult(
            ExitedOrMissing: false,
            AccessDenied: false,
            HandleStartTimeBeforeTicks: startTimeTicks,
            HandleExitTimeBeforeTicks: 0,
            OwnerSid: CurrentUserSid,
            IsElevated: false,
            IsWow64: false,
            ExecutablePath: @"C:\App\app.exe",
            WorkingDirectory: @"C:\App\",
            CommandLine: @"""C:\App\app.exe"" --arg",
            HandleStartTimeAfterTicks: startTimeTicks,
            HandleExitTimeAfterTicks: 0);
    }

    private sealed class FakeSnapshotSource : IProcessSnapshotSource
    {
        private readonly IReadOnlyList<RawProcessEntry> _entries;

        public FakeSnapshotSource(string currentUserSid, params RawProcessEntry[] entries)
        {
            CurrentUserSid = currentUserSid;
            _entries = entries;
        }

        public string CurrentUserSid { get; }

        public IReadOnlyList<RawProcessEntry> ReadEntries() => _entries;

        public RawProcessEntry? ReadEntry(int processId) =>
            _entries.FirstOrDefault(e => e.ProcessId == processId);
    }
}
