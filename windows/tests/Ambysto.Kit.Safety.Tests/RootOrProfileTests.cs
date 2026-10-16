using System;
using System.IO;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 10: a volume root, the profile folder, the profiles folder, another user's profile, Public or
/// Default is never moved. The spec: a check on the canonical path alone, run right after step 5, so such an item is
/// never walked and never stamped (the junk stamp here proves the order against step 6).</summary>
[TestClass]
public sealed class RootOrProfileTests
{
    private static void RefusedAsRootOrProfile(string path)
    {
        Expect.Refused(Gates.CheckWith(path, Gates.AnyStamp), RefusalReason.RootOrProfile);
        Expect.Refused(Gates.CheckWith(path.TrimEnd('\\') + "\\", Gates.AnyStamp), RefusalReason.RootOrProfile);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSystemVolumeRootIsRefused() => RefusedAsRootOrProfile(Volumes.System);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSecondVolumeRootIsRefused()
    {
        Volumes.RequireSecondNtfs();
        RefusedAsRootOrProfile(Volumes.SecondNtfs);
    }

    /// <summary>Step 5 still comes first: the removable volume's root is a volume refusal.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheRemovableVolumeRootIsAVolumeRefusal()
    {
        Volumes.RequireRemovableFat32();
        Expect.Refused(Gates.CheckWith(Volumes.RemovableFat32, Gates.AnyStamp), RefusalReason.UnsupportedVolume);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheProfileFolderIsRefused() => RefusedAsRootOrProfile(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheProfileFolderIsRefusedInAnotherSpelling()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RefusedAsRootOrProfile(profile.ToUpperInvariant());
        RefusedAsRootOrProfile(Win32.ShortPath(profile));
        using var subst = new SubstDrive(profile);
        RefusedAsRootOrProfile(subst.Letter + "\\");
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheProfilesFolderIsRefused()
        => RefusedAsRootOrProfile(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ThePublicProfileIsRefused()
        => RefusedAsRootOrProfile(Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!, "Public"));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheDefaultProfileIsRefused()
        => RefusedAsRootOrProfile(Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!, "Default"));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnotherUsersProfileIsRefused()
    {
        // Not the legacy junctions ("All Users", "Default User"): a correct gate refuses those at step 3 as links.
        var mine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var other = Directory.EnumerateDirectories(Path.GetDirectoryName(mine)!)
            .FirstOrDefault(d => !string.Equals(d, mine, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(d).Equals("Public", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(d).Equals("Default", StringComparison.OrdinalIgnoreCase)
                && (Win32.Attributes(d) & Win32.AttrReparsePoint) == 0);
        Assert.IsNotNull(other, "no other profile folder that is not a reparse point on this machine");
        Assert.AreEqual(0u, Win32.Attributes(other) & Win32.AttrReparsePoint, "precondition: the other profile is not a link");
        RefusedAsRootOrProfile(other);
    }

    /// <summary>An item directly inside the profile is not step 10's business (NTUSER.DAT is refused later, as protected).</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnItemInsideTheProfileIsNotARootOrProfile()
    {
        var hive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NTUSER.DAT");
        Expect.Refused(Gates.Check(hive), RefusalReason.Protected);
        using var folder = TempFolder.InProfile("root");
        var file = folder.File("plain.txt", 10);
        Expect.Approved(Gates.Check(file), Win32.FinalPath(file));
    }
}
