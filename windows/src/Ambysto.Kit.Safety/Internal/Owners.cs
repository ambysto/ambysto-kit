using System;
using System.Collections.Generic;
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

    /// <summary>The item first, then each folder above it up to and including the volume root.</summary>
    internal static List<OwnerFact> Chain(string canonical, string volumeRoot)
    {
        var facts = new List<OwnerFact>();
        var current = canonical;
        while (true)
        {
            var (probe, error) = FileProbe.Open(current);
            if (probe is null)
            {
                throw new System.ComponentModel.Win32Exception(error, "owner of " + current);
            }
            using (probe)
            {
                var sid = OwnerOf(probe);
                facts.Add(new OwnerFact(current, sid, string.Equals(sid, CurrentUserSid, StringComparison.Ordinal)));
            }
            if (string.Equals(current.TrimEnd('\\') + "\\", volumeRoot, StringComparison.OrdinalIgnoreCase) || current.Length <= 3)
            {
                break;
            }
            var parent = System.IO.Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            current = parent;
        }
        return facts;
    }
}
