using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ambysto.Kit.Safety.Native;

/// <summary>Win32 calls the gate makes. Every call reads; none creates, writes, moves or deletes anything
/// (scripts/windows-choke-points.sh checks the access rights and functions named here).</summary>
internal static partial class NativeMethods
{
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint READ_CONTROL = 0x00020000;
    internal const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    internal const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    internal const uint FILE_ATTRIBUTE_SPARSE_FILE = 0x200;
    internal const uint FILE_ATTRIBUTE_COMPRESSED = 0x800;
    internal const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
    internal const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_UNTRUSTED_MOUNT_POINT = 448;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_INVALID_NAME = 123;
    internal const int ERROR_BAD_NETPATH = 53;
    internal const int ERROR_NOT_READY = 21;

    internal const uint FILE_NAME_NORMALIZED = 0x0;
    internal const uint VOLUME_NAME_DOS = 0x0;
    internal const uint DRIVE_FIXED = 3;

    internal enum FileInfoClass
    {
        FileBasicInfo = 0,
        FileStandardInfo = 1,
        FileAttributeTagInfo = 9,
        FileIdInfo = 18,
    }

    // FILETIME is two DWORDs (4-byte aligned): Pack = 4 keeps the longs where Windows puts them.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct WIN32_FILE_ATTRIBUTE_DATA
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
        private readonly uint padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
        private readonly ushort padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ATTRIBUTE_TAG_INFO
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileAttributesEx(string name, int infoLevel, out WIN32_FILE_ATTRIBUTE_DATA data);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, FileInfoClass infoClass, void* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    internal static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* buffer, uint size, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetVolumeInformationByHandle(SafeFileHandle handle, char* volumeName, uint volumeNameSize,
        out uint serial, out uint maxComponent, out uint flags, char* fileSystemName, uint fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetVolumePathName(string fileName, char* volumePath, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetDriveType(string root);

    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    internal static partial int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    [LibraryImport("ole32.dll")]
    internal static partial void CoTaskMemFree(IntPtr p);

    internal const int SE_FILE_OBJECT = 1;
    internal const uint OWNER_SECURITY_INFORMATION = 0x1;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    internal static partial uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInfo,
        out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr p);
}

internal static partial class TokenMethods
{
    internal const int TokenElevation = 20;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token, int infoClass, out int elevated, int size, out int returned);
}

internal static unsafe partial class WalkMethods
{
    internal const int FindExInfoBasic = 1;
    internal const int FindExSearchNameMatch = 0;
    internal const uint FIND_FIRST_EX_LARGE_FETCH = 2;
    internal const int ERROR_NO_MORE_FILES = 18;
    internal const uint INVALID_FILE_SIZE = 0xFFFFFFFF;

