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

    /// <summary>Under the system drive's root (every level openable by the standard user, unlike C:\Users\Public, which
    /// it cannot open at all) and per clone: setup.ps1 derives the same path from its own location (the repository
    /// folder's name), so two clones' runs never share fixtures.</summary>
    public static string Folder => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, TempFolder.RootName, Path.GetFileName(RepoFiles.Root));

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

    /// <summary>A folder holding only a junction made by the administrator (followable, unlike the standard user's own).</summary>
    public string FolderWithAdminJunction => Required("folderWithAdminJunction");

    /// <summary>A volume mount point (the exFAT volume mounted in a folder).</summary>
    public string MountPoint => Required("mountPoint");

    /// <summary>A folder holding only the mount point.</summary>
    public string FolderWithMountPoint => Required("folderWithMountPoint");

    /// <summary>The control: the same kind of folder with a plain subfolder instead of the mount point.</summary>
    public string FolderWithoutMountPoint => Required("folderWithoutMountPoint");

    /// <summary>A folder holding a mount point and a descendant path of 260 characters or more.</summary>
    public string FolderWithMountPointAndLongPath => Required("folderWithMountPointAndLongPath");

    /// <summary>A <c>FileHistory</c> folder at the root of the second NTFS volume (created by setup when absent).</summary>
    public string FileHistoryRoot => Required("fileHistoryRoot");

    /// <summary>A file setup placed inside that folder.</summary>
    public string FileHistoryFile => Required("fileHistoryFile");

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

    /// <summary>A folder the standard user can open but whose owner it cannot read (READ_CONTROL denied on it alone).</summary>
    public string OwnerUnreadableAbove => Required("ownerUnreadableAbove");

    /// <summary>A readable folder inside it (a restore destination's parent).</summary>
    public string OwnerUnreadableAboveParent => Required("ownerUnreadableAboveParent");

    /// <summary>A readable file inside that folder.</summary>
    public string OwnerUnreadableAboveItem => Required("ownerUnreadableAboveItem");

    /// <summary>A folder the standard user cannot open at all (every right denied, inside a wrapper it may not list);
    /// its existence cannot be checked by this user, so only the recorded path is required.</summary>
    public string CannotOpenAbove
    {
        get
        {
            if (!root.TryGetProperty("cannotOpenAbove", out var value) || value.ValueKind != JsonValueKind.String)
            {
                Assert.Fail("setup.ps1 did not record 'cannotOpenAbove' in handoff.json");
            }
            return value.GetString()!;
        }
    }

    /// <summary>A readable folder inside it.</summary>
    public string CannotOpenAboveParent => Required("cannotOpenAboveParent");

    /// <summary>A readable file inside that folder.</summary>
    public string CannotOpenAboveItem => Required("cannotOpenAboveItem");

    /// <summary>A WOF-compressed file (compact /exe), owned by the standard user with full control.</summary>
    public string WofFile => Required("wofFile");

    /// <summary>A folder holding only the WOF-compressed file.</summary>
    public string FolderWithWofFile => Required("folderWithWofFile");
}
