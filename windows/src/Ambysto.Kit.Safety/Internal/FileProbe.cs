using System;
using System.ComponentModel;
using Ambysto.Kit.Safety.Native;
using Microsoft.Win32.SafeHandles;
using static Ambysto.Kit.Safety.Native.NativeMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>What one open, for attributes and security only, tells about an item. Never opens for data.</summary>
internal sealed class FileProbe : IDisposable
{
    private FileProbe(SafeFileHandle handle, string path) { Handle = handle; Path = path; }

    internal SafeFileHandle Handle { get; }

    /// <summary>The plain path it was opened by (for the fault seam).</summary>
    internal string Path { get; }

    public void Dispose() => Handle.Dispose();

    /// <summary>The attributes without opening (Windows step 2: an item marked recall-on-open is never opened).</summary>
    internal static (uint Attributes, int Error) Attributes(string plain)
    {
        if (Faults.Ok(Faults.Query.AttributesByName, GetFileAttributesEx(PathSpelling.Extended(plain), 0, out var data), plain))
        {
            return (Faults.Entry(Faults.Query.AttributesByName, plain, data.FileAttributes, 0).Attributes, 0);
        }
        return (0, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
    }

    internal static bool IsNotFound(int error) => error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_INVALID_NAME or ERROR_BAD_NETPATH or ERROR_NOT_READY;

    /// <summary>Opens for attributes and the security descriptor, never following a reparse point.</summary>
    internal static (FileProbe? Probe, int Error) Open(string plain, bool readControl = true)
    {
        Faults.Point(Faults.Query.Open, plain);
        var access = FILE_READ_ATTRIBUTES | (readControl ? READ_CONTROL : 0);
        var handle = CreateFile(PathSpelling.Extended(plain), access, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            handle.Dispose();
            return (null, error);
        }
        return (new FileProbe(handle, plain), 0);
    }

    /// <summary>Opens what a link points to (following it), for attributes only: a folder, never data.</summary>
    internal static (FileProbe? Probe, int Error) OpenTarget(string plain)
    {
        Faults.Point(Faults.Query.Open, plain);
        var handle = CreateFile(PathSpelling.Extended(plain), FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            handle.Dispose();
            return (null, error);
        }
        return (new FileProbe(handle, plain), 0);
    }

    internal unsafe (uint Attributes, uint Tag) AttributeTag()
    {
        FILE_ATTRIBUTE_TAG_INFO info;
        if (!Faults.Ok(Faults.Query.AttributeTag, GetFileInformationByHandleEx(Handle, FileInfoClass.FileAttributeTagInfo, &info, (uint)sizeof(FILE_ATTRIBUTE_TAG_INFO)), Path))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "attribute tag");
        }
        return (info.FileAttributes, info.ReparseTag);
    }

    internal unsafe FileIdentity Identity()
    {
        FILE_ID_INFO info;
        if (!Faults.Ok(Faults.Query.FileId, GetFileInformationByHandleEx(Handle, FileInfoClass.FileIdInfo, &info, (uint)sizeof(FILE_ID_INFO)), Path))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "file id");
        }
        return new FileIdentity(info.VolumeSerialNumber, new UInt128(info.FileIdHigh, info.FileIdLow));
    }

    /// <summary>The clusters the file uses (<c>FILE_STANDARD_INFO.AllocationSize</c>), read from the handle.</summary>
    internal unsafe long AllocationSize()
    {
        FILE_STANDARD_INFO standard = default;
        if (!Faults.Ok(Faults.Query.AllocatedSize, GetFileInformationByHandleEx(Handle, FileInfoClass.FileStandardInfo, &standard, (uint)sizeof(FILE_STANDARD_INFO)), Path))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "allocated size of " + Path);
        }
        return standard.AllocationSize;
    }

    internal unsafe FileStamp Stamp()
    {
        var identity = Identity();
        FILE_BASIC_INFO basic;
        FILE_STANDARD_INFO standard = default;
        if (!Faults.Ok(Faults.Query.TimesAndLength, GetFileInformationByHandleEx(Handle, FileInfoClass.FileBasicInfo, &basic, (uint)sizeof(FILE_BASIC_INFO))
            && GetFileInformationByHandleEx(Handle, FileInfoClass.FileStandardInfo, &standard, (uint)sizeof(FILE_STANDARD_INFO)), Path))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "file times and length");
        }
        return new FileStamp(identity, basic.LastWriteTime, standard.Directory != 0 ? null : standard.EndOfFile);
    }

    /// <summary>The final path from the handle, as returned (<c>\\?\C:\...</c> or <c>\\?\UNC\...</c>).</summary>
    internal string FinalPath() => FinalPath(VOLUME_NAME_DOS);

    internal unsafe string FinalPath(uint volumeName)
    {
        var size = 1024u;
        while (true)
        {
            var buffer = new char[size];
            uint length;
            fixed (char* b = buffer)
            {
                length = GetFinalPathNameByHandle(Handle, b, size, FILE_NAME_NORMALIZED | volumeName);
            }
            if (!Faults.Ok(Faults.Query.FinalPath, length != 0, Path))
            {
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "final path");
            }
            if (length < size)
            {
                return new string(buffer, 0, (int)length);
            }
            size = length + 1;
        }
    }

    internal unsafe string FileSystemName()
    {
        var name = new char[64];
        fixed (char* n = name)
        {
            if (!Faults.Ok(Faults.Query.FileSystem, GetVolumeInformationByHandle(Handle, null, 0, out _, out _, out _, n, (uint)name.Length), Path))
            {
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "file system");
            }
        }
        return new string(name).TrimEnd('\0');
    }

    /// <summary>The volume root that holds a plain path (a drive root or a mount folder), with a trailing backslash.</summary>
    internal static unsafe string VolumeRoot(string plain)
    {
        var buffer = new char[32768];
        fixed (char* b = buffer)
        {
            if (!Faults.Ok(Faults.Query.VolumeRoot, GetVolumePathName(PathSpelling.Extended(plain), b, (uint)buffer.Length), plain))
            {
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "volume root");
            }
        }
        var root = new string(buffer).TrimEnd('\0');
        if (root.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            root = root[4..];
        }
        return root.EndsWith('\\') ? root : root + "\\";
    }

    internal const uint TagWof = 0x80000017;

    /// <summary>Windows step 3's allow-list: no reparse point, a cloud placeholder tag, or WOF compression.</summary>
    internal static bool TagAllowed(uint attributes, uint tag) =>
        (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0 || IsCloudTag(tag) || tag == TagWof;

    internal static bool IsCloudTag(uint tag) => (tag & ~0xF000u) == 0x9000001Au;
}
