// Machine-readable output: every stdout line is one JSON object (status prints one JSON array).
// ASCII-only on purpose (the default encoder \u-escapes everything else), so the console code page cannot corrupt it.
using System.Text;
using System.Text.Json;

namespace Ambysto.Kit.Safety.CloudFixture;

internal static class Out
{
    static readonly object Gate = new();
    static readonly StreamWriter Stdout = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Json(object? value) => JsonSerializer.Serialize(value, Options);

    public static Dictionary<string, object?> Obj(string ev, params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?> { ["event"] = ev };
        foreach (var (k, v) in fields) d[k] = v;
        return d;
    }

    public static void Line(string ev, params (string Key, object? Value)[] fields) => Raw(Json(Obj(ev, fields)));

    public static void Raw(string line)
    {
        lock (Gate) Stdout.WriteLine(line);
    }

    public static void Error(Exception e)
    {
        Line("error", ("message", e.Message), ("hresult", e.HResult == 0 ? null : $"0x{e.HResult:X8}"), ("type", e.GetType().Name));
    }
}

/// <summary>Append-only JSON-lines event log. Opened with FileShare.ReadWrite|Delete so tests can read it while serving.</summary>
internal sealed class EventLog : IDisposable
{
    readonly object _gate = new();
    readonly StreamWriter _w;

    public EventLog(string path)
    {
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _w = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }

    public void Write(string ev, params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?> { ["time"] = DateTime.UtcNow.ToString("o"), ["event"] = ev };
        foreach (var (k, v) in fields) d[k] = v;
        string line = Out.Json(d);
        lock (_gate) _w.WriteLine(line);
    }

    public void Dispose() { lock (_gate) _w.Dispose(); }
}
