using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>What windows/tests/admin/setup.ps1 made as administrator before an AdminSetup run, read from the handoff
/// file it writes under <c>%PUBLIC%</c> (a place both accounts can reach). The tests only read these items.</summary>
internal sealed class AdminHandoff
{
    public const string FolderName = "Ambysto.Kit.Safety.Tests";

    private readonly JsonElement root;

    private AdminHandoff(JsonElement root)
    {
        this.root = root;
    }

    public static string Folder => Path.Combine(Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public", FolderName);

    public static AdminHandoff Read()
    {
        var path = Path.Combine(Folder, "handoff.json");
        if (!File.Exists(path))
        {
            Assert.Fail("the AdminSetup fixtures are missing: windows/tests/admin/setup.ps1 did not run (" + path + ")");
        }
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new AdminHandoff(document.RootElement.Clone());
    }

    private string Required(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            Assert.Fail($"setup.ps1 did not record '{name}' in handoff.json");
        }
        var path = value.GetString()!;
        Assert.IsTrue(File.Exists(path) || Directory.Exists(path), $"setup.ps1 recorded '{name}' = {path}, which does not exist");
        return path;
    }

    /// <summary>A file an elevated (SYSTEM) process holds open without delete sharing.</summary>
    public string HeldFile => Required("heldFile");

    public int HolderPid => root.GetProperty("holderPid").GetInt32();

    /// <summary>A symbolic link to a file.</summary>
    public string FileSymlink => Required("fileSymlink");

    /// <summary>A symbolic link to a folder.</summary>
    public string DirectorySymlink => Required("directorySymlink");

    /// <summary>A folder holding only the file symbolic link.</summary>
    public string FolderWithFileSymlink => Required("folderWithFileSymlink");

    /// <summary>A folder holding only the directory symbolic link.</summary>
    public string FolderWithDirectorySymlink => Required("folderWithDirectorySymlink");

    /// <summary>A volume mount point (the exFAT volume mounted in a folder).</summary>
    public string MountPoint => Required("mountPoint");

    /// <summary>A folder holding only the mount point.</summary>
    public string FolderWithMountPoint => Required("folderWithMountPoint");

    /// <summary>A file whose owner is SYSTEM.</summary>
    public string SystemOwnedFile => Required("systemOwnedFile");

    /// <summary>A folder (owned by Administrators) holding the SYSTEM-owned file.</summary>
    public string FolderWithSystemOwnedFile => Required("folderWithSystemOwnedFile");

    /// <summary>A folder created elevated: owned by Administrators, with a plain file inside.</summary>
    public string AdministratorsOwnedFolder => Required("administratorsOwnedFolder");

    public string AdministratorsOwnedFile => Required("administratorsOwnedFile");

    /// <summary>A file owned by TrustedInstaller, outside every system location.</summary>
    public string TrustedInstallerOwnedFile => Required("trustedInstallerOwnedFile");

    /// <summary>A folder (owned by Administrators) holding the TrustedInstaller-owned file.</summary>
    public string FolderWithTrustedInstallerOwnedFile => Required("folderWithTrustedInstallerOwnedFile");

    /// <summary>A WOF-compressed file (compact /exe), owned by the standard user with full control.</summary>
    public string WofFile => Required("wofFile");

    /// <summary>A folder holding only the WOF-compressed file.</summary>
    public string FolderWithWofFile => Required("folderWithWofFile");
}
