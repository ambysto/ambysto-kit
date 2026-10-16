using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>Drives the test cloud provider (windows/tests/Ambysto.Kit.Safety.CloudFixture): a registered sync root
/// under the profile with online-only placeholders, a serving provider whose log records every fetch with the
/// reader's process, and <c>status</c> as a second oracle (bytes on disk; it never fetches).</summary>
internal sealed class CloudRoot : IDisposable
{
    private Process? serve;

    private CloudRoot(string root, string log)
    {
        Root = root;
        LogPath = log;
    }

    /// <summary>The sync root (a plain folder before registration).</summary>
    public string Root { get; }

    public string LogPath { get; }

    public string Combine(string relative) => Path.Combine(Root, relative.Replace('/', '\\'));

    /// <summary>The provider's executable, built with the solution (the gates run <c>dotnet test --no-build</c> after it).</summary>
    public static string Exe { get; } = FindExe();

    private static string FindExe()
    {
        // One exact path (configuration, framework, runtime of the fixture project): a stale build elsewhere under bin/
        // must never stand in for the current one.
        var configuration = AppContext.BaseDirectory.Contains(@"\Release\", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var exe = Path.Combine(RepoFiles.Root, "windows", "tests", "Ambysto.Kit.Safety.CloudFixture", "bin", configuration,
            "net10.0-windows", "win-x64", "Ambysto.Kit.Safety.CloudFixture.exe");
        if (!File.Exists(exe))
        {
            Assert.Fail("the cloud fixture executable was not built: " + exe);
        }
        return exe;
    }

    /// <summary>Registers <paramref name="root"/> (created if needed) as a sync root of the test provider; the policies
    /// default to full hydration and always-full population (OneDrive-like); "partial" lets a short read leave a file
    /// partly on disk and an unpopulated directory stay on demand.</summary>
    public static CloudRoot Register(string root, string logPath, string hydration = "full", string population = "always-full")
    {
        Directory.CreateDirectory(root);
        Run("register", "--root", root, "--hydration", hydration, "--population", population);
        return new CloudRoot(root, logPath);
    }

