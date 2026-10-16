using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>The Win32 calls the fixtures need: an independent oracle for canonical paths and identity, reparse points,
/// subst and mapped drives, the Recycle Bin of the test user. Tests only: the library has its own P/Invoke.</summary>
internal static unsafe partial class Win32
{
    public const uint FileReadAttributes = 0x80;
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint ShareAll = 0x7;
    public const uint OpenExisting = 3;
    public const uint FlagBackupSemantics = 0x02000000;
    public const uint FlagOpenReparsePoint = 0x00200000;

    public const uint AttrReparsePoint = 0x400;
    public const uint AttrCompressed = 0x800;
    public const uint AttrRecallOnOpen = 0x40000;
    public const uint AttrPinned = 0x80000;
    public const uint AttrRecallOnDataAccess = 0x400000;
    public const uint InvalidAttributes = 0xFFFFFFFF;

    public const uint TagMountPoint = 0xA0000003;
    public const uint TagWofCompressed = 0x80000017;
    /// <summary>A third-party tag (bit 31 clear) the system has no handler for: "unknown" to the gate.</summary>
    public const uint TagUnknownThirdParty = 0x0000AB57;
    private static readonly Guid UnknownTagGuid = new("3b7a4d2e-5c61-4f0b-9a8d-1e2f3a4b5c6d");

    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint FsctlDeleteReparsePoint = 0x000900AC;
    private const uint FsctlSetCompression = 0x0009C040;
    private const int FileIdInfoClass = 18;

