using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>A free drive letter for a subst or a mapped drive, released on dispose.</summary>
internal static class DriveLetters
{
    public static string Free()
    {
        var used = Directory.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])).ToHashSet();
        for (var letter = 'Z'; letter >= 'H'; letter--)
        {
            if (!used.Contains(letter) && !Win32.IsDosDeviceDefined(letter + ":"))
            {
                return letter + ":";
            }
        }
        Assert.Fail("no free drive letter");
        return "";
    }
}

/// <summary>A <c>subst</c> drive (DefineDosDevice) onto a folder; this logon session only; removed on dispose.</summary>
internal sealed class SubstDrive : IDisposable
{
    public SubstDrive(string targetFolder)
    {
        Target = targetFolder.TrimEnd('\\');
        Letter = DriveLetters.Free();
        Win32.DefineDosDevice(Letter, Target);
    }

    public string Letter { get; }

    public string Target { get; }

    public string Combine(string relative) => Letter + "\\" + relative;

    public void Dispose() => Win32.RemoveDosDevice(Letter, Target);
}

/// <summary>A drive letter mapped to the loopback share (the brief: <c>\\localhost\ambynet</c>), unmapped on dispose.</summary>
internal sealed class MappedDrive : IDisposable
{
    public const string Share = @"\\localhost\ambynet";

    public MappedDrive()
    {
        Letter = DriveLetters.Free();
        var error = Win32.AddNetworkConnection(Letter, Share);
        if (error != 0)
        {
            Assert.Fail($"mapping {Letter} to {Share} failed: Win32 error {error}");
        }
    }

    public string Letter { get; }

    public string Combine(string relative) => Letter + "\\" + relative;

    public void Dispose() => Win32.CancelNetworkConnection(Letter);
}

/// <summary>A folder of the tests' own on the loopback share (written through the UNC path), deleted on dispose.</summary>
internal sealed class ShareFolder : IDisposable
{
    public ShareFolder()
    {
        Relative = TempFolder.RootName + "\\" + Guid.NewGuid().ToString("N")[..8];
        Path = MappedDrive.Share + "\\" + Relative;
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder relative to the share root.</summary>
    public string Relative { get; }

    /// <summary>The UNC path of the folder.</summary>
    public string Path { get; }

    public string File(string name, long bytes = 16)
    {
        var path = Path + "\\" + name;
        TempFolder.WriteBytes(path, bytes);
        return path;
    }

    public void Dispose() => TempFolder.DeleteTree(Path);
}