    // FILETIME is two DWORDs (4-byte aligned): Pack = 4 keeps the longs where Windows puts them.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct WIN32_FIND_DATA
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[260];
        public fixed char AlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindFirstFileEx(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindNextFile(IntPtr handle, out WIN32_FIND_DATA data);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindClose(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetCompressedFileSize(string name, out uint high);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* volumeName, uint size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SHQUERYRBINFO
    {
        public uint Size;
        public long TotalBytes;
        public long ItemCount;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHQueryRecycleBin(string rootPath, ref SHQUERYRBINFO info);
}

/// <summary>Struct sizes checked against the Windows SDK at run time: a layout error must refuse (failing closed), never
/// read misplaced fields. x64 sizes: WIN32_FILE_ATTRIBUTE_DATA 36, WIN32_FIND_DATAW 592, FILE_ID_INFO 24,
/// FILE_BASIC_INFO 40, FILE_STANDARD_INFO 24, FILE_ATTRIBUTE_TAG_INFO 8, SHQUERYRBINFO 24.</summary>
internal static class LayoutCheck
{
    private static readonly bool Valid =
        Marshal.SizeOf<NativeMethods.WIN32_FILE_ATTRIBUTE_DATA>() == 36
        && Marshal.SizeOf<WalkMethods.WIN32_FIND_DATA>() == 592
        && Marshal.SizeOf<NativeMethods.FILE_ID_INFO>() == 24
        && Marshal.SizeOf<NativeMethods.FILE_BASIC_INFO>() == 40
        && Marshal.SizeOf<NativeMethods.FILE_STANDARD_INFO>() == 24
        && Marshal.SizeOf<NativeMethods.FILE_ATTRIBUTE_TAG_INFO>() == 8
        && Marshal.SizeOf<WalkMethods.SHQUERYRBINFO>() == 24
        && Marshal.OffsetOf<NativeMethods.WIN32_FILE_ATTRIBUTE_DATA>(nameof(NativeMethods.WIN32_FILE_ATTRIBUTE_DATA.FileSizeLow)) == 32
        && Marshal.OffsetOf<WalkMethods.WIN32_FIND_DATA>(nameof(WalkMethods.WIN32_FIND_DATA.Reserved0)) == 36
        && Marshal.OffsetOf<WalkMethods.WIN32_FIND_DATA>("FileName") == 44
        && Marshal.OffsetOf<WalkMethods.SHQUERYRBINFO>(nameof(WalkMethods.SHQUERYRBINFO.TotalBytes)) == 8;

    internal static void Verify()
    {
        if (!Valid)
        {
            throw new InvalidOperationException("native struct layout differs from the Windows SDK");
        }
    }
}

internal static unsafe partial class CloudMethods
{
    internal const int CF_PLACEHOLDER_INFO_STANDARD = 1;
    internal const int HRESULT_NOT_A_CLOUD_FILE = unchecked((int)0x80070178);
    internal const int HRESULT_MORE_DATA = unchecked((int)0x800700EA);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_PLACEHOLDER_STANDARD_INFO
    {
        public long OnDiskDataSize;
        public long ValidatedDataSize;
        public long ModifiedDataSize;
        public long PropertiesSize;
        public int PinState;
        public int InSyncState;
        public long FileId;
        public long SyncRootFileId;
        public uint FileIdentityLength;
    }

    /// <summary>Measured: on a volume the Cloud Files filter is not attached to (no sync root ever there), the queries
    /// return ERROR_INVALID_FUNCTION; no placeholder can exist there.</summary>
    internal const int HRESULT_INVALID_FUNCTION = unchecked((int)0x80070001);
    internal const int HRESULT_NOT_UNDER_SYNC_ROOT = unchecked((int)0x80070186);
    internal const int CF_SYNC_ROOT_INFO_BASIC = 0;

    [LibraryImport("cldapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CfGetSyncRootInfoByPath(string path, int infoClass, void* buffer, uint length, out uint returned);

    [LibraryImport("cldapi.dll")]
    internal static partial int CfGetPlaceholderInfo(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int infoClass, void* buffer, uint length, out uint returned);
}

internal static partial class SfcMethods
{
    [LibraryImport("sfc.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SfcIsFileProtected(IntPtr rpcHandle, string path);
}

internal static unsafe partial class InUseMethods
{
    internal const int FileProcessIdsUsingFileInformation = 47;
    internal const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public IntPtr Information;
    }

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQueryInformationFile(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out IO_STATUS_BLOCK status, void* buffer, uint length, int infoClass);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct RM_PROCESS_INFO
    {
        public int ProcessId;
        public long ProcessStartTime;
        public fixed char AppName[256];
        public fixed char ServiceShortName[64];
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int Restartable;
    }

    internal const int ERROR_MORE_DATA = 234;

    [LibraryImport("rstrtmgr.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RmStartSession(out uint session, int flags, char* key);

    [LibraryImport("rstrtmgr.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RmRegisterResources(uint session, uint fileCount, string[] files, uint appCount, IntPtr apps, uint serviceCount, IntPtr services);

    [LibraryImport("rstrtmgr.dll")]
    internal static partial int RmGetList(uint session, out uint needed, ref uint count, RM_PROCESS_INFO* info, out uint reasons);

    [LibraryImport("rstrtmgr.dll")]
    internal static partial int RmEndSession(uint session);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_VM_READ = 0x0010;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageName(IntPtr process, uint flags, char* name, ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQueryInformationProcess(IntPtr process, int infoClass, out PROCESS_BASIC_INFORMATION info, int length, out int returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadProcessMemory(IntPtr process, IntPtr address, void* buffer, nint size, out nint read);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CommandLineToArgvW(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr LocalFree(IntPtr p);
}
