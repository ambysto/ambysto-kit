using System;
using System.Security.Principal;
using Ambysto.Kit.Safety.Native;
using static Ambysto.Kit.Safety.Native.NativeMethods;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Owners of an item and of every folder above it (the facts an allowed-root rule may use).</summary>
internal static class Owners
{
    internal static readonly string CurrentUserSid = WindowsIdentity.GetCurrent().User?.Value
        ?? throw new InvalidOperationException("the current user has no SID");

    internal const string SystemSid = "S-1-5-18";
    internal const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>The owner SID read through an open handle; throws when it cannot be read (fails closed).</summary>
    internal static string OwnerOf(FileProbe probe)
    {
        var rc = GetSecurityInfo(probe.Handle, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, out var owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
        if (Faults.Code(Faults.Query.Owner, (int)rc, probe.Path) != 0 && rc == 0)
        {
            LocalFree(descriptor);
            rc = (uint)Faults.InjectedError;
        }
        if (rc != 0)
        {
            throw new System.ComponentModel.Win32Exception((int)rc, "owner");
        }
        try
        {
            return new SecurityIdentifier(owner).Value;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }
}
