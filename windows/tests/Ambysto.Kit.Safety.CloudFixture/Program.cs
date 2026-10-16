// AmbyCloud: a fake cloud-files provider for the Windows test VM.
// Registers a classic cfapi sync root (no package identity, no Microsoft account), creates dehydrated placeholders,
// serves deterministic bytes on demand and reports exactly what happened. Test infrastructure: it must never lie.
using System.Text;
using System.Text.Json;

namespace Ambysto.Kit.Safety.CloudFixture;

internal sealed class Opts
{
    static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "deny-fetch", "ignore-stdin" };
    readonly Dictionary<string, string> _v = new(StringComparer.Ordinal);

    public Opts(IEnumerable<string> args)
    {
        string? key = null;
        foreach (var a in args)
        {
            if (key == null)
            {
                if (!a.StartsWith("--")) throw new ArgumentException($"unexpected argument '{a}'");
                key = a[2..];
                if (Flags.Contains(key)) { _v[key] = "true"; key = null; }
            }
            else { _v[key] = a; key = null; }
        }
        if (key != null) throw new ArgumentException($"option --{key} needs a value");
    }

    public string? Get(string k) => _v.TryGetValue(k, out var v) ? v : null;
    public bool Has(string k) => _v.ContainsKey(k);
    public string Need(string k) => Get(k) ?? throw new ArgumentException($"missing --{k}");

    /// <summary>Full path of --root without a trailing separator.</summary>
    public string Root()
    {
        var full = Path.GetFullPath(Need("root"));
        if (full.Length > 3) full = full.TrimEnd('\\');
        return full;
    }
}

internal static class Program
{
    const string Usage = "usage: AmbyCloud <register|populate|serve|status|dehydrate|unregister> --root <dir> [options]\n" +
        "  register   --root <dir> [--hydration full|partial|progressive] [--population always-full|partial]\n" +
        "  populate   --root <dir> --manifest <file.json>   (entries: path, size | dir, onDemand)\n" +
        "  serve      --root <dir> --log <file> [--deny-fetch] [--seconds N] [--ignore-stdin]\n" +
        "  status     --root <dir>\n" +
        "  dehydrate  --root <dir> [--path <relative file>]\n" +
        "  hydrate-range --root <dir> --path <relative file> --offset N --length N   (as the provider; serve must not be running)\n" +
        "  unregister --root <dir>\n" +
        "  read       --root <dir> --path <relative file> [--offset N] [--length N]   (a reader that is not PowerShell; THIS WILL HYDRATE)";

    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { Console.Error.WriteLine(Usage); return 2; }
            var o = new Opts(args.Skip(1));
            return args[0].ToLowerInvariant() switch
            {
                "register" => Commands.Register(o),
                "unregister" => Commands.Unregister(o),
                "populate" => Commands.Populate(o),
                "dehydrate" => Commands.Dehydrate(o),
                "hydrate-range" => Commands.HydrateRange(o),
                "status" => StatusCommand.Run(o),
                "read" => Commands.Read(o),
                "serve" => Server.Run(o),
                _ => throw new ArgumentException($"unknown command '{args[0]}'"),
            };
        }
        catch (ArgumentException e)
        {
            Out.Line("error", ("message", e.Message), ("kind", "usage"));
            return 2;
        }
        catch (Exception e)
        {
            Out.Error(e);
            return 1;
        }
    }
}

internal static class Commands
{
    /// <summary>Fixed so every test run (and every session) registers the same provider.</summary>
    public static readonly Guid ProviderId = new("6f2a1c7e-3b54-4d8a-9c1e-a7b3d5e2f401");
    const string ProviderName = "AmbyCloud";
    const string ProviderVersion = "1.0";

    static void Check(int hr, string what)
    {
        if (hr < 0) throw new CfException(what, hr);
    }

