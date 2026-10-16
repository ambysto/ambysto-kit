// `serve`: connect to the sync root and answer cloud-file callbacks.
// Deterministic content: byte i of every file = (i*31 + 7) & 0xFF.
using System.Runtime.InteropServices;

namespace Ambysto.Kit.Safety.CloudFixture;

internal sealed class Server
{
    const long Align = 4096;          // TRANSFER_DATA offset/length granularity (except a transfer that ends at EOF)
    const int ChunkSize = 1 << 20;    // 1 MiB, a multiple of Align

    readonly string _root;
    readonly bool _deny;
    readonly EventLog? _logFile;
    EventLogProxy _log => new(_logFile);
    readonly struct EventLogProxy(EventLog? l) { public void Write(string ev, params (string Key, object? Value)[] f) => l?.Write(ev, f); }

    // Delegates are fields so the GC cannot collect them while native code holds the function pointers.
    readonly CfCallback _onFetch, _onCancel, _onDelete, _onDeleteDone, _onRename, _onRenameDone, _onDehydrate, _onDehydrateDone;

    Server(string root, bool deny, EventLog? log)
    {
        _root = root; _deny = deny; _logFile = log;
        _onFetch = (i, p) => Guard("fetch", i, p, OnFetch, (c, q) => { ReadFetchRange(q, c, out var o, out var l); Fail(c, o, l); });
        _onCancel = (i, p) => Guard("cancel-fetch", i, p, OnCancel);
        _onDelete = (i, p) => Guard("delete", i, p, OnDelete, (c, _) => Ack(c, Native.CF_OPERATION_TYPE_ACK_DELETE, "ack-delete-error"));
        _onDeleteDone = (i, p) => Guard("delete-complete", i, p, OnDeleteDone);
        _onRename = (i, p) => Guard("rename", i, p, OnRename, (c, _) => Ack(c, Native.CF_OPERATION_TYPE_ACK_RENAME, "ack-rename-error"));
        _onRenameDone = (i, p) => Guard("rename-complete", i, p, OnRenameDone);
        _onDehydrate = (i, p) => Guard("dehydrate", i, p, OnDehydrate, (c, _) => AckDehydrate(c));
        _onDehydrateDone = (i, p) => Guard("dehydrate-complete", i, p, OnDehydrateDone);
    }

    public static int Run(Opts o)
    {
        var root = o.Root();
        var logPath = Path.GetFullPath(o.Need("log"));
        bool deny = o.Has("deny-fetch");
        int? seconds = o.Get("seconds") is { } s ? int.Parse(s) : null;
        bool useStdin = !o.Has("ignore-stdin");
        if (!Directory.Exists(root)) throw new ArgumentException($"root does not exist: {root}");

        using var log = new EventLog(logPath);
        var srv = new Server(root, deny, log);
        using var stop = new ManualResetEventSlim(false);
        string reason = "";
        void Stop(string why) { if (!stop.IsSet) { reason = why; stop.Set(); } }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop("ctrl-c"); };
        if (seconds is { } sec) _ = Task.Delay(TimeSpan.FromSeconds(sec)).ContinueWith(_ => Stop("seconds"));
        if (useStdin)
        {
            var t = new Thread(() =>
            {
                try { using var si = Console.OpenStandardInput(); var b = new byte[256]; while (si.Read(b, 0, b.Length) > 0) { } }
                catch { }
                Stop("stdin-closed");
            }) { IsBackground = true };
            t.Start();
        }