    /// <summary>Creates a directory placeholder that is not populated (on demand): it carries RECALL_ON_OPEN.</summary>
    public void PopulateOnDemandDirectory(string relative)
    {
        var manifest = Path.Combine(Path.GetDirectoryName(Root)!, "manifest-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new[] { new { path = relative.TrimEnd('\\') + "\\", dir = true, onDemand = true } }));
        try
        {
            var lines = Run("populate", "--root", Root, "--manifest", manifest);
            var summary = lines.Last(l => l.TryGetProperty("event", out var e) && e.GetString() == "populated");
            Assert.AreEqual(0, summary.GetProperty("failed").GetInt32(), "populate reported failures: " + string.Join("\n", lines.Select(l => l.ToString())));
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    /// <summary>Creates online-only placeholders: files with a size, folders with a trailing backslash.</summary>
    public void Populate(params (string Path, long Size)[] entries)
    {
        var manifest = Path.Combine(Path.GetDirectoryName(Root)!, "manifest-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var items = entries.Select(e => e.Path.EndsWith('\\')
            ? (object)new { path = e.Path, dir = true }
            : new { path = e.Path, size = e.Size });
        File.WriteAllText(manifest, JsonSerializer.Serialize(items));
        try
        {
            var lines = Run("populate", "--root", Root, "--manifest", manifest);
            var summary = lines.Last(l => l.TryGetProperty("event", out var e) && e.GetString() == "populated");
            Assert.AreEqual(0, summary.GetProperty("failed").GetInt32(), "populate reported failures: " + string.Join("\n", lines.Select(l => l.ToString())));
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    /// <summary>Starts the provider; stopped by closing its stdin.</summary>
    public void Serve()
    {
        var info = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("serve");
        info.ArgumentList.Add("--root");
        info.ArgumentList.Add(Root);
        info.ArgumentList.Add("--log");
        info.ArgumentList.Add(LogPath);
        serve = Process.Start(info)!;
        var line = serve.StandardOutput.ReadLine();
        if (line is null || !line.Contains("\"serving\"", StringComparison.Ordinal))
        {
            var error = serve.StandardError.ReadToEnd();
            StopServe();
            Assert.Fail($"the cloud provider did not start: {line}\n{error}");
        }
    }

    public void StopServe()
    {
        if (serve is null)
        {
            return;
        }
        try
        {
            serve.StandardInput.Close();
            if (!serve.WaitForExit(10000))
            {
                serve.Kill(entireProcessTree: true);
                serve.WaitForExit(5000);
            }
        }
        finally
        {
            serve.Dispose();
            serve = null;
        }
    }

    /// <summary>Lines of the provider's log that record a fetch (a download the gate must never cause).</summary>
    public IReadOnlyList<string> Fetches()
    {
        if (!File.Exists(LogPath))
        {
            return [];
        }
        using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var fetches = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains("\"event\":\"fetch\"", StringComparison.Ordinal))
            {
                fetches.Add(line);
            }
        }
        return fetches;
    }

    /// <summary>Fetches the fixture itself caused on purpose (hydration in setup) are not the gate's.</summary>
    private int baseline;

    public void MarkFetchBaseline() => baseline = Fetches().Count;

    public void AssertNoFetch(string after) => Assert.IsEmpty(Fetches().Skip(baseline).ToList(), "the gate started a download (" + after + ")");

    /// <summary>The provider's own view of one file: bytes on disk and whether it still needs a recall.</summary>
    public (long OnDisk, long Size, bool RecallOnDataAccess, bool IsPlaceholder, string Hydration, bool ListingRecallOnOpen) Status(string relative)
    {
        var text = string.Join("", RunRaw("status", "--root", Root));
        using var doc = JsonDocument.Parse(text);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!string.Equals(entry.GetProperty("path").GetString(), relative.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var onDisk = entry.TryGetProperty("onDisk", out var od) && od.ValueKind == JsonValueKind.Number ? od.GetInt64() : -1;
            // What the parent's directory listing shows, without opening the item (RECALL_ON_OPEN lives there).
            var listing = Convert.ToUInt32(entry.GetProperty("listingAttributesRaw").GetString()![2..], 16);
            return (onDisk, entry.GetProperty("size").GetInt64(), entry.GetProperty("recallOnDataAccess").GetBoolean(),
                entry.GetProperty("isPlaceholder").GetBoolean(), entry.GetProperty("hydration").GetString() ?? "",
                (listing & Win32.AttrRecallOnOpen) != 0);
        }
        Assert.Fail($"status does not list {relative}");
        return default;
    }

    /// <summary>Downloads a whole file on purpose. The reader is Windows PowerShell: the test machine's notes record
    /// that in session 0 a read by the fixture's own exe is refused (0x8007018B) while a PowerShell read hydrates in
    /// both sessions. The fetch this causes is recorded as the baseline for <see cref="AssertNoFetch"/>.</summary>
    public void Hydrate(string relative, long size)
    {
        var quoted = Combine(relative).Replace("'", "''", StringComparison.Ordinal);
        ReadThroughPowerShell(relative, size, "$b = [System.IO.File]::ReadAllBytes('" + quoted + "'); [Console]::Out.WriteLine($b.Length)");
    }

    /// <summary>Hydrates one range through the provider itself (the fixture's hydrate-range, which connects as the
    /// provider, so it runs before <see cref="Serve"/>). On a root registered with partial hydration the rest of the
    /// file stays in the cloud: a partly downloaded placeholder. A reader process cannot produce that state here: the
    /// test machine's notes and a probe show a 64 KiB read by PowerShell fetching the whole file under every policy.</summary>
    public void HydrateRange(string relative, long offset, long length)
    {
        Run("hydrate-range", "--root", Root, "--path", relative, "--offset", offset.ToString(CultureInfo.InvariantCulture), "--length", length.ToString(CultureInfo.InvariantCulture));
    }

    private void ReadThroughPowerShell(string relative, long expectedBytes, string command)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(expectedBytes.ToString(CultureInfo.InvariantCulture), output, $"reading {relative} through PowerShell failed (exit {process.ExitCode}): {error}");
        MarkFetchBaseline();
    }

    private static List<JsonElement> Run(params string[] args)
    {
        var raw = RunRaw(args);
        var parsed = new List<JsonElement>();
        foreach (var line in raw)
        {
            if (line.StartsWith('{'))
            {
                using var document = JsonDocument.Parse(line);
                parsed.Add(document.RootElement.Clone());
            }
        }
        return parsed;
    }

    private static List<string> RunRaw(params string[] args)
    {
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info)!;
        var output = new List<string>();
        while (process.StandardOutput.ReadLine() is { } line)
        {
            output.Add(line);
        }
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Fail($"cloud fixture '{string.Join(' ', args)}' exited {process.ExitCode}:\n{string.Join("\n", output)}\n{error}");
        }
        return output;
    }

    public void Dispose()
    {
        StopServe();
        try
        {
            RunRaw("unregister", "--root", Root);
        }
        catch (AssertFailedException)
        {
            // Already unregistered or never registered: the folder is still removed below.
        }
        TempFolder.DeleteTree(Root);
        for (var i = 0; i < 5 && File.Exists(LogPath); i++)
        {
            try
            {
                File.Delete(LogPath);
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
