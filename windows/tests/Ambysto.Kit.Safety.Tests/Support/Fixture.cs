using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>Test categories, one per context of windows/gates.json.</summary>
internal static class Cat
{
    /// <summary>The standard user over SSH; the environment seam set to an interactive standard user; the real file system.</summary>
    public const string Session0 = "Session0";
    /// <summary>The standard user in the interactive console session, real environment.</summary>
    public const string Console = "Console";
    /// <summary>The standard user over SSH, real environment: step 1 refuses.</summary>
    public const string RealEnvironmentSsh = "RealEnvironmentSsh";
    /// <summary>The administrator over SSH, elevated, real environment: step 1 refuses.</summary>
    public const string RealEnvironmentAdmin = "RealEnvironmentAdmin";
    /// <summary>Needs windows/tests/admin/setup.ps1 (run as administrator before, teardown.ps1 after).</summary>
    public const string AdminSetup = "AdminSetup";
}

/// <summary>A fresh folder under the profile (or under <c>%AMBYSTO_TESTDATA%</c>, excluded from Defender, for in-use
/// fixtures). Deleted on dispose, after any attribute, ACL or reparse point a test left is undone.</summary>
internal sealed class TempFolder : IDisposable
{
    public const string RootName = "AmbystoKitSafetyTests";

