using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>The test machine's volumes (the brief). A missing one fails the test loudly: it never skips.</summary>
internal static class Volumes
{
    public const string System = @"C:\";
    public const string SecondNtfs = @"E:\";
    public const string RemovableFat32 = @"F:\";
    public const string FixedExFat = @"G:\";

    public static void RequireSecondNtfs() => Require(SecondNtfs, DriveType.Fixed, "NTFS");

    public static void RequireRemovableFat32() => Require(RemovableFat32, DriveType.Removable, "FAT32");

    public static void RequireFixedExFat() => Require(FixedExFat, DriveType.Fixed, "exFAT");

    private static void Require(string root, DriveType type, string format)
    {
        var drive = new DriveInfo(root);
        Assert.IsTrue(drive.IsReady, $"{root} is not ready: the test machine is not set up as the brief says");
        Assert.AreEqual(type, drive.DriveType, root);
        Assert.AreEqual(format, drive.DriveFormat, root);
    }
}
