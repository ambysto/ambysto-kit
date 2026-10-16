// `status` and the directory walker shared with `dehydrate`.
//
// HYDRATION SAFETY: nothing here reads file data. Directories are listed with FindFirstFileExW (a directory query), and
// each entry is opened with access FILE_READ_ATTRIBUTES only (no FILE_READ_DATA, so no read can be issued on the handle),
// FILE_FLAG_OPEN_REPARSE_POINT and FILE_FLAG_OPEN_NO_RECALL (the documented "do not bring this back from remote storage"
// open). ReadFile / memory mapping / any data API is never called. The self-test proves it by watching the serve log.
using System.Runtime.InteropServices;

namespace Ambysto.Kit.Safety.CloudFixture;

internal sealed record WalkEntry(string Rel, string Full, Win32FindData Find);

internal static class Inspect
{
    public static List<WalkEntry> Walk(string root)
    {
        var list = new List<WalkEntry>();
        WalkDir(root, "", list);
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Rel, b.Rel));
        return list;
    }

    static void WalkDir(string dir, string rel, List<WalkEntry> list)
    {
        // FindExInfoBasic = 1, FindExSearchNameMatch = 0, FIND_FIRST_EX_LARGE_FETCH = 2
        IntPtr h = Native.FindFirstFileExW(dir + @"\*", 1, out var fd, 0, IntPtr.Zero, 2);
        if (h == new IntPtr(-1))
        {
            int err = Marshal.GetLastWin32Error();
            throw new CfException($"list {dir}", unchecked((int)(0x80070000u | (uint)err)));
        }
        var subdirs = new List<(string Name, Win32FindData Fd)>();
        try
        {
            do
            {
                if (fd.cFileName == "." || fd.cFileName == "..") continue;
                var entryRel = rel.Length == 0 ? fd.cFileName : rel + "\\" + fd.cFileName;
                list.Add(new WalkEntry(entryRel, dir + "\\" + fd.cFileName, fd));
                if ((fd.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0) subdirs.Add((fd.cFileName, fd));
            } while (Native.FindNextFileW(h, out fd));
            int err = Marshal.GetLastWin32Error();
            if (err != 18 /* ERROR_NO_MORE_FILES */) throw new CfException($"list {dir}", unchecked((int)(0x80070000u | (uint)err)));
        }
        finally { Native.FindClose(h); }

        foreach (var (name, sfd) in subdirs)
        {
            // Do not follow foreign reparse points (junctions/symlinks). Cloud placeholders do not show the
            // REPARSE_POINT bit in a directory listing at all (verified on this VM), so they are never skipped here;
            // the tag test only matters if some listing does expose the bit: cloud tags are 0x9000001A + n*0x1000.
            bool reparse = (sfd.dwFileAttributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            if (reparse && (sfd.dwReserved0 & 0xFFFF0FFF) != 0x9000001A) continue;
            // An on-demand (unpopulated) directory placeholder: listing it would ask the provider for its children.
            // It is reported from the parent's listing and never entered.
            if ((sfd.dwFileAttributes & Native.FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0) continue;
            WalkDir(dir + "\\" + name, rel.Length == 0 ? name : rel + "\\" + name, list);
        }
    }
}

internal static class StatusCommand
{
    static readonly (uint Bit, string Name)[] AttrNames =
    {
        (0x1, "READONLY"), (0x2, "HIDDEN"), (0x4, "SYSTEM"), (0x10, "DIRECTORY"), (0x20, "ARCHIVE"), (0x80, "NORMAL"),
        (0x100, "TEMPORARY"), (0x200, "SPARSE_FILE"), (0x400, "REPARSE_POINT"), (0x800, "COMPRESSED"),
        (0x1000, "OFFLINE"), (0x2000, "NOT_CONTENT_INDEXED"), (0x4000, "ENCRYPTED"),
        (0x40000, "RECALL_ON_OPEN"), (0x80000, "PINNED"), (0x100000, "UNPINNED"), (0x400000, "RECALL_ON_DATA_ACCESS"),
    };

    static readonly (uint Bit, string Name)[] StateNames =
    {
        (0x1, "PLACEHOLDER"), (0x2, "SYNC_ROOT"), (0x4, "ESSENTIAL_PROP_PRESENT"), (0x8, "IN_SYNC"),
        (0x10, "PARTIAL"), (0x20, "PARTIALLY_ON_DISK"),
    };

    static List<string> Names((uint Bit, string Name)[] table, uint value) =>
        table.Where(t => (value & t.Bit) != 0).Select(t => t.Name).ToList();

    static string PinName(int pin) => pin switch { 0 => "UNSPECIFIED", 1 => "PINNED", 2 => "UNPINNED", 3 => "EXCLUDED", _ => $"UNKNOWN({pin})" };

    public static int Run(Opts o)
    {
        var root = o.Root();
        if (!Directory.Exists(root)) throw new ArgumentException($"root does not exist: {root}");
        var entries = Inspect.Walk(root);
        Out.Raw("[");
        for (int i = 0; i < entries.Count; i++)
            Out.Raw(Out.Json(Describe(entries[i])) + (i + 1 < entries.Count ? "," : ""));
        Out.Raw("]");
        return 0;
    }

    static Dictionary<string, object?> Describe(WalkEntry e)
    {
        var fd = e.Find;
        uint listingAttrs = fd.dwFileAttributes;
        bool isDir = (listingAttrs & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
        long size = isDir ? 0 : ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
        long lastWrite = ((long)fd.ftLastWriteTime.dwHighDateTime << 32) | (uint)fd.ftLastWriteTime.dwLowDateTime;

        var d = new Dictionary<string, object?>
        {
            ["path"] = e.Rel,
            ["isDirectory"] = isDir,
            ["size"] = size,
            ["isPlaceholder"] = false,
            ["hydration"] = "unknown",
            ["onDisk"] = null,
            ["validated"] = null,
            ["allocationSize"] = null,
            ["inSync"] = null,
            ["pinState"] = null,
            ["fileIdentity"] = null,
            ["lastWriteUtc"] = lastWrite > 0 ? DateTime.FromFileTimeUtc(lastWrite).ToString("o") : null,
        };
        void Attrs(uint attrs)
        {
            d["attributes"] = Names(AttrNames, attrs);
            d["attributesRaw"] = $"0x{attrs:X8}";
            d["recallOnDataAccess"] = (attrs & Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0;
            d["recallOnOpen"] = (attrs & Native.FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0;
            d["pinned"] = (attrs & Native.FILE_ATTRIBUTE_PINNED) != 0;
            d["unpinned"] = (attrs & Native.FILE_ATTRIBUTE_UNPINNED) != 0;
            d["offline"] = (attrs & Native.FILE_ATTRIBUTE_OFFLINE) != 0;
        }
        Attrs(listingAttrs); // replaced below by the handle's view when the open works
        d["listingAttributesRaw"] = $"0x{listingAttrs:X8}"; // what a directory listing shows (no REPARSE_POINT bit for placeholders)
        d["reparseTag"] = null;
        d["state"] = new List<string>();
        d["stateRaw"] = "0x00000000";

        // Query-only handle: FILE_READ_ATTRIBUTES, never data access, never recall.
        using var h = Native.CreateFileW(e.Full, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_ALL, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_FLAG_OPEN_REPARSE_POINT | Native.FILE_FLAG_OPEN_NO_RECALL | Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            d["error"] = CfException.Describe("open for query", unchecked((int)(0x80070000u | (uint)err)));
            return d;
        }

        IntPtr buf = Marshal.AllocHGlobal(8192), ibuf = Marshal.AllocHGlobal(8192);
        try
        {
            // FileAttributeTagInfo = 9: { DWORD FileAttributes; DWORD ReparseTag }.
            // CAUTION (verified on this VM): the attribute view depends on the calling process. powershell.exe, pwsh.exe and
            // cmd.exe see REPARSE_POINT|OFFLINE|SPARSE_FILE on a placeholder; AmbyCloud.exe (like python.exe or dotnet.exe)
            // sees only RECALL_ON_DATA_ACCESS (and ARCHIVE/DIRECTORY), and ReparseTag reads 0 here. So "attributes" below is
            // what THIS process sees, and the reparse tag is read separately with FSCTL_GET_REPARSE_POINT (metadata only).
            uint attrs = listingAttrs;
            if (Native.GetFileInformationByHandleEx(h, 9, buf, 8)) attrs = (uint)Marshal.ReadInt32(buf, 0);
            else d["tagInfoError"] = CfException.Describe("GetFileInformationByHandleEx(FileAttributeTagInfo)", unchecked((int)(0x80070000u | (uint)Marshal.GetLastWin32Error())));
            Attrs(attrs);

            uint tag = 0;
            if (Native.DeviceIoControl(h, Native.FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buf, 8192, out uint got, IntPtr.Zero) && got >= 4)
                tag = (uint)Marshal.ReadInt32(buf, 0);
            d["reparseTag"] = tag != 0 ? $"0x{tag:X8}" : null;
            d["reparseVisibleInAttributes"] = (attrs & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            uint state = Native.CfGetPlaceholderStateFromAttributeTag(tag != 0 ? attrs | Native.FILE_ATTRIBUTE_REPARSE_POINT : attrs, tag);
            d["state"] = state == Native.CF_PLACEHOLDER_STATE_INVALID ? new List<string> { "INVALID" } : Names(StateNames, state);
            d["stateRaw"] = $"0x{state:X8}";

            // CF_PLACEHOLDER_STANDARD_INFO: OnDiskDataSize@0 ValidatedDataSize@8 ModifiedDataSize@16 PropertiesSize@24
            // PinState@32 InSyncState@36 FileId@40 SyncRootFileId@48 FileIdentityLength@56 FileIdentity@60.
            // Success of this call is the authoritative "this is a cloud placeholder".
            int hr = Native.CfGetPlaceholderInfo(h, 1, ibuf, 8192, out _);
            bool isPlaceholder = hr >= 0;
            d["isPlaceholder"] = isPlaceholder;

            // Allocation size as the file system reports it (FileStandardInfo = 1: AllocationSize, EndOfFile, ...).
            if (Native.GetFileInformationByHandleEx(h, 1, buf, 24)) d["allocationSize"] = Marshal.ReadInt64(buf, 0);

            if (!isPlaceholder)
            {
                d["hydration"] = "not-a-placeholder";
                d["infoError"] = CfException.Describe("CfGetPlaceholderInfo", hr);
                return d;
            }
            long onDisk = Marshal.ReadInt64(ibuf, 0);
            d["onDisk"] = onDisk;
            d["validated"] = Marshal.ReadInt64(ibuf, 8);
            d["pinState"] = PinName(Marshal.ReadInt32(ibuf, 32));
            d["inSync"] = Marshal.ReadInt32(ibuf, 36) == 1;
            int idLen = Marshal.ReadInt32(ibuf, 56);
            if (idLen > 0 && idLen % 2 == 0 && idLen <= 8192 - 60) d["fileIdentity"] = Marshal.PtrToStringUni(ibuf + 60, idLen / 2);

            // The platform's own flag decoder needs the reparse tag, which this process cannot see (see CAUTION above), so
            // "state" is the platform's answer (when it has one) plus what CfGetPlaceholderInfo proves.
            var flags = new List<string>(Names(StateNames, state == Native.CF_PLACEHOLDER_STATE_INVALID ? 0 : state));
            void Add(string n) { if (!flags.Contains(n)) flags.Add(n); }
            Add("PLACEHOLDER");
            if (Marshal.ReadInt32(ibuf, 36) == 1) Add("IN_SYNC");
            if (!isDir && onDisk > 0 && onDisk < size) Add("PARTIALLY_ON_DISK");
            d["state"] = flags;
            d["stateSource"] = "platform flags (needs reparse tag) + derived from CfGetPlaceholderInfo";

            if (isDir) d["hydration"] = "n/a";
            else
            {
                bool recall = (attrs & Native.FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0;
                d["hydration"] =
                    recall && onDisk == 0 ? "dehydrated" :
                    !recall && onDisk >= size ? "hydrated" :
                    onDisk > 0 && onDisk < size ? "partial" :
                    $"inconsistent(recallOnDataAccess={recall},onDisk={onDisk},size={size})";
            }
        }
        finally { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(ibuf); }
        return d;
    }
}
