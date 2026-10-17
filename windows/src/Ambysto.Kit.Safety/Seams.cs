using System;
using System.Linq;
using System.Security.Principal;
using Ambysto.Kit.Safety.Native;

namespace Ambysto.Kit.Safety;

/// <summary>Facts about the calling process's environment (Windows step 1). Internal seam: only the Kit's tests replace it.</summary>
internal interface IEnvironmentFacts
{
    bool IsElevated { get; }
    bool IsSystem { get; }
    int SessionId { get; }
    bool HasInteractiveSid { get; }
}

/// <summary>Machine and user policy a standard user cannot set (NoRecycleFiles). Internal seam: only the Kit's tests replace it.</summary>
internal interface IRecycleBinPolicy
{
    /// <summary>True when policy disallows the Recycle Bin; null when the policy cannot be read (fails closed).</summary>
    bool? NoRecycleFiles { get; }
}

/// <summary>The real environment. Measured (2026-10-16): an SSH administrator's token is elevated although its elevation
/// type is "default", so elevation is read as TokenIsElevated; the INTERACTIVE SID is present only in a desktop session.</summary>
internal sealed class SystemEnvironmentFacts : IEnvironmentFacts
{
    internal static readonly SystemEnvironmentFacts Instance = new();

    private const string InteractiveSid = "S-1-5-4";

    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!Internal.Faults.Ok(Internal.Faults.Query.TokenElevation, TokenMethods.GetTokenInformation(identity.AccessToken, TokenMethods.TokenElevation, out var elevated, sizeof(int), out _)))
            {
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "token elevation");
            }
            return elevated != 0;
        }
    }

    public bool IsSystem
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }
    }

    public int SessionId
    {
        get
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.SessionId;
        }
    }

    public bool HasInteractiveSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Groups?.Any(g => string.Equals(g.Value, InteractiveSid, StringComparison.Ordinal)) == true;
        }
    }
}

internal sealed class SystemRecycleBinPolicy : IRecycleBinPolicy
{
    internal static readonly SystemRecycleBinPolicy Instance = new();
    public bool? NoRecycleFiles => Internal.RecycleBin.NoRecycleFilesPolicy();
}