        int hr = srv.Connect(root);
        if (hr < 0) throw new CfException("CfConnectSyncRoot", hr);
        Out.Line("serving", ("root", root), ("log", logPath), ("denyFetch", deny), ("pid", Environment.ProcessId));
        try { stop.Wait(); }
        finally
        {
            int dhr = srv.Disconnect();
            Out.Line("stopped", ("reason", reason), ("disconnectHresult", $"0x{dhr:X8}"), ("clean", dhr >= 0));
        }
        return 0;
    }

    /// <summary>Connect as the provider for <paramref name="root"/>. Returns the HRESULT; the connection key is kept in _conn.</summary>
    public int Connect(string root)
    {
        var srv = this;
        var table = new[]
        {
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_FETCH_DATA, Callback = Marshal.GetFunctionPointerForDelegate(srv._onFetch) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = Marshal.GetFunctionPointerForDelegate(srv._onCancel) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = Marshal.GetFunctionPointerForDelegate(srv._onDelete) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION, Callback = Marshal.GetFunctionPointerForDelegate(srv._onDeleteDone) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = Marshal.GetFunctionPointerForDelegate(srv._onRename) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION, Callback = Marshal.GetFunctionPointerForDelegate(srv._onRenameDone) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE, Callback = Marshal.GetFunctionPointerForDelegate(srv._onDehydrate) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE_COMPLETION, Callback = Marshal.GetFunctionPointerForDelegate(srv._onDehydrateDone) },
            new CfCallbackRegistration { Type = Native.CF_CALLBACK_TYPE_NONE, Callback = IntPtr.Zero },
        };
        int hr = Native.CfConnectSyncRoot(root, table,
            IntPtr.Zero, Native.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO | Native.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH, out long key);
        if (hr >= 0) srv._conn = key;
        return hr;
    }

    public int Disconnect() => Native.CfDisconnectSyncRoot(_conn);

    /// <summary>Connect for a command that must run as the connected provider. <paramref name="deny"/> false serves
    /// fetches (hydrate-range needs its own fetch answered), true fails them (dehydrate needs none); with a
    /// <paramref name="log"/> every callback during the connection is recorded like `serve` does (who else read).</summary>
    public static Server ConnectQuiet(string root, out int hr, bool deny = true, EventLog? log = null)
    {
        var srv = new Server(root, deny, log);
        hr = srv.Connect(root);
        return srv;
    }

    long _conn;

    // ---- plumbing ----

    /// <summary>Native callers must never see a managed exception: log it, then run the recovery that still answers the
    /// platform's request (fail the fetch, ack the delete/rename/dehydrate). No path may leave a request unanswered.</summary>
    void Guard(string name, IntPtr pInfo, IntPtr pPar, Action<CfCallbackInfo, IntPtr> body, Action<CfCallbackInfo, IntPtr>? recover = null)
    {
        CfCallbackInfo ci;
        try { ci = Marshal.PtrToStructure<CfCallbackInfo>(pInfo); }
        catch (Exception e) { try { _log.Write("callback-error", ("callback", name), ("message", e.Message)); } catch { } return; }
        try { body(ci, pPar); }
        catch (Exception e)
        {
            try { _log.Write("callback-error", ("callback", name), ("message", e.Message)); } catch { }
            if (recover != null)
            {
                try { recover(ci, pPar); }
                catch (Exception e2) { try { _log.Write("callback-error", ("callback", name + "-recover"), ("message", e2.Message)); } catch { } }
            }
        }
    }

    static void ReadFetchRange(IntPtr par, CfCallbackInfo ci, out long off, out long len)
    {
        off = Marshal.ReadInt64(par, 16);
        len = Marshal.ReadInt64(par, 24);
        if (len < 0) len = Math.Max(0, ci.FileSize - off);
    }

    static string? Str(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p);

    static string? Identity(CfCallbackInfo ci) =>
        ci.FileIdentity == IntPtr.Zero || ci.FileIdentityLength < 2 ? null : Marshal.PtrToStringUni(ci.FileIdentity, (int)ci.FileIdentityLength / 2);

    /// <summary>Path relative to the sync root, from the callback's volume-relative NormalizedPath (falls back to the file identity).</summary>
    string RelPath(CfCallbackInfo ci)
    {
        var dos = Str(ci.VolumeDosName);
        var np = Str(ci.NormalizedPath);
        if (dos != null && np != null)
        {
            var full = dos + np;
            if (full.StartsWith(_root + "\\", StringComparison.OrdinalIgnoreCase)) return full[(_root.Length + 1)..];
            if (full.Equals(_root, StringComparison.OrdinalIgnoreCase)) return "";
        }
        return Identity(ci) ?? np ?? "";
    }

    static (long Pid, string? Image) Proc(CfCallbackInfo ci)
    {
        if (ci.ProcessInfo == IntPtr.Zero) return (-1, null);
        var pi = Marshal.PtrToStructure<CfProcessInfo>(ci.ProcessInfo);
        return (pi.ProcessId, Str(pi.ImagePath));
    }

    static CfOperationInfo Op(CfCallbackInfo ci, uint type) => new()
    {
        StructSize = (uint)Marshal.SizeOf<CfOperationInfo>(),
        Type = type,
        ConnectionKey = ci.ConnectionKey,
        TransferKey = ci.TransferKey,
        RequestKey = ci.RequestKey,
    };

    /// <summary>The pattern: byte i = (i*31 + 7) &amp; 0xFF.</summary>
    static void Fill(byte[] buf, long start, int count)
    {
        for (int j = 0; j < count; j++) buf[j] = (byte)(((start + j) * 31 + 7) & 0xFF);
    }

    // ---- callbacks ----
    // CF_CALLBACK_PARAMETERS: ULONG ParamSize @0, union @8.
    //   FetchData : Flags@8 RequiredFileOffset@16 RequiredLength@24 OptionalFileOffset@32 OptionalLength@40
    //   Cancel    : Flags@8 FetchData.FileOffset@16 FetchData.Length@24
    //   Delete    : Flags@8        Rename: Flags@8 TargetPath@16

    void OnFetch(CfCallbackInfo ci, IntPtr par)
    {
        uint flags = (uint)Marshal.ReadInt32(par, 8);
        long reqOff = Marshal.ReadInt64(par, 16), reqLen = Marshal.ReadInt64(par, 24);
        long optOff = Marshal.ReadInt64(par, 32), optLen = Marshal.ReadInt64(par, 40);
        var (pid, image) = Proc(ci);
        _log.Write("fetch", ("path", RelPath(ci)), ("identity", Identity(ci)), ("offset", reqOff), ("length", reqLen),
            ("optionalOffset", optOff), ("optionalLength", optLen), ("fileSize", ci.FileSize), ("flags", flags),
            ("pid", pid), ("image", image), ("denied", _deny));

        if (reqLen < 0) reqLen = Math.Max(0, ci.FileSize - reqOff); // CF_EOF = "to the end"
        if (_deny)
        {
            Fail(ci, reqOff, reqLen);
            return;
        }

        // Alignment: TRANSFER_DATA wants a 4 KiB aligned offset and length, except that a transfer may end exactly at
        // EOF. Start rounds down, end rounds up (never past EOF), every chunk starts aligned and is a multiple of 4 KiB
        // except possibly the one that reaches EOF.
        long start = reqOff & ~(Align - 1);
        long end = reqOff + reqLen;
        if (end < ci.FileSize) end = (end + Align - 1) & ~(Align - 1);
        if (end > ci.FileSize) end = ci.FileSize;
        if (end <= start)
        {
            // Nothing to transfer for the required range (zero-length request or offset at/after EOF): the request must
            // still be completed, so fail it explicitly and say so in the log.
            _log.Write("fetch-empty-range", ("path", RelPath(ci)), ("offset", reqOff), ("length", reqLen), ("fileSize", ci.FileSize));
            Fail(ci, reqOff, Math.Max(reqLen, 1));
            return;
        }

        var chunk = new byte[(int)Math.Min(ChunkSize, end - start)];
        var pin = GCHandle.Alloc(chunk, GCHandleType.Pinned);
        try
        {
            var op = Op(ci, Native.CF_OPERATION_TYPE_TRANSFER_DATA);
            for (long pos = start; pos < end; pos += chunk.Length)
            {
                int n = (int)Math.Min(chunk.Length, end - pos);
                Fill(chunk, pos, n);
                var prm = new CfOpTransferData
                {
                    ParamSize = (uint)Marshal.SizeOf<CfOpTransferData>(),
                    CompletionStatus = Native.STATUS_SUCCESS,
                    Buffer = pin.AddrOfPinnedObject(),
                    Offset = pos,
                    Length = n,
                };
                int hr = Native.CfExecuteTransfer(op, ref prm);
                if (hr < 0)
                {
                    _log.Write("transfer-error", ("path", RelPath(ci)), ("offset", pos), ("length", n), ("hresult", $"0x{hr:X8}"));
                    Fail(ci, reqOff, reqLen);
                    return;
                }
            }
        }
        finally { pin.Free(); }
    }

    /// <summary>Fail the pending request: TRANSFER_DATA with a failure CompletionStatus over the required range.</summary>
    void Fail(CfCallbackInfo ci, long offset, long length)
    {
        var op = Op(ci, Native.CF_OPERATION_TYPE_TRANSFER_DATA);
        var prm = new CfOpTransferData
        {
            ParamSize = (uint)Marshal.SizeOf<CfOpTransferData>(),
            CompletionStatus = Native.STATUS_CLOUD_FILE_UNSUCCESSFUL,
            Buffer = IntPtr.Zero,
            Offset = offset,
            Length = length,
        };
        int hr = Native.CfExecuteTransfer(op, ref prm);
        if (hr < 0)
            _log.Write("fail-error", ("path", RelPath(ci)), ("offset", offset), ("length", length), ("hresult", $"0x{hr:X8}"));
    }

    void OnCancel(CfCallbackInfo ci, IntPtr par)
    {
        var (pid, image) = Proc(ci);
        _log.Write("cancel-fetch", ("path", RelPath(ci)), ("offset", Marshal.ReadInt64(par, 16)), ("length", Marshal.ReadInt64(par, 24)),
            ("flags", (uint)Marshal.ReadInt32(par, 8)), ("pid", pid), ("image", image));
    }

    void OnDelete(CfCallbackInfo ci, IntPtr par)
    {
        uint flags = (uint)Marshal.ReadInt32(par, 8);
        var (pid, image) = Proc(ci);
        _log.Write("delete", ("path", RelPath(ci)), ("identity", Identity(ci)), ("isDirectory", (flags & 1) != 0), ("isUndelete", (flags & 2) != 0),
            ("pid", pid), ("image", image));
        Ack(ci, Native.CF_OPERATION_TYPE_ACK_DELETE, "ack-delete-error");
    }

    void OnDeleteDone(CfCallbackInfo ci, IntPtr par)
    {
        var (pid, image) = Proc(ci);
        _log.Write("delete-complete", ("path", RelPath(ci)), ("pid", pid), ("image", image));
    }

    void OnRename(CfCallbackInfo ci, IntPtr par)
    {
        uint flags = (uint)Marshal.ReadInt32(par, 8);
        var (pid, image) = Proc(ci);
        _log.Write("rename", ("path", RelPath(ci)), ("identity", Identity(ci)), ("target", Str(Marshal.ReadIntPtr(par, 16))),
            ("isDirectory", (flags & 1) != 0), ("sourceInScope", (flags & 2) != 0), ("targetInScope", (flags & 4) != 0),
            ("pid", pid), ("image", image));
        Ack(ci, Native.CF_OPERATION_TYPE_ACK_RENAME, "ack-rename-error");
    }

    void OnRenameDone(CfCallbackInfo ci, IntPtr par)
    {
        var (pid, image) = Proc(ci);
        _log.Write("rename-complete", ("path", RelPath(ci)), ("source", Str(Marshal.ReadIntPtr(par, 16))), ("pid", pid), ("image", image));
    }

    // Dehydrate: Flags@8 Reason@12
    void OnDehydrate(CfCallbackInfo ci, IntPtr par)
    {
        var (pid, image) = Proc(ci);
        _log.Write("dehydrate", ("path", RelPath(ci)), ("flags", (uint)Marshal.ReadInt32(par, 8)), ("reason", Marshal.ReadInt32(par, 12)), ("pid", pid), ("image", image));
        AckDehydrate(ci);
    }

    void AckDehydrate(CfCallbackInfo ci)
    {
        var op = Op(ci, Native.CF_OPERATION_TYPE_ACK_DEHYDRATE);
        var prm = new CfOpAckDehydrate { ParamSize = (uint)Marshal.SizeOf<CfOpAckDehydrate>(), CompletionStatus = Native.STATUS_SUCCESS, FileIdentity = ci.FileIdentity, FileIdentityLength = ci.FileIdentityLength };
        int hr = Native.CfExecuteAckDehydrate(op, ref prm);
        if (hr < 0) _log.Write("ack-dehydrate-error", ("path", RelPath(ci)), ("hresult", $"0x{hr:X8}"));
    }

    void OnDehydrateDone(CfCallbackInfo ci, IntPtr par)
    {
        _log.Write("dehydrate-complete", ("path", RelPath(ci)), ("flags", (uint)Marshal.ReadInt32(par, 8)));
    }

    void Ack(CfCallbackInfo ci, uint type, string errEvent)
    {
        var op = Op(ci, type);
        var prm = new CfOpAck { ParamSize = (uint)Marshal.SizeOf<CfOpAck>(), CompletionStatus = Native.STATUS_SUCCESS };
        int hr = Native.CfExecuteAck(op, ref prm);
        if (hr < 0) _log.Write(errEvent, ("path", RelPath(ci)), ("hresult", $"0x{hr:X8}"));
    }
}
