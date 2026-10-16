using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static Ambysto.Kit.Safety.Native.InUseMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>docs/spec/safety.md "In use", Windows note: any method that names a holder makes the item in use.</summary>
internal static class InUse
{
    internal static InUseResult Check(ApprovedItem item)
    {
        var tree = TreeWalk.Measure(item.CanonicalPath, FileProbe.Attributes(item.CanonicalPath).Attributes);
        var holders = new List<InUseHolder>();
        var names = new Dictionary<int, string?>();
        void Add(int pid, string method)
        {
            if (pid == Environment.ProcessId || holders.Exists(h => h.ProcessId == pid && h.Method == method))
            {
                return;
            }
            if (!names.TryGetValue(pid, out var name))
            {
                name = ImageName(pid);
                names[pid] = name;
            }
            holders.Add(new InUseHolder(pid, name, method));
        }

        // 1. The per-file query, on every file and folder of the tree.
        foreach (var path in tree.Files.Concat(tree.Folders))
        {
            foreach (var pid in ProcessesUsing(path))
            {
                Add(pid, "open file");
            }
        }

        // 2. The Restart Manager (files only: it refuses folders), listing only.
        foreach (var (pid, _) in RestartManager(tree.Files))
        {
            Add(pid, "Restart Manager");
        }

        // 3. Readable processes: image, current directory, command line.
        var unreadable = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id is 0 or 4 || process.Id == Environment.ProcessId)
                {
                    continue;
                }
                var facts = ProcessFacts(process.Id);
                if (facts is null)
                {
                    unreadable++;
                    continue;
                }
                var (image, cwd, commandLine) = facts.Value;
                if (image is not null && Within(image, item.CanonicalPath)) { Add(process.Id, "image"); }
                if (cwd is not null && Within(cwd, item.CanonicalPath)) { Add(process.Id, "current directory"); }
                if (commandLine is not null && ArgumentNames(commandLine, cwd, item.CanonicalPath)) { Add(process.Id, "command line"); }
            }
        }
        return new InUseResult(holders, unreadable);
    }

    internal static bool Within(string path, string item)
    {
        var p = path.TrimEnd('\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) { p = p[4..]; }
        return Canonical.SamePath(p, item) || Cloud.IsInside(p, item);
    }

    private static unsafe List<int> ProcessesUsing(string path)
    {
        var result = new List<int>();
        var (probe, error) = FileProbe.Open(path, readControl: false);
        if (probe is null)
        {
            throw new System.ComponentModel.Win32Exception(error, "open " + path);
        }
        using (probe)
        {
            var size = 4096u;
            while (true)
            {
                var buffer = new byte[size];
                int status;
                fixed (byte* b = buffer)
                {
                    status = NtQueryInformationFile(probe.Handle, out _, b, size, FileProcessIdsUsingFileInformation);
                    if (status == STATUS_INFO_LENGTH_MISMATCH && size < 1 << 20)
                    {
                        size *= 4;
                        continue;
                    }
                    if (status < 0)
                    {
                        throw new System.ComponentModel.Win32Exception("processes using " + path + ": NTSTATUS 0x" + status.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    var count = *(uint*)b;
                    var ids = (nuint*)(b + IntPtr.Size);
                    for (var i = 0; i < count; i++)
                    {
                        result.Add((int)ids[i]);
                    }
                }
                return result;
            }
        }
    }

    private static unsafe List<(int Pid, int Type)> RestartManager(List<string> files)
    {
        var holders = new List<(int, int)>();
        if (files.Count == 0)
        {
            return holders;
        }
        var key = stackalloc char[33];
        var rc = RmStartSession(out var session, 0, key);
        if (rc != 0)
        {
            throw new System.ComponentModel.Win32Exception(rc, "Restart Manager session");
        }
        try
        {
            for (var i = 0; i < files.Count; i += 100)
            {
                var batch = files.GetRange(i, Math.Min(100, files.Count - i)).ConvertAll(f => f.Length < 260 ? f : PathSpelling.Extended(f)).ToArray();
                rc = RmRegisterResources(session, (uint)batch.Length, batch, 0, IntPtr.Zero, 0, IntPtr.Zero);
                if (rc != 0)
                {
                    throw new System.ComponentModel.Win32Exception(rc, "Restart Manager register");
                }
            }
            uint count = 0;
            rc = RmGetList(session, out var needed, ref count, null, out _);
            while (rc == ERROR_MORE_DATA)
            {
                count = needed;
                var list = new RM_PROCESS_INFO[count];
                fixed (RM_PROCESS_INFO* p = list)
                {
                    rc = RmGetList(session, out needed, ref count, p, out _);
                    if (rc == 0)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            holders.Add((list[i].ProcessId, list[i].ApplicationType));
                        }
                    }
                }
            }
            if (rc != 0)
            {
                throw new System.ComponentModel.Win32Exception(rc, "Restart Manager list");
            }
            return holders;
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    private static unsafe string? ImageName(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? Path.GetFileName(new string(buffer, 0, (int)size)) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Image path, current directory and command line of a process; null when it cannot be read.</summary>
    private static unsafe (string? Image, string? Cwd, string? CommandLine)? ProcessFacts(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            if (NtQueryInformationProcess(process, 0, out var basic, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) < 0 || basic.PebBaseAddress == IntPtr.Zero)
            {
                return null;
            }
            IntPtr parameters;
            if (!ReadProcessMemory(process, basic.PebBaseAddress + 0x20, &parameters, IntPtr.Size, out _))
            {
                return null;
            }
            // RTL_USER_PROCESS_PARAMETERS (x64): CurrentDirectory.DosPath at 0x38, ImagePathName at 0x60, CommandLine at 0x70.
            var cwd = ReadUnicodeString(process, parameters + 0x38);
            var image = ReadUnicodeString(process, parameters + 0x60);
            var commandLine = ReadUnicodeString(process, parameters + 0x70);
            return (image, cwd, commandLine);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static unsafe string? ReadUnicodeString(IntPtr process, IntPtr address)
    {
        var header = stackalloc byte[16]; // USHORT Length, USHORT MaximumLength, (padding), PWSTR Buffer
        if (!ReadProcessMemory(process, address, header, 16, out _))
        {
            return null;
        }
        var length = *(ushort*)header;
        var buffer = *(IntPtr*)(header + 8);
        if (length == 0 || buffer == IntPtr.Zero)
        {
            return null;
        }
        var chars = new char[length / 2];
        fixed (char* c = chars)
        {
            if (!ReadProcessMemory(process, buffer, c, length, out _))
            {
                return null;
            }
        }
        return new string(chars);
    }

    private static bool ArgumentNames(string commandLine, string? cwd, string item)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == IntPtr.Zero)
        {
            return false;
        }
        try
        {
            for (var i = 0; i < count; i++)
            {
                var arg = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }
                string full;
                try
                {
                    full = Path.IsPathRooted(arg) ? Path.GetFullPath(arg) : cwd is null ? arg : Path.GetFullPath(Path.Combine(cwd, arg));
                }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
                catch (PathTooLongException) { continue; }
                if (Within(full, item))
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}