    static bool IsRegistered(string root)
    {
        IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);
        try { return Native.CfGetSyncRootInfoByPath(root, 0 /* BASIC */, buf, 64, out _) >= 0; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    static void RequireNtfsLocal(string root)
    {
        if (root.StartsWith(@"\\")) throw new ArgumentException("root must be on a local NTFS volume (UNC paths are not supported)");
        var fmt = new DriveInfo(root[..1]).DriveFormat;
        if (!fmt.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"root must be on NTFS, but {root[..2]} is {fmt}");
    }

    public static int Register(Opts o)
    {
        var root = o.Root();
        RequireNtfsLocal(root);
        Directory.CreateDirectory(root);
        bool already = IsRegistered(root);

        var identity = Encoding.Unicode.GetBytes("AmbyCloud-syncroot");
        IntPtr pIdentity = System.Runtime.InteropServices.Marshal.AllocHGlobal(identity.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(identity, 0, pIdentity, identity.Length);
            var reg = new CfSyncRegistration
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CfSyncRegistration>(),
                ProviderName = ProviderName,
                ProviderVersion = ProviderVersion,
                SyncRootIdentity = pIdentity,
                SyncRootIdentityLength = (uint)identity.Length,
                ProviderId = ProviderId,
            };
            // --hydration full (default) | partial | progressive; --population always-full (default) | partial.
            // Partial hydration lets a short read leave a file partly on disk; partial population lets a directory
            // placeholder stay unpopulated (on demand), which carries RECALL_ON_OPEN.
            ushort hydration = (o.Get("hydration") ?? "full").ToLowerInvariant() switch
            {
                "full" => Native.CF_HYDRATION_POLICY_FULL,
                "partial" => Native.CF_HYDRATION_POLICY_PARTIAL,
                "progressive" => Native.CF_HYDRATION_POLICY_PROGRESSIVE,
                var other => throw new ArgumentException($"unknown --hydration '{other}' (full|partial|progressive)"),
            };
            ushort population = (o.Get("population") ?? "always-full").ToLowerInvariant() switch
            {
                "always-full" => Native.CF_POPULATION_POLICY_ALWAYS_FULL,
                "partial" => Native.CF_POPULATION_POLICY_PARTIAL,
                var other => throw new ArgumentException($"unknown --population '{other}' (always-full|partial)"),
            };
            var pol = new CfSyncPolicies
            {
                StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CfSyncPolicies>(),
                HydrationPrimary = hydration,
                HydrationModifier = 0,
                PopulationPrimary = population,
                PopulationModifier = 0,
                InSync = 0,        // CF_INSYNC_POLICY_NONE (default)
                HardLink = 0,      // CF_HARDLINK_POLICY_NONE
                PlaceholderManagement = 0, // CF_PLACEHOLDER_MANAGEMENT_POLICY_DEFAULT
            };
            Check(Native.CfRegisterSyncRoot(root, reg, pol, already ? Native.CF_REGISTER_FLAG_UPDATE : 0), "CfRegisterSyncRoot");
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pIdentity); }

