using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>What a holder process does with the fixture.</summary>
internal enum HoldKind
{
    /// <summary>Open for read, sharing read only (no delete sharing).</summary>
    OpenNoDeleteSharing,
    /// <summary>Open for read, sharing read, write and delete.</summary>
    OpenWithDeleteSharing,
    /// <summary>LoadLibrary on the file (a DLL copy).</summary>
    LoadedLibrary,
    /// <summary>A read-only memory-mapped view with the file handle closed (what the Restart Manager misses).</summary>
    MappedView,
    /// <summary>The folder as the process's current directory.</summary>
    CurrentDirectory,
    /// <summary>Nothing opened: the item's path appears only in the process's command line.</summary>
    CommandLineOnly,
}

/// <summary>Another process (Windows PowerShell 5.1, always present) that holds a fixture in one of the ways the spec
/// lists. It signals READY on stdout once it holds the item and exits when its stdin closes. <see cref="StartImage"/>
/// runs an executable copied inside the item instead (a process whose image is inside the item).</summary>
internal sealed class HolderProcess : IDisposable
{
    private const string Script = """
        param([string]$Kind, [string]$Target)
        $ErrorActionPreference = 'Stop'
        switch ($Kind) {
            'OpenNoDeleteSharing' { $global:h = [System.IO.File]::Open($Target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read) }
            'OpenWithDeleteSharing' { $global:h = [System.IO.File]::Open($Target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)) }
            'LoadedLibrary' {
                Add-Type -Namespace AmbystoHolder -Name Native -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryW(string path);'
                $global:h = [AmbystoHolder.Native]::LoadLibraryW($Target)
                if ($global:h -eq [IntPtr]::Zero) { throw "LoadLibrary failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
            }
            'MappedView' {
                $fs = [System.IO.File]::Open($Target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                $global:m = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateFromFile($fs, [NullString]::Value, [long]0, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read, [System.IO.MemoryMappedFiles.MemoryMappedFileSecurity]$null, [System.IO.HandleInheritability]::None, $false)
                $global:v = $global:m.CreateViewAccessor(0, 0, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
                $fs.Dispose()
            }
            'CurrentDirectory' {
                Set-Location -LiteralPath $Target
                [System.Environment]::CurrentDirectory = $Target
            }
            'CommandLineOnly' { }
            default { throw "unknown kind $Kind" }
        }
        [Console]::Out.WriteLine('READY')
        [Console]::Out.Flush()
        [Console]::In.ReadLine() | Out-Null
        """;

    private readonly Process process;
    private readonly string? scriptPath;

    private HolderProcess(Process process, string? scriptPath)
    {
        this.process = process;
        this.scriptPath = scriptPath;
    }

    public int ProcessId => process.Id;

    public static HolderProcess Start(HoldKind kind, string target)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "ambysto-holder-" + Guid.NewGuid().ToString("N")[..8] + ".ps1");
        File.WriteAllText(scriptPath, Script);
        var info = StartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, kind.ToString(), target })
        {
            info.ArgumentList.Add(a);
        }
        return Launch(info, scriptPath, kind.ToString());
    }

    /// <summary>Runs <paramref name="exePath"/>, a copy of cmd.exe the test placed inside the item, as a process whose
    /// image is inside the item; it prints READY and exits when its stdin closes.</summary>
    public static HolderProcess StartImage(string exePath)
    {
        var info = StartInfo(exePath);
        info.ArgumentList.Add("/k");
        info.ArgumentList.Add("echo READY");
        return Launch(info, null, "image " + exePath);
    }

    private static ProcessStartInfo StartInfo(string fileName) => new(fileName)
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetTempPath(),
    };

    private static HolderProcess Launch(ProcessStartInfo info, string? scriptPath, string what)
    {
        var process = Process.Start(info)!;
        var holder = new HolderProcess(process, scriptPath);
        var ready = process.StandardOutput.ReadLine();
        if (ready is null || !ready.Contains("READY", StringComparison.Ordinal))
        {
            var error = process.StandardError.ReadToEnd();
            holder.Dispose();
            Assert.Fail($"holder ({what}) did not become ready: {ready}\n{error}");
        }
        return holder;
    }

    public void Dispose()
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(5000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
        }
        finally
        {
            process.Dispose();
            if (scriptPath is not null)
            {
                File.Delete(scriptPath);
            }
        }
    }
}
