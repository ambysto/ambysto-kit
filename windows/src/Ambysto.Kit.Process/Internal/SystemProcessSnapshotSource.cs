using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Ambysto.Kit.Processes.Native;

namespace Ambysto.Kit.Processes.Internal;

internal sealed class SystemProcessSnapshotSource : IProcessSnapshotSource
{
    public SystemProcessSnapshotSource()
    {
        if (Marshal.SizeOf<ProcessNativeMethods.SystemProcessInformation>() != 256)
        {
            throw new InvalidOperationException("Unexpected SYSTEM_PROCESS_INFORMATION struct size on x64.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        CurrentUserSid = identity.User?.Value
            ?? throw new InvalidOperationException("Unable to determine the current user SID.");
    }

    public string CurrentUserSid { get; }

    public IReadOnlyList<RawProcessEntry> ReadEntries()
    {
        nint buffer = QuerySystemProcessBuffer();
        try
        {
            var entries = new List<RawProcessEntry>(256);
            nint current = buffer;
            while (true)
            {
                var info = Marshal.PtrToStructure<ProcessNativeMethods.SystemProcessInformation>(current);
                int pid = unchecked((int)info.UniqueProcessId);
                if (pid > 0 && info.NumberOfThreads > 0 && info.CreateTime > 0)
                {
                    entries.Add(BuildRawEntry(in info));
                }

                if (info.NextEntryOffset == 0)
                {
                    break;
                }

                current += (nint)info.NextEntryOffset;
            }

            return entries;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public RawProcessEntry? ReadEntry(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        nint buffer = QuerySystemProcessBuffer();
        try
        {
            ProcessNativeMethods.SystemProcessInformation? bestMatch = null;
            nint current = buffer;
            while (true)
            {
                var info = Marshal.PtrToStructure<ProcessNativeMethods.SystemProcessInformation>(current);
                int pid = unchecked((int)info.UniqueProcessId);
                if (pid == processId && info.NumberOfThreads > 0 && info.CreateTime > 0)
                {
                    if (bestMatch is null || info.CreateTime >= bestMatch.Value.CreateTime)
                    {
                        bestMatch = info;
                    }
                }

                if (info.NextEntryOffset == 0)
                {
                    break;
                }

                current += (nint)info.NextEntryOffset;
            }

            if (bestMatch is not ProcessNativeMethods.SystemProcessInformation matched)
            {
                return null;
            }

            return BuildRawEntry(in matched);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private RawProcessEntry BuildRawEntry(in ProcessNativeMethods.SystemProcessInformation info)
    {
        int pid = unchecked((int)info.UniqueProcessId);
        int parentPid = unchecked((int)info.InheritedFromUniqueProcessId);
        string name = ReadUnicodeStringLocal(in info.ImageName);
        int sessionId = info.SessionId > int.MaxValue ? 0 : (int)info.SessionId;
        long startTimeTicks = info.CreateTime;

        long memoryBytes = Math.Max(0L, info.WorkingSetPrivateSize);
        long cpuTimeNanoseconds = Math.Max(0L, unchecked((info.UserTime + info.KernelTime) * 100L));
        long bytesRead = Math.Max(0L, info.ReadTransferCount);
        long bytesWritten = Math.Max(0L, info.WriteTransferCount);

        var counters = new ProcessCounters(
            memoryBytes: memoryBytes,
            cpuTimeNanoseconds: cpuTimeNanoseconds,
            bytesRead: bytesRead,
            bytesWritten: bytesWritten);

        RawProcessHandleResult handleResult = ReadHandleMetadata(pid, startTimeTicks);

        return new RawProcessEntry(
            ProcessId: pid,
            InheritedFromProcessId: parentPid,
            Name: name,
            SessionId: sessionId,
            NumberOfThreads: info.NumberOfThreads,
            SnapshotStartTimeTicks: startTimeTicks,
            Counters: counters,
            HandleResult: handleResult);
    }

    private RawProcessHandleResult ReadHandleMetadata(int pid, long snapshotStartTimeTicks)
    {
        nint hProcess = ProcessNativeMethods.OpenProcess(
            ProcessNativeMethods.ProcessQueryLimitedInformation,
            inheritHandle: false,
            (uint)pid);

        if (hProcess == 0)
        {
            int err = Marshal.GetLastPInvokeError();
            bool exited = err == ProcessNativeMethods.ErrorInvalidParameter;
            return new RawProcessHandleResult(
                ExitedOrMissing: exited,
                AccessDenied: !exited,
                HandleStartTimeBeforeTicks: 0,
                HandleExitTimeBeforeTicks: 0,
                OwnerSid: null,
                IsElevated: false,
                IsWow64: false,
                ExecutablePath: null,
                WorkingDirectory: null,
                CommandLine: null,
                HandleStartTimeAfterTicks: 0,
                HandleExitTimeAfterTicks: 0);
        }

        try
        {
            if (!ProcessNativeMethods.GetProcessTimes(
                    hProcess,
                    out long createBefore,
                    out long exitBefore,
                    out _,
                    out _))
            {
                return new RawProcessHandleResult(
                    ExitedOrMissing: true,
                    AccessDenied: false,
                    HandleStartTimeBeforeTicks: 0,
                    HandleExitTimeBeforeTicks: 0,
                    OwnerSid: null,
                    IsElevated: false,
                    IsWow64: false,
                    ExecutablePath: null,
                    WorkingDirectory: null,
                    CommandLine: null,
                    HandleStartTimeAfterTicks: 0,
                    HandleExitTimeAfterTicks: 0);
            }

            if (exitBefore != 0 || createBefore != snapshotStartTimeTicks)
            {
                return new RawProcessHandleResult(
                    ExitedOrMissing: false,
                    AccessDenied: false,
                    HandleStartTimeBeforeTicks: createBefore,
                    HandleExitTimeBeforeTicks: exitBefore,
                    OwnerSid: null,
                    IsElevated: false,
                    IsWow64: false,
                    ExecutablePath: null,
                    WorkingDirectory: null,
                    CommandLine: null,
                    HandleStartTimeAfterTicks: createBefore,
                    HandleExitTimeAfterTicks: exitBefore);
            }

            if (!TryReadTokenOwnerAndElevation(hProcess, out string? ownerSid, out bool isElevated))
            {
                return new RawProcessHandleResult(
                    ExitedOrMissing: false,
                    AccessDenied: true,
                    HandleStartTimeBeforeTicks: createBefore,
                    HandleExitTimeBeforeTicks: 0,
                    OwnerSid: null,
                    IsElevated: false,
                    IsWow64: false,
                    ExecutablePath: null,
                    WorkingDirectory: null,
                    CommandLine: null,
                    HandleStartTimeAfterTicks: createBefore,
                    HandleExitTimeAfterTicks: 0);
            }

            if ( isElevated || !string.Equals(ownerSid, CurrentUserSid, StringComparison.Ordinal))
            {
                return new RawProcessHandleResult(
                    ExitedOrMissing: false,
                    AccessDenied: false,
                    HandleStartTimeBeforeTicks: createBefore,
                    HandleExitTimeBeforeTicks: 0,
                    OwnerSid: ownerSid,
                    IsElevated: isElevated,
                    IsWow64: false,
                    ExecutablePath: null,
                    WorkingDirectory: null,
                    CommandLine: null,
                    HandleStartTimeAfterTicks: createBefore,
                    HandleExitTimeAfterTicks: 0);
            }

            bool isWow64 = false;
            if (ProcessNativeMethods.IsWow64Process2(hProcess, out ushort processMachine, out _))
            {
                isWow64 = processMachine != 0;
            }

            string? executablePath = TryQueryExecutablePath(hProcess);
            string? commandLine = TryQueryCommandLine(hProcess);
            string? workingDirectory = isWow64 ? null : TryRead64BitWorkingDirectory(pid, snapshotStartTimeTicks);

            if (!ProcessNativeMethods.GetProcessTimes(
                    hProcess,
                    out long createAfter,
                    out long exitAfter,
                    out _,
                    out _))
            {
                return new RawProcessHandleResult(
                    ExitedOrMissing: true,
                    AccessDenied: false,
                    HandleStartTimeBeforeTicks: createBefore,
                    HandleExitTimeBeforeTicks: exitBefore,
                    OwnerSid: null,
                    IsElevated: false,
                    IsWow64: false,
                    ExecutablePath: null,
                    WorkingDirectory: null,
                    CommandLine: null,
                    HandleStartTimeAfterTicks: 0,
                    HandleExitTimeAfterTicks: 0);
            }

            return new RawProcessHandleResult(
                ExitedOrMissing: false,
                AccessDenied: false,
                HandleStartTimeBeforeTicks: createBefore,
                HandleExitTimeBeforeTicks: exitBefore,
                OwnerSid: ownerSid,
                IsElevated: false,
                IsWow64: isWow64,
                ExecutablePath: executablePath,
                WorkingDirectory: workingDirectory,
                CommandLine: commandLine,
                HandleStartTimeAfterTicks: createAfter,
                HandleExitTimeAfterTicks: exitAfter);
        }
        finally
        {
            _ = ProcessNativeMethods.CloseHandle(hProcess);
        }
    }

    private static bool TryReadTokenOwnerAndElevation(nint hProcess, out string? ownerSid, out bool isElevated)
    {
        ownerSid = null;
        isElevated = false;

        if (!ProcessNativeMethods.OpenProcessToken(hProcess, ProcessNativeMethods.TokenQuery, out nint hToken) || hToken == 0)
        {
            return false;
        }

        try
        {
            // 1. TokenElevation
            var elevation = default(ProcessNativeMethods.TokenElevation);
            uint elevationSize = (uint)Marshal.SizeOf<ProcessNativeMethods.TokenElevation>();
            unsafe
            {
                if (!ProcessNativeMethods.GetTokenInformation(
                        hToken,
                        ProcessNativeMethods.TokenElevationClass,
                        (nint)(&elevation),
                        elevationSize,
                        out _))
                {
                    return false;
                }
            }

            isElevated = elevation.TokenIsElevated != 0;

            // 2. TokenUser
            _ = ProcessNativeMethods.GetTokenInformation(
                hToken,
                ProcessNativeMethods.TokenUserClass,
                0,
                0,
                out uint needed);

            if (needed == 0)
            {
                return false;
            }

            nint tokenUserBuf = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!ProcessNativeMethods.GetTokenInformation(
                        hToken,
                        ProcessNativeMethods.TokenUserClass,
                        tokenUserBuf,
                        needed,
                        out _))
                {
                    return false;
                }

                var sidAndAttrs = Marshal.PtrToStructure<ProcessNativeMethods.SidAndAttributes>(tokenUserBuf);
                if (!ProcessNativeMethods.ConvertSidToStringSidW(sidAndAttrs.Sid, out nint stringSidPtr) || stringSidPtr == 0)
                {
                    return false;
                }

                try
                {
                    ownerSid = Marshal.PtrToStringUni(stringSidPtr);
                    return !string.IsNullOrEmpty(ownerSid);
                }
                finally
                {
                    _ = ProcessNativeMethods.LocalFree(stringSidPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tokenUserBuf);
            }
        }
        finally
        {
            _ = ProcessNativeMethods.CloseHandle(hToken);
        }
    }

    private static unsafe string? TryQueryExecutablePath(nint hProcess)
    {
        char[] buffer = new char[1024];
        uint size = (uint)buffer.Length;
        fixed (char* ptr = buffer)
        {
            if (ProcessNativeMethods.QueryFullProcessImageNameW(hProcess, 0, ptr, ref size) && size > 0)
            {
                return new string(buffer, 0, (int)size);
            }
        }

        char[] largeBuffer = new char[32768];
        size = (uint)largeBuffer.Length;
        fixed (char* ptr = largeBuffer)
        {
            if (ProcessNativeMethods.QueryFullProcessImageNameW(hProcess, 0, ptr, ref size) && size > 0)
            {
                return new string(largeBuffer, 0, (int)size);
            }
        }

        return null;
    }

    private static string? TryQueryCommandLine(nint hProcess)
    {
        int status = ProcessNativeMethods.NtQueryInformationProcess(
            hProcess,
            ProcessNativeMethods.ProcessCommandLineInformationClass,
            0,
            0,
            out uint needed);

        if (needed == 0 && status != ProcessNativeMethods.StatusInfoLengthMismatch)
        {
            return null;
        }

        uint allocSize = Math.Max(needed, 512u);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal((int)allocSize);
            try
            {
                status = ProcessNativeMethods.NtQueryInformationProcess(
                    hProcess,
                    ProcessNativeMethods.ProcessCommandLineInformationClass,
                    buffer,
                    allocSize,
                    out needed);

                if (status == 0)
                {
                    var unicode = Marshal.PtrToStructure<ProcessNativeMethods.UnicodeString>(buffer);
                    return ReadUnicodeStringLocal(in unicode);
                }

                if (status == ProcessNativeMethods.StatusInfoLengthMismatch && needed > allocSize)
                {
                    allocSize = needed + 256u;
                    continue;
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    private static unsafe string? TryRead64BitWorkingDirectory(int pid, long expectedStartTimeTicks)
    {
        nint hVm = ProcessNativeMethods.OpenProcess(
            ProcessNativeMethods.ProcessQueryLimitedInformation | ProcessNativeMethods.ProcessVmRead,
            inheritHandle: false,
            (uint)pid);

        if (hVm == 0)
        {
            return null;
        }

        try
        {
            if (!ProcessNativeMethods.GetProcessTimes(hVm, out long createBefore, out long exitBefore, out _, out _) ||
                exitBefore != 0 ||
                createBefore != expectedStartTimeTicks)
            {
                return null;
            }

            var pbi = default(ProcessNativeMethods.ProcessBasicInformation);
            int status = ProcessNativeMethods.NtQueryInformationProcess(
                hVm,
                ProcessNativeMethods.ProcessBasicInformationClass,
                (nint)(&pbi),
                (uint)Marshal.SizeOf<ProcessNativeMethods.ProcessBasicInformation>(),
                out _);

            if (status != 0 || pbi.PebBaseAddress == 0)
            {
                return null;
            }

            nint processParametersPtr = 0;
            if (!ProcessNativeMethods.ReadProcessMemory(
                    hVm,
                    pbi.PebBaseAddress + ProcessNativeMethods.PebProcessParametersOffset,
                    (nint)(&processParametersPtr),
                    (nuint)nint.Size,
                    out nuint bytesRead) ||
                bytesRead != (nuint)nint.Size ||
                processParametersPtr == 0)
            {
                return null;
            }

            var dosPathUnicode = default(ProcessNativeMethods.UnicodeString);
            if (!ProcessNativeMethods.ReadProcessMemory(
                    hVm,
                    processParametersPtr + ProcessNativeMethods.ProcessParametersCurrentDirectoryDosPathOffset,
                    (nint)(&dosPathUnicode),
                    (nuint)Marshal.SizeOf<ProcessNativeMethods.UnicodeString>(),
                    out bytesRead) ||
                bytesRead != (nuint)Marshal.SizeOf<ProcessNativeMethods.UnicodeString>() ||
                dosPathUnicode.Length == 0 ||
                dosPathUnicode.Buffer == 0)
            {
                return null;
            }

            int charCount = dosPathUnicode.Length / sizeof(char);
            if (charCount <= 0 || charCount > 32768)
            {
                return null;
            }

            char[] chars = new char[charCount];
            fixed (char* charPtr = chars)
            {
                if (!ProcessNativeMethods.ReadProcessMemory(
                        hVm,
                        dosPathUnicode.Buffer,
                        (nint)charPtr,
                        (nuint)(charCount * sizeof(char)),
                        out bytesRead) ||
                    bytesRead != (nuint)(charCount * sizeof(char)))
                {
                    return null;
                }
            }

            if (!ProcessNativeMethods.GetProcessTimes(hVm, out long createAfter, out long exitAfter, out _, out _) ||
                exitAfter != 0 ||
                createAfter != expectedStartTimeTicks)
            {
                return null;
            }

            return new string(chars);
        }
        finally
        {
            _ = ProcessNativeMethods.CloseHandle(hVm);
        }
    }

    private static nint QuerySystemProcessBuffer()
    {
        uint size = 256 * 1024;
        while (true)
        {
            nint buffer = Marshal.AllocHGlobal((int)size);
            int status = ProcessNativeMethods.NtQuerySystemInformation(
                ProcessNativeMethods.SystemProcessInformationClass,
                buffer,
                size,
                out uint returnLength);

            if (status == 0)
            {
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);
            if (status != ProcessNativeMethods.StatusInfoLengthMismatch)
            {
                throw new InvalidOperationException($"NtQuerySystemInformation(SystemProcessInformation) failed with NTSTATUS 0x{status:X8}.");
            }

            size = Math.Max(size * 2, returnLength + 64 * 1024);
        }
    }

    private static string ReadUnicodeStringLocal(in ProcessNativeMethods.UnicodeString unicode)
    {
        if (unicode.Length == 0 || unicode.Buffer == 0)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / sizeof(char)) ?? string.Empty;
    }
}