        Out.Line("registered", ("root", root), ("updated", already), ("providerName", ProviderName),
            ("providerId", ProviderId.ToString("D")), ("hydrationPolicy", (o.Get("hydration") ?? "full").ToUpperInvariant()),
            ("populationPolicy", (o.Get("population") ?? "always-full").ToUpperInvariant().Replace('-', '_')));
        return 0;
    }

    public static int Unregister(Opts o)
    {
        var root = o.Root();
        Check(Native.CfUnregisterSyncRoot(root), "CfUnregisterSyncRoot");
        Out.Line("unregistered", ("root", root));
        return 0;
    }

    /// <summary>OnDemand (directories only): created without "disable on-demand population", so the platform marks it
    /// RECALL_ON_OPEN and asks the provider for its children on first enumeration (which `serve` does not answer:
    /// the gate must never enumerate such a folder).</summary>
    sealed record Item(string Rel, bool IsDir, long Size, DateTime Time, bool OnDemand = false);

    static Item[] ReadManifest(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("manifest must be a JSON array");
        var defaultTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string path = el.GetProperty("path").GetString() ?? throw new ArgumentException("manifest entry without path");
            path = path.Replace('/', '\\');
            bool isDir = (el.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.True) || path.EndsWith('\\');
            path = path.Trim('\\');
            if (path.Length == 0 || Path.IsPathRooted(path) || path.Contains(':'))
                throw new ArgumentException($"bad manifest path '{path}' (must be relative)");
            foreach (var seg in path.Split('\\'))
                if (seg.Length == 0 || seg == "." || seg == "..") throw new ArgumentException($"bad manifest path '{path}'");
            long size = el.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            if (!isDir && size < 0) throw new ArgumentException($"negative size for '{path}'");
            var time = el.TryGetProperty("mtime", out var m) ? m.GetDateTime().ToUniversalTime() : defaultTime;
            bool onDemand = el.TryGetProperty("onDemand", out var od) && od.ValueKind == JsonValueKind.True;
            if (onDemand && !isDir) throw new ArgumentException($"onDemand applies to directories only: '{path}'");
            items[path] = new Item(path, isDir, isDir ? 0 : size, time, onDemand);
        }
        return items.Values.ToArray();
    }

    static bool IsExistsHr(int hr) => hr == unchecked((int)0xC0000035) || (hr & 0xFFFF) == 0xB7 || (hr & 0xFFFF) == 0x50;

    /// <summary>Creates one batch of placeholders that all live in baseDir. Returns (created, existing, failed).</summary>
    static (int Created, int Existing, int Failed) CreateBatch(string baseDir, List<Item> batch)
    {
        var allocs = new List<IntPtr>();
        var arr = new CfPlaceholderCreateInfo[batch.Count];
        try
        {
            for (int i = 0; i < batch.Count; i++)
            {
                var it = batch[i];
                var identity = Encoding.Unicode.GetBytes(it.Rel);
                if (identity.Length > 4096) throw new ArgumentException($"path too long for a file identity: {it.Rel}");
                IntPtr pName = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(Path.GetFileName(it.Rel));
                IntPtr pId = System.Runtime.InteropServices.Marshal.AllocHGlobal(identity.Length);
                System.Runtime.InteropServices.Marshal.Copy(identity, 0, pId, identity.Length);
                allocs.Add(pName); allocs.Add(pId);
                long t = it.Time.ToFileTimeUtc();
                arr[i] = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = pName,
                    FileIdentity = pId,
                    FileIdentityLength = (uint)identity.Length,
                    // Directories are marked fully populated (we create every child ourselves), files in sync + dehydrated.
                    Flags = Native.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC |
                            (it.IsDir && !it.OnDemand ? Native.CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION : 0),
                    FsMetadata = new CfFsMetadata
                    {
                        CreationTime = t, LastAccessTime = t, LastWriteTime = t, ChangeTime = t,
                        FileAttributes = it.IsDir ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL,
                        FileSize = it.IsDir ? 0 : it.Size,
                    },
                };
            }
            int hr = Native.CfCreatePlaceholders(baseDir, arr, (uint)arr.Length, 0, out uint processed);
            int created = 0, existing = 0, failed = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                int r = arr[i].Result;
                if (r >= 0 && i < processed) { created++; Out.Line("created", ("path", batch[i].Rel), ("dir", batch[i].IsDir), ("size", batch[i].Size)); }
                else if (IsExistsHr(r)) { existing++; Out.Line("exists", ("path", batch[i].Rel), ("dir", batch[i].IsDir)); }
                else
                {
                    failed++;
                    int code = r < 0 ? r : hr;
                    Out.Line("failed", ("path", batch[i].Rel), ("hresult", $"0x{code:X8}"), ("message", CfException.Describe("CfCreatePlaceholders", code)));
                }
            }
            // hr is the batch status (e.g. ERROR_ALREADY_EXISTS when any entry exists); the per-entry Result values above are the truth.
            return (created, existing, failed);
        }
        finally { foreach (var p in allocs) System.Runtime.InteropServices.Marshal.FreeHGlobal(p); }
    }

    public static int Populate(Opts o)
    {
        var root = o.Root();
        var manifest = o.Need("manifest");
        if (!Directory.Exists(root)) throw new ArgumentException($"root does not exist: {root}");
        if (!IsRegistered(root)) throw new ArgumentException($"root is not a registered sync root: {root} (run register first)");
        var items = ReadManifest(manifest);

        // Every ancestor of every entry must exist as a placeholder directory first (parents before children).
        var dirs = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            var parts = it.Rel.Split('\\');
            int upto = it.IsDir ? parts.Length : parts.Length - 1;
            for (int n = 1; n <= upto; n++)
            {
                var rel = string.Join('\\', parts[..n]);
                if (n == parts.Length && it.IsDir) dirs[rel] = it;
                else if (!dirs.ContainsKey(rel)) dirs[rel] = new Item(rel, true, 0, it.Time);
            }
        }

        int created = 0, existing = 0, failed = 0;
        foreach (var d in dirs.Values.OrderBy(x => x.Rel.Count(c => c == '\\')).ThenBy(x => x.Rel, StringComparer.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(d.Rel);
            var (c, e, f) = CreateBatch(string.IsNullOrEmpty(parent) ? root : Path.Combine(root, parent), new List<Item> { d });
            created += c; existing += e; failed += f;
        }
        foreach (var g in items.Where(i => !i.IsDir).GroupBy(i => Path.GetDirectoryName(i.Rel) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var baseDir = g.Key.Length == 0 ? root : Path.Combine(root, g.Key);
            foreach (var chunk in g.OrderBy(i => i.Rel, StringComparer.OrdinalIgnoreCase).Chunk(500))
            {
                var (c, e, f) = CreateBatch(baseDir, chunk.ToList());
                created += c; existing += e; failed += f;
            }
        }
        Out.Line("populated", ("root", root), ("created", created), ("existing", existing), ("failed", failed));
        return failed == 0 ? 0 : 1;
    }

    public static int Dehydrate(Opts o)
    {
        var root = o.Root();
        var only = o.Get("path")?.Replace('/', '\\').Trim('\\'); // optional: dehydrate just this file (relative to root)
        int ok = 0, failed = 0, skipped = 0;
        // cfapi only lets the connected provider dehydrate (from another process it hangs or is denied), so connect first.
        var srv = Server.ConnectQuiet(root, out int connectHr);
        if (connectHr < 0)
            throw new CfException("CfConnectSyncRoot (dehydrate must run as the provider; is `serve` still running? stop it first)", connectHr);
        try {
        foreach (var e in Inspect.Walk(root))
        {
            if ((e.Find.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
            if (only != null && !e.Rel.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            int hr = DehydrateOne(e.Full);
            if ((hr & 0xFFFF) == 0x178) { skipped++; Out.Line("skipped", ("path", e.Rel), ("reason", "not a placeholder")); continue; } // ERROR_NOT_A_CLOUD_FILE
            if (hr >= 0) { ok++; Out.Line("dehydrated", ("path", e.Rel)); }
            else { failed++; Out.Line("failed", ("path", e.Rel), ("hresult", $"0x{hr:X8}"), ("message", CfException.Describe("dehydrate", hr))); }
        }
        } finally { if (connectHr >= 0) srv.Disconnect(); }
        Out.Line("dehydrate-done", ("root", root), ("dehydrated", ok), ("failed", failed), ("skipped", skipped));
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Reads bytes from a placeholder as AmbyCloud.exe (a reader with its own pid/image) and checks them against the pattern.
    /// This is the one command that hydrates on purpose.</summary>
    public static int Read(Opts o)
    {
        var root = o.Root();
        var rel = o.Need("path").Replace('/', '\\').Trim('\\');
        long offset = long.Parse(o.Get("offset") ?? "0");
        int length = int.Parse(o.Get("length") ?? "1024");
        var full = Path.Combine(root, rel);
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[length];
            int n = 0, r;
            while (n < length && (r = fs.Read(buf, n, length - n)) > 0) n += r;
            bool ok = true;
            for (int i = 0; i < n; i++) if (buf[i] != (byte)(((offset + i) * 31 + 7) & 0xFF)) { ok = false; break; }
            Out.Line("read", ("path", rel), ("offset", offset), ("requested", length), ("read", n), ("patternOk", ok), ("pid", Environment.ProcessId));
            return ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Out.Line("read-failed", ("path", rel), ("offset", offset), ("message", e.Message.Trim()), ("hresult", $"0x{e.HResult:X8}"), ("type", e.GetType().Name), ("pid", Environment.ProcessId));
            return 1;
        }
    }

    /// <summary>Hydrates one range of a placeholder as the connected provider (CfHydratePlaceholder; the fetch it
    /// triggers is served by this process). With a root registered with partial hydration the rest of the file stays
    /// in the cloud, which is the "partly downloaded" state the gate must refuse. Like dehydrate it must run while
    /// `serve` is not connected.</summary>
    public static int HydrateRange(Opts o)
    {
        var root = o.Root();
        var rel = o.Need("path").Replace('/', '\\').Trim('\\');
        long offset = long.Parse(o.Get("offset") ?? "0");
        long length = long.Parse(o.Need("length"));
        var full = Path.Combine(root, rel);
        var srv = Server.ConnectQuiet(root, out int connectHr, deny: false);
        if (connectHr < 0)
            throw new CfException("CfConnectSyncRoot (hydrate-range must run as the provider; is `serve` still running? stop it first)", connectHr);
        try
        {
            int hr = Native.CfOpenFileWithOplock(full, Native.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out IntPtr prot);
            if (hr < 0) throw new CfException("CfOpenFileWithOplock " + full, hr);
            try
            {
                IntPtr h = Native.CfGetWin32HandleFromProtectedHandle(prot);
                int result = Native.CfHydratePlaceholder(h, offset, length, 0, IntPtr.Zero);
                Out.Line(result >= 0 ? "hydrated-range" : "failed", ("path", rel), ("offset", offset), ("length", length),
                    ("hresult", $"0x{result:X8}"), ("message", result >= 0 ? null : CfException.Describe("CfHydratePlaceholder", result)));
                return result >= 0 ? 0 : 1;
            }
            finally { Native.CfCloseHandle(prot); }
        }
        finally { srv.Disconnect(); }
    }

    static int DehydrateOne(string path)
    {
        // Protected handle with write access (what CfDehydratePlaceholder needs). Opening for write does not read data.
        int hr = Native.CfOpenFileWithOplock(path, Native.CF_OPEN_FILE_FLAG_WRITE_ACCESS, out IntPtr prot);
        if (hr < 0) return hr;
        try
        {
            IntPtr h = Native.CfGetWin32HandleFromProtectedHandle(prot);
            return Native.CfDehydratePlaceholder(h, 0, Native.CF_EOF, 0, IntPtr.Zero);
        }
        finally { Native.CfCloseHandle(prot); }
    }
}
