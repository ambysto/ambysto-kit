// P/Invoke surface of cldapi.dll (Cloud Files API) and the few kernel32 calls AmbyCloud needs.
// Struct layouts were checked against Windows SDK 10.0.26100 cfapi.h for x64 (the only platform this builds for).
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ambysto.Kit.Safety.CloudFixture;

/// <summary>A failed Win32/COM/NTSTATUS-style call, carrying the HRESULT.</summary>
internal sealed class CfException : Exception
{
    public CfException(string what, int hr) : base(Describe(what, hr)) { HResult = hr; }

    public static string Describe(string what, int hr)
    {
        string text = "";
        if ((hr & 0x1FFF0000) == 0x00070000) // HRESULT_FROM_WIN32
        {
            try { text = new Win32Exception(hr & 0xFFFF).Message; } catch { }
        }
        return $"{what} failed: 0x{hr:X8} {text}".TrimEnd();
    }
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate void CfCallback(IntPtr callbackInfo, IntPtr callbackParameters);

// CF_FS_METADATA = FILE_BASIC_INFO (40 bytes, ends with padding) + FileSize.
[StructLayout(LayoutKind.Sequential)]
internal struct CfFsMetadata
{
    public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
    public uint FileAttributes;
    public long FileSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfPlaceholderCreateInfo
{
    public IntPtr RelativeFileName;
    public CfFsMetadata FsMetadata;
    public IntPtr FileIdentity;
    public uint FileIdentityLength;
    public uint Flags;
    public int Result;
    public long CreateUsn;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfSyncPolicies
{
    public uint StructSize;
    public ushort HydrationPrimary, HydrationModifier;
    public ushort PopulationPrimary, PopulationModifier;
    public uint InSync;
    public uint HardLink;
    public uint PlaceholderManagement;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct CfSyncRegistration
{
    public uint StructSize;
    [MarshalAs(UnmanagedType.LPWStr)] public string? ProviderName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? ProviderVersion;
    public IntPtr SyncRootIdentity;
    public uint SyncRootIdentityLength;
    public IntPtr FileIdentity;
    public uint FileIdentityLength;
    public Guid ProviderId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfCallbackRegistration
{
    public uint Type;
    public IntPtr Callback;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfProcessInfo
{
    public uint StructSize;
    public uint ProcessId;
    public IntPtr ImagePath;
    public IntPtr PackageName;
    public IntPtr ApplicationId;
    public IntPtr CommandLine;
    public uint SessionId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfCallbackInfo
{
    public uint StructSize;
    public long ConnectionKey;
    public IntPtr CallbackContext;
    public IntPtr VolumeGuidName;
    public IntPtr VolumeDosName;
    public uint VolumeSerialNumber;
    public long SyncRootFileId;
    public IntPtr SyncRootIdentity;
    public uint SyncRootIdentityLength;
    public long FileId;
    public long FileSize;
    public IntPtr FileIdentity;
    public uint FileIdentityLength;
    public IntPtr NormalizedPath;
    public long TransferKey;
    public byte PriorityHint;
    public IntPtr CorrelationVector;
    public IntPtr ProcessInfo;
    public long RequestKey;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfOperationInfo
{
    public uint StructSize;
    public uint Type;
    public long ConnectionKey;
    public long TransferKey;
    public IntPtr CorrelationVector;
    public IntPtr SyncStatus;
    public long RequestKey;
}

// CF_OPERATION_PARAMETERS: ULONG ParamSize, 4 bytes padding (the union is 8-aligned), then the member.
[StructLayout(LayoutKind.Sequential)]
internal struct CfOpTransferData
{
    public uint ParamSize;
    public uint Pad;
    public uint Flags;
    public int CompletionStatus;
    public IntPtr Buffer;
    public long Offset;
    public long Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfOpAck // AckDelete and AckRename share this shape: Flags + CompletionStatus
{
    public uint ParamSize;
    public uint Pad;
    public uint Flags;
    public int CompletionStatus;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CfOpAckDehydrate
{
    public uint ParamSize;
    public uint Pad;
    public uint Flags;
    public int CompletionStatus;
    public IntPtr FileIdentity;
    public uint FileIdentityLength;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct Win32FindData
{
    public uint dwFileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0; // reparse tag when REPARSE_POINT is set
    public uint dwReserved1;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
}

internal static class Native
{
    // cfapi constants
    public const long CF_EOF = -1;
    public const uint CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 0x1;
    public const uint CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC = 0x2;
    public const uint CF_REGISTER_FLAG_UPDATE = 0x1;
    public const ushort CF_HYDRATION_POLICY_PARTIAL = 0;
    public const ushort CF_HYDRATION_POLICY_PROGRESSIVE = 1;
    public const ushort CF_HYDRATION_POLICY_FULL = 2;
    public const ushort CF_POPULATION_POLICY_PARTIAL = 0;
    public const ushort CF_POPULATION_POLICY_ALWAYS_FULL = 3;
    public const uint CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO = 0x2;
    public const uint CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH = 0x4;
    public const uint CF_OPEN_FILE_FLAG_WRITE_ACCESS = 0x2;

    public const uint CF_CALLBACK_TYPE_FETCH_DATA = 0;
    public const uint CF_CALLBACK_TYPE_CANCEL_FETCH_DATA = 2;
    public const uint CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE = 7;
    public const uint CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE_COMPLETION = 8;
    public const uint CF_CALLBACK_TYPE_NOTIFY_DELETE = 9;
    public const uint CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION = 10;
    public const uint CF_CALLBACK_TYPE_NOTIFY_RENAME = 11;
    public const uint CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION = 12;
    public const uint CF_CALLBACK_TYPE_NONE = 0xFFFFFFFF;

    public const uint CF_OPERATION_TYPE_TRANSFER_DATA = 0;
    public const uint CF_OPERATION_TYPE_ACK_DEHYDRATE = 5;
    public const uint CF_OPERATION_TYPE_ACK_DELETE = 6;
    public const uint CF_OPERATION_TYPE_ACK_RENAME = 7;

    public const int STATUS_SUCCESS = 0;
    public const int STATUS_CLOUD_FILE_UNSUCCESSFUL = unchecked((int)0xC000CF12);

    // CF_PLACEHOLDER_STATE
    public const uint CF_PLACEHOLDER_STATE_INVALID = 0xFFFFFFFF;

    public const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;

    // file attributes
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    public const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000;
    public const uint FILE_ATTRIBUTE_PINNED = 0x80000;
    public const uint FILE_ATTRIBUTE_UNPINNED = 0x100000;
    public const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;

    // CreateFile
    public const uint FILE_READ_ATTRIBUTES = 0x80;
    public const uint FILE_SHARE_ALL = 0x7; // read | write | delete
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OPEN_NO_RECALL = 0x00100000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // ---- cldapi.dll ----
    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfRegisterSyncRoot(string syncRootPath, in CfSyncRegistration registration, in CfSyncPolicies policies, uint registerFlags);

    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfUnregisterSyncRoot(string syncRootPath);

    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfConnectSyncRoot(string syncRootPath, CfCallbackRegistration[] callbackTable, IntPtr callbackContext, uint connectFlags, out long connectionKey);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern int CfDisconnectSyncRoot(long connectionKey);

    [DllImport("cldapi.dll", ExactSpelling = true, EntryPoint = "CfExecute")]
    public static extern int CfExecuteTransfer(in CfOperationInfo opInfo, ref CfOpTransferData opParams);

    [DllImport("cldapi.dll", ExactSpelling = true, EntryPoint = "CfExecute")]
    public static extern int CfExecuteAck(in CfOperationInfo opInfo, ref CfOpAck opParams);

    [DllImport("cldapi.dll", ExactSpelling = true, EntryPoint = "CfExecute")]
    public static extern int CfExecuteAckDehydrate(in CfOperationInfo opInfo, ref CfOpAckDehydrate opParams);

    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfCreatePlaceholders(string baseDirectoryPath, [In, Out] CfPlaceholderCreateInfo[] placeholderArray, uint placeholderCount, uint createFlags, out uint entriesProcessed);

    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfOpenFileWithOplock(string filePath, uint flags, out IntPtr protectedHandle);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern IntPtr CfGetWin32HandleFromProtectedHandle(IntPtr protectedHandle);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern void CfCloseHandle(IntPtr protectedHandle);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern int CfDehydratePlaceholder(IntPtr fileHandle, long startingOffset, long length, uint dehydrateFlags, IntPtr overlapped);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern int CfHydratePlaceholder(IntPtr fileHandle, long startingOffset, long length, uint hydrateFlags, IntPtr overlapped);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern int CfGetPlaceholderInfo(SafeFileHandle fileHandle, int infoClass, IntPtr infoBuffer, uint infoBufferLength, out uint returnedLength);

    [DllImport("cldapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int CfGetSyncRootInfoByPath(string filePath, int infoClass, IntPtr infoBuffer, uint infoBufferLength, out uint returnedLength);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern uint CfGetPlaceholderStateFromFindData(in Win32FindData findData);

    [DllImport("cldapi.dll", ExactSpelling = true)]
    public static extern uint CfGetPlaceholderStateFromAttributeTag(uint fileAttributes, uint reparseTag);

    // ---- kernel32.dll ----
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass, IntPtr buffer, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindFirstFileExW(string fileName, int infoLevelId, out Win32FindData findData, int searchOp, IntPtr searchFilter, int additionalFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool FindNextFileW(IntPtr findHandle, out Win32FindData findData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindClose(IntPtr findHandle);
}
