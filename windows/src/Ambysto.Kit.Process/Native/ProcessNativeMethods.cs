using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ambysto.Kit.Processes.Native;

internal static partial class ProcessNativeMethods
{
    internal const int SystemProcessInformationClass = 5;
    internal const int ProcessBasicInformationClass = 0;
    internal const int ProcessCommandLineInformationClass = 60;

    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    internal const int ErrorInvalidParameter = 87;

    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint TokenQuery = 0x0008;

    internal const int TokenUserClass = 1;
    internal const int TokenElevationClass = 20;

    // 64-bit PEB offsets for RTL_USER_PROCESS_PARAMETERS and CurrentDirectory.DosPath
    internal const int PebProcessParametersOffset = 0x20;
    internal const int ProcessParametersCurrentDirectoryDosPathOffset = 0x38;

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public uint Padding;
        public nint Buffer;
    }

    /// <summary>
    /// Exact 64-bit layout of <c>SYSTEM_PROCESS_INFORMATION</c> from <c>winternl.h</c> (256 bytes on x64).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessInformation
    {
        public uint NextEntryOffset;                  // offset 0
        public uint NumberOfThreads;                  // offset 4
        public long WorkingSetPrivateSize;            // offset 8
        public uint HardFaultCount;                   // offset 16
        public uint NumberOfThreadsHighWatermark;     // offset 20
        public ulong CycleTime;                       // offset 24
        public long CreateTime;                       // offset 32
        public long UserTime;                         // offset 40
        public long KernelTime;                       // offset 48
        public UnicodeString ImageName;               // offset 56 (16 bytes)
        public int BasePriority;                      // offset 72 (+4 padding -> 80)
        public nint UniqueProcessId;                  // offset 80
        public nint InheritedFromUniqueProcessId;     // offset 88
        public uint HandleCount;                      // offset 96
        public uint SessionId;                        // offset 100
        public nuint UniqueProcessKey;                // offset 104
        public nuint PeakVirtualSize;                 // offset 112
        public nuint VirtualSize;                     // offset 120
        public uint PageFaultCount;                   // offset 128 (+4 padding -> 136)
        public nuint PeakWorkingSetSize;              // offset 136
        public nuint WorkingSetSize;                  // offset 144
        public nuint QuotaPeakPagedPoolUsage;         // offset 152
        public nuint QuotaPagedPoolUsage;             // offset 160
        public nuint QuotaPeakNonPagedPoolUsage;      // offset 168
        public nuint QuotaNonPagedPoolUsage;          // offset 176
        public nuint PagefileUsage;                   // offset 184
        public nuint PeakPagefileUsage;               // offset 192
        public nuint PrivatePageCount;                // offset 200
        public long ReadOperationCount;               // offset 208
        public long WriteOperationCount;              // offset 216
        public long OtherOperationCount;              // offset 224
        public long ReadTransferCount;                // offset 232
        public long WriteTransferCount;               // offset 240
        public long OtherTransferCount;               // offset 248
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nuint UniqueProcessId;
        public nint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenElevation
    {
        public uint TokenIsElevated;
    }

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQuerySystemInformation(
        int systemInformationClass,
        nint systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        nint processInformation,
        uint processInformationLength,
        out uint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        nint processHandle,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool QueryFullProcessImageNameW(
        nint processHandle,
        uint flags,
        char* exeName,
        ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process2(
        nint processHandle,
        out ushort processMachine,
        out ushort nativeMachine);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadProcessMemory(
        nint processHandle,
        nint baseAddress,
        nint buffer,
        nuint size,
        out nuint numberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint LocalFree(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertSidToStringSidW(
        nint sid,
        out nint stringSid);

    [LibraryImport("shell32.dll", EntryPoint = "CommandLineToArgvW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CommandLineToArgvW(
        string cmdLine,
        out int numArgs);

    internal static IReadOnlyList<string> ParseCommandLineArguments(string commandLine)
    {
        // Guard against CommandLineToArgvW("") returning the current process's own executable path
        // and CommandLineToArgvW("   ") returning [""] (docs/spec/process.md, "Command line and arguments").
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        nint argvPtr = CommandLineToArgvW(commandLine, out int numArgs);
        if (argvPtr == 0)
        {
            return Array.Empty<string>();
        }

        try
        {
            if (numArgs <= 0)
            {
                return Array.Empty<string>();
            }

            var args = new string[numArgs];
            for (int i = 0; i < numArgs; i++)
            {
                nint argPtr = Marshal.ReadIntPtr(argvPtr, i * nint.Size);
                args[i] = Marshal.PtrToStringUni(argPtr) ?? string.Empty;
            }

            return args;
        }
        finally
        {
            _ = LocalFree(argvPtr);
        }
    }
}
