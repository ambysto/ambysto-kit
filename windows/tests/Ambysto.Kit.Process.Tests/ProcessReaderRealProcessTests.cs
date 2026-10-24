using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed class ProcessReaderRealProcessTests
{
    [TestMethod]
    public void ReadSnapshotCurrentProcessPopulatesExpectedReadableMetadataAndUsage()
    {
        var reader = new ProcessReader();
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        ProcessRecord? self = snapshot.Processes.SingleOrDefault(p => p.Identity.ProcessId == Environment.ProcessId);
        Assert.IsNotNull(self);
        Assert.IsTrue(self.IsReadable);

        using var currentProcess = Process.GetCurrentProcess();
        Assert.AreEqual(currentProcess.StartTime.ToFileTimeUtc(), self.Identity.StartTimeTicks);

        string expectedSid = WindowsIdentity.GetCurrent().User!.Value;
        Assert.IsNotNull(self.OwnerSid);
        StringAssert.StartsWith(self.OwnerSid, "S-1-5-21-", StringComparison.Ordinal);
        Assert.AreEqual(expectedSid, self.OwnerSid);

        Assert.IsNotNull(self.ExecutablePath);
        Assert.IsTrue(self.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (Environment.ProcessPath is not null)
        {
            Assert.AreEqual(Environment.ProcessPath, self.ExecutablePath, ignoreCase: true);
        }

        string expectedCwd = NormalizeExpectedWorkingDirectory(Environment.CurrentDirectory);
        Assert.AreEqual(expectedCwd, self.WorkingDirectory);

        Assert.IsFalse(string.IsNullOrEmpty(self.CommandLine));
        Assert.IsNotNull(self.Arguments);
        Assert.IsTrue(self.Arguments.Count >= 1);

        Assert.IsNotNull(self.Usage);
        Assert.IsTrue(self.Usage.MemoryBytes > 0);
        Assert.IsTrue(self.Usage.CpuTimeNanoseconds >= 0);
        Assert.IsTrue(self.Usage.BytesRead >= 0);
        Assert.IsTrue(self.Usage.BytesWritten >= 0);
        Assert.IsNull(self.Usage.IdleWakeups);
        Assert.IsNull(self.Usage.InterruptWakeups);
        Assert.IsNull(self.Usage.EnergyNanojoules);
    }

    [TestMethod]
    public void ReadSnapshotUnreadableSystemAndServiceProcessesNullOutGatedFieldsAndMatchUnreadableCount()
    {
        var reader = new ProcessReader();
        ProcessSnapshot snapshot = reader.ReadSnapshot();

        Assert.IsTrue(snapshot.UnreadableProcessCount >= 1);
        int expectedUnreadableCount = snapshot.Processes.Count(p => !p.IsReadable);
        Assert.AreEqual(expectedUnreadableCount, snapshot.UnreadableProcessCount);

        // PID 0 (System Idle Process) must never appear in the snapshot.
        Assert.IsFalse(snapshot.Processes.Any(p => p.Identity.ProcessId == 0));

        // System (PID 4) is present and unreadable for a standard user.
        ProcessRecord? systemProc = snapshot.Processes.SingleOrDefault(p => p.Identity.ProcessId == 4);
        Assert.IsNotNull(systemProc);
        Assert.IsFalse(systemProc.IsReadable);

        foreach (ProcessRecord unreadable in snapshot.Processes.Where(p => !p.IsReadable))
        {
            Assert.IsTrue(unreadable.Identity.ProcessId > 0);
            Assert.IsTrue(unreadable.Identity.StartTimeTicks > 0);
            Assert.IsFalse(string.IsNullOrEmpty(unreadable.Name));
            Assert.IsNull(unreadable.OwnerSid);
            Assert.IsNull(unreadable.ExecutablePath);
            Assert.IsNull(unreadable.WorkingDirectory);
            Assert.IsNull(unreadable.CommandLine);
            Assert.IsNull(unreadable.Arguments);
            Assert.IsNull(unreadable.Usage);
        }
    }

    [TestMethod]
    public void ReadSnapshotSpawned32BitWow64ChildIsReadableWithNullWorkingDirectoryAndValidParent()
    {
        string sysWow64Cmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysWOW64",
            "cmd.exe");
        Assert.IsTrue(File.Exists(sysWow64Cmd), $"Expected 32-bit cmd.exe at {sysWow64Cmd}");

        var psi = new ProcessStartInfo(sysWow64Cmd, "/c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process? child = Process.Start(psi);
        Assert.IsNotNull(child);
        try
        {
            ProcessRecord? childRecord = WaitForProcessInSnapshot(child.Id, requireCommandLine: true);
            Assert.IsNotNull(childRecord);
            Assert.IsTrue(childRecord.IsReadable);
            Assert.AreEqual(Environment.ProcessId, childRecord.ParentProcessId);
            Assert.IsNotNull(childRecord.ExecutablePath);
            Assert.IsTrue(
                childRecord.ExecutablePath.EndsWith(@"\SysWOW64\cmd.exe", StringComparison.OrdinalIgnoreCase),
                $"Unexpected ExecutablePath: {childRecord.ExecutablePath}");
            Assert.IsNotNull(childRecord.CommandLine);
            StringAssert.Contains(childRecord.CommandLine, "ping", StringComparison.OrdinalIgnoreCase);
            Assert.IsNotNull(childRecord.Arguments);
            Assert.IsTrue(childRecord.Arguments.Count >= 2);
            Assert.IsNull(childRecord.WorkingDirectory);
            Assert.IsNotNull(childRecord.Usage);
            Assert.IsTrue(childRecord.Usage.MemoryBytes > 0);
        }
        finally
        {
            try
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(5_000);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    [TestMethod]
    public void ReadSnapshotOrphanedChildProcessClearsParentProcessIdWhenParentExits()
    {
        string systemCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string pingExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
        int uniqueTimeoutMs = 17_531;

        var psi = new ProcessStartInfo(
            systemCmd,
            $"/c start \"\" /b \"{pingExe}\" -n 30 -w {uniqueTimeoutMs} 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        int? orphanPid = null;
        using Process? parentCmd = Process.Start(psi);
        Assert.IsNotNull(parentCmd);
        try
        {
            int parentPid = parentCmd.Id;
            long parentStartTicks = parentCmd.StartTime.ToFileTimeUtc();
            var parentIdentity = new ProcessIdentity(parentPid, parentStartTicks);

            parentCmd.WaitForExit(10_000);

            var reader = new ProcessReader();
            ProcessRecord? orphanRecord = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5_000)
            {
                ProcessSnapshot snapshot = reader.ReadSnapshot();

                // Even while parentCmd handle is still held open by this test, the exited parent is absent.
                Assert.IsFalse(snapshot.Processes.Any(p => p.Identity == parentIdentity));
                Assert.IsNull(reader.Read(parentIdentity));

                orphanRecord = snapshot.Processes.FirstOrDefault(p =>
                    p.IsReadable &&
                    p.CommandLine is not null &&
                    p.CommandLine.Contains($"-w {uniqueTimeoutMs}", StringComparison.Ordinal));

                if (orphanRecord is not null)
                {
                    orphanPid = orphanRecord.Identity.ProcessId;
                    break;
                }

                Thread.Sleep(50);
            }

            Assert.IsNotNull(orphanRecord, "Expected spawned orphan ping.exe to appear in snapshot.");
            Assert.IsNull(orphanRecord.ParentProcessId);
        }
        finally
        {
            if (orphanPid is int pid)
            {
                try
                {
                    using Process orphan = Process.GetProcessById(pid);
                    orphan.Kill(entireProcessTree: true);
                    orphan.WaitForExit(5_000);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static ProcessRecord? WaitForProcessInSnapshot(int processId, bool requireCommandLine)
    {
        var reader = new ProcessReader();
        var sw = Stopwatch.StartNew();
        ProcessRecord? lastSeen = null;
        while (sw.ElapsedMilliseconds < 5_000)
        {
            ProcessSnapshot snapshot = reader.ReadSnapshot();
            lastSeen = snapshot.Processes.SingleOrDefault(p => p.Identity.ProcessId == processId);
            if (lastSeen is not null && (!requireCommandLine || !string.IsNullOrEmpty(lastSeen.CommandLine)))
            {
                return lastSeen;
            }

            Thread.Sleep(50);
        }

        return lastSeen;
    }

    private static string NormalizeExpectedWorkingDirectory(string path)
    {
        string normalized = path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;

        if (normalized.Length == 3 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':' &&
            normalized[2] == '\\')
        {
            return normalized;
        }

        return normalized.TrimEnd('\\');
    }
}
