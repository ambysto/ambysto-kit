using System;
using System.ComponentModel;
using Ambysto.Kit.Safety.Native;
using Microsoft.Win32.SafeHandles;
using static Ambysto.Kit.Safety.Native.NativeMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>What one open, for attributes and security only, tells about an item. Never opens for data.</summary>
internal sealed class FileProbe : IDisposable
{
    private FileProbe(SafeFileHandle handle) { Handle = handle; }

    internal SafeFileHandle Handle { get; }

    public void Dispose() => Handle.Dispose();

    /// <summary>The attributes without opening (Windows step 2: an item marked recall-on-open is never opened).</summary>
    internal static (uint Attributes, int Error) Attributes(string plain)
    {
        if (GetFileAttributesEx(PathSpelling.Extended(plain), 0, out var data))
        {
            return (data.FileAttributes, 0);
        }
        return (0, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
    }

    internal static bool IsNotFound(int error) => error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_INVALID_NAME or ERROR_BAD_NETPATH or ERROR_NOT_READY;

    /// <summary>Opens for attributes and the security descriptor, never following a reparse point.</summary>
    internal static (FileProbe? Probe, int Error) Open(string plain, bool readControl = true)
    {
        var access = FILE_READ_ATTRIBUTES | (readControl ? READ_CONTROL : 0);
        var handle = CreateFile(PathSpelling.Extended(plain), access, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            handle.Dispose();
            return (null, error);
        }
        return (new FileProbe(handle), 0);
    }

    internal unsafe (uint Attributes, uint Tag) AttributeTag()
    {
        FILE_ATTRIBUTE_TAG_INFO info;
        if (!GetFileInformationByHandleEx(Handle, FileInfoClass.FileAttributeTagInfo, &info, (uint)sizeof(FILE_ATTRIBUTE_TAG_INFO)))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "attribute tag");
        }
        return (info.FileAttributes, info.ReparseTag);
    }

    internal unsafe FileIdentity Identity()
    {
        FILE_ID_INFO info;
        if (!GetFileInformationByHandleEx(Handle, FileInfoClass.FileIdInfo, &info, (uint)sizeof(FILE_ID_INFO)))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "file id");
        }
        return new FileIdentity(info.VolumeSerialNumber, new UInt128(info.FileIdHigh, info.FileIdLow));
    }

    internal unsafe FileStamp Stamp()
    {
        var identity = Identity();
        FILE_BASIC_INFO basic;
        FILE_STANDARD_INFO standard;
        if (!GetFileInformationByHandleEx(Handle, FileInfoClass.FileBasicInfo, &basic, (uint)sizeof(FILE_BASIC_INFO))
            || !GetFileInformationByHandleEx(Handle, FileInfoClass.FileStandardInfo, &standard, (uint)sizeof(FILE_STANDARD_INFO)))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "file times and length");
        }
        return new FileStamp(identity, basic.LastWriteTime, standard.Directory != 0 ? null : standard.EndOfFile);
    }

    /// <summary>The final path from the handle, as returned (<c>\\?\C:\...</c> or <c>\\?\UNC\...</c>).</summary>
    internal unsafe string FinalPath()
    {
        var size = 1024u;
        while (true)
        {
            var buffer = new char[size];
            uint length;
            fixed (char* b = buffer)
            {
                length = GetFinalPathNameByHandle(Handle, b, size, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            }
            if (length == 0)
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
            if (!GetVolumeInformationByHandle(Handle, null, 0, out _, out _, out _, n, (uint)name.Length))
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
            if (!GetVolumePathName(PathSpelling.Extended(plain), b, (uint)buffer.Length))
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