    public static string ProfileRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RootName);

    public string Path { get; }

    private TempFolder(string parent, string hint)
    {
        Path = System.IO.Path.Combine(parent, hint + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    /// <summary>A fresh folder inside the profile.</summary>
    public static TempFolder InProfile(string hint) => new(ProfileRoot, hint);

    /// <summary>A fresh folder on another volume, in a folder of the tests' own.</summary>
    public static TempFolder OnDrive(string driveRoot, string hint) => new(System.IO.Path.Combine(driveRoot, RootName), hint);

    /// <summary>A fresh folder in the Defender-excluded test data folder (in-use tests: Defender is never a holder there).</summary>
    public static TempFolder InTestData(string hint)
    {
        var root = Environment.GetEnvironmentVariable("AMBYSTO_TESTDATA");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            Assert.Fail("%AMBYSTO_TESTDATA% is not set or missing: the test machine is not set up as the brief says");
        }
        return new TempFolder(root!, hint);
    }

    /// <summary>The canonical spelling of this folder, from the system (the tests' own oracle, not the library's).</summary>
    public string Canonical => Win32.FinalPath(Path);

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative);

    /// <summary>Creates a file of <paramref name="bytes"/> bytes (parents created) and returns its path.</summary>
    public string File(string relative, long bytes = 16, byte fill = 0x5A)
    {
        var path = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        WriteBytes(path, bytes, fill);
        return path;
    }

    public string Dir(string relative)
    {
        var path = Combine(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    public static void WriteBytes(string path, long bytes, byte fill = 0x5A)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var chunk = new byte[Math.Min(bytes, 1 << 20)];
        Array.Fill(chunk, fill);
        var left = bytes;
        while (left > 0)
        {
            var n = (int)Math.Min(left, chunk.Length);
            stream.Write(chunk, 0, n);
            left -= n;
        }
    }

    public void Dispose() => DeleteTree(Path);

    /// <summary>Deletes a tree the tests made, undoing read-only bits, deny ACEs and test reparse points first.</summary>
    public static void DeleteTree(string root)
    {
        if (!Directory.Exists(root) && !System.IO.File.Exists(root))
        {
            return;
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Scrub(new DirectoryInfo(root));
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    private static void Scrub(DirectoryInfo directory)
    {
        Acl.Reset(directory.FullName);
        directory.Attributes = FileAttributes.Directory;
        foreach (var entry in directory.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                UndoReparsePoint(entry);
                continue;
            }
            if (entry is DirectoryInfo sub)
            {
                Scrub(sub);
            }
            else
            {
                Acl.Reset(entry.FullName);
                entry.Attributes = FileAttributes.Normal;
            }
        }
    }

    private static void UndoReparsePoint(FileSystemInfo entry)
    {
        var tag = (uint)(entry.LinkTarget is null ? Win32.TagUnknownThirdParty : Win32.TagMountPoint);
        try
        {
            Win32.DeleteReparsePoint(entry.FullName, entry is DirectoryInfo ? Win32.TagMountPoint : tag);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A kind the tests did not set (a WOF-compressed file deletes like a plain file).
        }
        entry.Attributes = entry is DirectoryInfo ? FileAttributes.Directory : FileAttributes.Normal;
    }
}

/// <summary>Deny ACEs for the fail-closed tests, always on the tests' own items, and their removal.</summary>
internal static class Acl
{
    public static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User!;

    /// <summary>OWNER RIGHTS (S-1-3-4): an ACE for it replaces the owner's implicit READ_CONTROL and WRITE_DAC, which is
    /// what makes a deny stick on an item the test user owns.</summary>
    public static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    /// <summary>Denies this user the given rights.</summary>
    public static void Deny(string path, FileSystemRights rights) => AddDeny(path, CurrentUser, rights);

    /// <summary>Denies the given rights to the owner as such (OWNER RIGHTS), on top of the explicit user deny, so the
    /// owner's implicit READ_CONTROL/WRITE_DAC no longer grant them. The test user keeps WRITE_DAC through its
    /// inherited allow ACE, which <see cref="Reset"/> relies on.</summary>
    public static void DenyOwnerRights(string path, FileSystemRights rights)
    {
        AddDeny(path, CurrentUser, rights);
        AddDeny(path, OwnerRights, rights);
    }

    private static void AddDeny(string path, SecurityIdentifier who, FileSystemRights rights)
    {
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(who, rights, AccessControlType.Deny));
            info.SetAccessControl(security);
        }
        else
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(who, rights, AccessControlType.Deny));
            info.SetAccessControl(security);
        }
    }

    /// <summary>Removes every explicit deny ACE the tests add (for the user and for OWNER RIGHTS). When the DACL cannot
    /// even be read any more, a fresh inheriting DACL is written instead, which needs only WRITE_DAC.</summary>
    public static void Reset(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                DirectorySecurity security;
                try
                {
                    security = info.GetAccessControl(AccessControlSections.Access);
                    security.RemoveAccessRuleAll(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, AccessControlType.Deny));
                    security.RemoveAccessRuleAll(new FileSystemAccessRule(OwnerRights, FileSystemRights.FullControl, AccessControlType.Deny));
                }
                catch (UnauthorizedAccessException)
                {
                    security = new DirectorySecurity();
                    security.SetAccessRuleProtection(false, true);
                }
                info.SetAccessControl(security);
            }
            else if (File.Exists(path))
            {
                var info = new FileInfo(path);
                FileSecurity security;
                try
                {
                    security = info.GetAccessControl(AccessControlSections.Access);
                    security.RemoveAccessRuleAll(new FileSystemAccessRule(CurrentUser, FileSystemRights.FullControl, AccessControlType.Deny));
                    security.RemoveAccessRuleAll(new FileSystemAccessRule(OwnerRights, FileSystemRights.FullControl, AccessControlType.Deny));
                }
                catch (UnauthorizedAccessException)
                {
                    security = new FileSecurity();
                    security.SetAccessRuleProtection(false, true);
                }
                info.SetAccessControl(security);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Not ours to change (an admin-made fixture): left as it is.
        }
        catch (InvalidOperationException)
        {
            // No DACL to edit.
        }
    }
}

/// <summary>Files of the repository, read in place so a test and the spec cannot drift.</summary>
internal static class RepoFiles
{
    private const string Marker = "docs/spec/safety.md";

    public static string Root { get; } = FindRoot();

    public static string Path(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, Marker.Replace('/', System.IO.Path.DirectorySeparatorChar))))
            {
                return dir.FullName;
            }
        }
        throw new FileNotFoundException($"{Marker} not found above {AppContext.BaseDirectory}");
    }
}