    private const uint DddRemoveDefinition = 0x2;
    private const uint DddExactMatchOnRemove = 0x4;

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle handle, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathNameW(string longPath, char* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFileAttributesW(string path);

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileAttributesW(string path, uint attributes);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(string linkPath, string existingPath, IntPtr security);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, byte* input, uint inputSize, byte* output, uint outputSize, out uint returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, byte* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPointW(string mountPoint, char* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "DefineDosDeviceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DefineDosDeviceW(uint flags, string device, string? target);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint QueryDosDeviceW(string device, char* buffer, uint length);

    [LibraryImport("mpr.dll", EntryPoint = "WNetAddConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint WNetAddConnection2W(ref NetResource resource, string? password, string? user, uint flags);

    [LibraryImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint WNetCancelConnection2W(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperationW(ref ShFileOpStruct op);

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string rootPath, ref ShQueryRbInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NetResource
    {
        public uint Scope;
        public uint Type;
        public uint DisplayType;
        public uint Usage;
        public IntPtr LocalName;
        public IntPtr RemoteName;
        public IntPtr Comment;
        public IntPtr Provider;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }

    private static Win32Exception LastError(string what) => new(Marshal.GetLastPInvokeError(), what + ": " + new Win32Exception(Marshal.GetLastPInvokeError()).Message);

    /// <summary>Opens for attributes only, never following a reparse point.</summary>
    public static SafeFileHandle OpenForAttributes(string path)
    {
        var handle = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw LastError("CreateFile(attributes) " + path);
        }
        return handle;
    }

    private static SafeFileHandle OpenForWrite(string path)
    {
        var handle = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw LastError("CreateFile(write) " + path);
        }
        return handle;
    }

    /// <summary>The spec's canonical spelling, from the system: long names, file-system case, subst and mapped drives
    /// resolved, no <c>\\?\</c> prefix on a drive path (a network path keeps its <c>\\?\UNC\</c> form).</summary>
    public static string FinalPath(string path)
    {
        using var handle = OpenForAttributes(path);
        var buffer = new char[32768];
        fixed (char* p = buffer)
        {
            var length = GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length)
            {
                throw LastError("GetFinalPathNameByHandle " + path);
            }
            var final = new string(p, 0, (int)length);
            if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            {
                return final;
            }
            return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        }
    }

    /// <summary>The 8.3 spelling (the whole path), as the file system reports it.</summary>
    public static string ShortPath(string path)
    {
        var buffer = new char[32768];
        fixed (char* p = buffer)
        {
            var length = GetShortPathNameW(path, p, (uint)buffer.Length);
            if (length == 0 || length >= buffer.Length)
            {
                throw LastError("GetShortPathName " + path);
            }
            return new string(p, 0, (int)length);
        }
    }

    public static uint Attributes(string path)
    {
        var attributes = GetFileAttributesW(path);
        if (attributes == InvalidAttributes)
        {
            throw LastError("GetFileAttributes " + path);
        }
        return attributes;
    }

    public static void SetAttributes(string path, uint attributes)
    {
        if (!SetFileAttributesW(path, attributes))
        {
            throw LastError("SetFileAttributes " + path);
        }
    }

    public static void CreateHardLink(string linkPath, string existingPath)
    {
        if (!CreateHardLinkW(linkPath, existingPath, IntPtr.Zero))
        {
            throw LastError("CreateHardLink " + linkPath);
        }
    }

    /// <summary>Turns an empty directory into a junction (what <c>mklink /J</c> does; no privilege needed).</summary>
    public static void SetJunction(string directory, string target)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        var dataLength = 8 + substitute.Length + 2 + print.Length + 2;
        var buffer = new byte[8 + dataLength];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), TagMountPoint);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), (ushort)dataLength);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), (ushort)0);
        BitConverter.TryWriteBytes(buffer.AsSpan(10), (ushort)substitute.Length);
        BitConverter.TryWriteBytes(buffer.AsSpan(12), (ushort)(substitute.Length + 2));
        BitConverter.TryWriteBytes(buffer.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(buffer, 16);
        print.CopyTo(buffer, 16 + substitute.Length + 2);
        Fsctl(directory, FsctlSetReparsePoint, buffer, "FSCTL_SET_REPARSE_POINT (junction) " + directory);
    }

    /// <summary>Gives a file a reparse point with a third-party tag nothing handles: the gate's "unknown" kind.</summary>
    public static void SetUnknownReparsePoint(string file)
    {
        var buffer = new byte[24 + 8];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), TagUnknownThirdParty);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), (ushort)8);
        BitConverter.TryWriteBytes(buffer.AsSpan(6), (ushort)0);
        UnknownTagGuid.TryWriteBytes(buffer.AsSpan(8));
        Fsctl(file, FsctlSetReparsePoint, buffer, "FSCTL_SET_REPARSE_POINT (unknown tag) " + file);
    }

    /// <summary>Removes a reparse point the tests set, so the fixture can be deleted like a plain item.</summary>
    public static void DeleteReparsePoint(string path, uint tag)
    {
        var isMicrosoft = (tag & 0x80000000) != 0;
        var buffer = new byte[isMicrosoft ? 8 : 24];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), tag);
        if (!isMicrosoft)
        {
            UnknownTagGuid.TryWriteBytes(buffer.AsSpan(8));
        }
        Fsctl(path, FsctlDeleteReparsePoint, buffer, "FSCTL_DELETE_REPARSE_POINT " + path);
    }

    /// <summary>NTFS (LZNT1) compression: an attribute, not a reparse point.</summary>
    public static void SetNtfsCompression(string file)
    {
        var format = BitConverter.GetBytes((ushort)1);
        Fsctl(file, FsctlSetCompression, format, "FSCTL_SET_COMPRESSION " + file);
    }

    private static void Fsctl(string path, uint code, byte[] input, string what)
    {
        using var handle = OpenForWrite(path);
        fixed (byte* p = input)
        {
            if (!DeviceIoControl(handle, code, p, (uint)input.Length, null, 0, out _, IntPtr.Zero))
            {
                throw LastError(what);
            }
        }
    }

    /// <summary>The volume serial and the 16 bytes of the 128-bit file id (FILE_ID_INFO), as the system reports them.</summary>
    public static (ulong VolumeSerial, byte[] FileId) FileIdInfo(string path)
    {
        using var handle = OpenForAttributes(path);
        var buffer = new byte[24];
        fixed (byte* p = buffer)
        {
            if (!GetFileInformationByHandleEx(handle, FileIdInfoClass, p, (uint)buffer.Length))
            {
                throw LastError("GetFileInformationByHandleEx(FileIdInfo) " + path);
            }
        }
        return (BitConverter.ToUInt64(buffer, 0), buffer[8..24]);
    }

    /// <summary>Whether FILE_ID_INFO can be read at all (FAT and exFAT refuse it with ERROR_INVALID_PARAMETER).</summary>
    public static bool HasFileIdInfo(string path)
    {
        using var handle = OpenForAttributes(path);
        var buffer = new byte[24];
        fixed (byte* p = buffer)
        {
            return GetFileInformationByHandleEx(handle, FileIdInfoClass, p, (uint)buffer.Length);
        }
    }

    /// <summary>The volume GUID of a drive root such as <c>C:\</c>, with braces, as the Recycle Bin keys name it.</summary>
    public static string VolumeGuid(string root)
    {
        var buffer = new char[64];
        fixed (char* p = buffer)
        {
            if (!GetVolumeNameForVolumeMountPointW(root, p, (uint)buffer.Length))
            {
                throw LastError("GetVolumeNameForVolumeMountPoint " + root);
            }
            var name = new string(p, 0, Array.IndexOf(buffer, '\0'));
            var open = name.IndexOf('{', StringComparison.Ordinal);
            var close = name.IndexOf('}', StringComparison.Ordinal);
            return name.Substring(open, close - open + 1);
        }
    }

    public static void DefineDosDevice(string device, string target)
    {
        if (!DefineDosDeviceW(0, device, target))
        {
            throw LastError("DefineDosDevice " + device);
        }
    }

    public static void RemoveDosDevice(string device, string target)
    {
        if (!DefineDosDeviceW(DddRemoveDefinition | DddExactMatchOnRemove, device, target))
        {
            throw LastError("DefineDosDevice(remove) " + device);
        }
    }

    public static bool IsDosDeviceDefined(string device)
    {
        var buffer = new char[1024];
        fixed (char* p = buffer)
        {
            return QueryDosDeviceW(device, p, (uint)buffer.Length) != 0;
        }
    }

    public static uint AddNetworkConnection(string localName, string remoteName)
    {
        var resource = new NetResource { Type = 1, LocalName = Marshal.StringToHGlobalUni(localName), RemoteName = Marshal.StringToHGlobalUni(remoteName) };
        try
        {
            return WNetAddConnection2W(ref resource, null, null, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(resource.LocalName);
            Marshal.FreeHGlobal(resource.RemoteName);
        }
    }

    public static uint CancelNetworkConnection(string localName) => WNetCancelConnection2W(localName, 0, true);

    /// <summary>Moves one of the test's own files to the Recycle Bin through the shell (a fixture for the "full" rule;
    /// the library never moves anything).</summary>
    public static void RecycleOwnFile(string path)
    {
        var from = Marshal.StringToHGlobalUni(path + "\0");
        try
        {
            var op = new ShFileOpStruct { Func = FoDelete, From = from, Flags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent };
            var result = SHFileOperationW(ref op);
            if (result != 0 || op.AnyOperationsAborted != 0)
            {
                throw new InvalidOperationException($"SHFileOperation(recycle) failed: 0x{result:X8}, aborted={op.AnyOperationsAborted}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    /// <summary>Bytes this user's Recycle Bin holds on the volume of <paramref name="root"/>.</summary>
    public static long RecycleBinBytes(string root)
    {
        var info = new ShQueryRbInfo { Size = (uint)Marshal.SizeOf<ShQueryRbInfo>() };
        var result = SHQueryRecycleBinW(root, ref info);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHQueryRecycleBin({root}) failed: 0x{result:X8}");
        }
        return info.Bytes;
    }

    private const uint FsctlGetExternalBacking = 0x00090310;
    private const int ErrorObjectNotExternallyBacked = 342;

    /// <summary>Whether a file is WOF-backed (compact /exe). Measured on the test machine: such a file is a reparse
    /// point on disk (fsutil file layout: tag 0x80000017) while GetFileAttributes, FindFirstFile and
    /// FSCTL_GET_REPARSE_POINT hide it; FSCTL_GET_EXTERNAL_BACKING is the user-mode way to see it.</summary>
    public static bool IsWofBacked(string path)
    {
        using var handle = OpenForAttributes(path);
        var buffer = new byte[64];
        fixed (byte* p = buffer)
        {
            if (DeviceIoControl(handle, FsctlGetExternalBacking, null, 0, p, (uint)buffer.Length, out _, IntPtr.Zero))
            {
                return true;
            }
            var error = Marshal.GetLastPInvokeError();
            if (error == ErrorObjectNotExternallyBacked)
            {
                return false;
            }
            throw new Win32Exception(error, "FSCTL_GET_EXTERNAL_BACKING " + path);
        }
    }

    /// <summary>FOLDERID_Pictures.</summary>
    public static readonly Guid PicturesFolderId = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [LibraryImport("shell32.dll", EntryPoint = "SHSetKnownFolderPath", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHSetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, string path);

    /// <summary>The known folder's current path, from the shell (no default fallback, no verification).</summary>
    public static string KnownFolderPath(Guid folderId)
    {
        var result = SHGetKnownFolderPath(folderId, 0x00004000 /* KF_FLAG_DONT_VERIFY */, IntPtr.Zero, out var pointer);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHGetKnownFolderPath failed: 0x{result:X8}");
        }
        try
        {
            return Marshal.PtrToStringUni(pointer)!;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>The known folder's default path for this user (where a redirect is restored to after a hard kill).</summary>
    public static string KnownFolderDefaultPath(Guid folderId)
    {
        var result = SHGetKnownFolderPath(folderId, 0x00000400 /* KF_FLAG_DEFAULT_PATH */ | 0x00004000 /* KF_FLAG_DONT_VERIFY */, IntPtr.Zero, out var pointer);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHGetKnownFolderPath(default) failed: 0x{result:X8}");
        }
        try
        {
            return Marshal.PtrToStringUni(pointer)!;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>Redirects a known folder of the current user (the shell writes the user's own setting; nothing is moved).</summary>
    public static void SetKnownFolderPath(Guid folderId, string path)
    {
        var result = SHSetKnownFolderPath(folderId, 0, IntPtr.Zero, path);
        if (result != 0)
        {
            throw new InvalidOperationException($"SHSetKnownFolderPath({path}) failed: 0x{result:X8}");
        }
    }
}
