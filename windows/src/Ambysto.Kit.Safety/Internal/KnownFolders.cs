using System;
using System.Runtime.InteropServices;
using static Ambysto.Kit.Safety.Native.NativeMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Known folders through the shell (so redirected folders are covered), never hard-coded drive letters.</summary>
internal static class KnownFolders
{
    internal static readonly Guid Profile = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
    internal static readonly Guid UserProfiles = new("0762D272-C50A-4BB0-A382-697DCD729B80");
    internal static readonly Guid Pictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    internal static readonly Guid LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    internal static readonly Guid RoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");
    internal static readonly Guid ProgramData = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97");
    internal static readonly Guid Windows = new("F38BF404-1D43-42F2-9305-67DE0B28FC23");
    internal static readonly Guid ProgramFiles = new("905E63B6-C1BF-494E-B29C-65B732D3D21A");
    internal static readonly Guid ProgramFilesX86 = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E");
    internal static readonly Guid Public = new("DFDF76A2-C82A-4D63-906A-5644AC457385");

    /// <summary>The folder's current path; throws when the shell cannot give it (fails closed).</summary>
    internal static string Get(Guid id)
    {
        var hr = Faults.Hr(Faults.Query.KnownFolder, SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out var p), id.ToString());
        try
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
            return Marshal.PtrToStringUni(p) ?? throw new InvalidOperationException("empty known folder path");
        }
        finally
        {
            CoTaskMemFree(p);
        }
    }

    /// <summary>The path or null when this known folder does not exist on the machine.</summary>
    internal static string? TryGet(Guid id)
    {
        var hr = SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out var p);
        try
        {
            return hr < 0 ? null : Marshal.PtrToStringUni(p);
        }
        finally
        {
            CoTaskMemFree(p);
        }
    }
}
