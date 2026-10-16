using System.Diagnostics.CodeAnalysis;

namespace Ambysto.Kit.Licensing;

/// <summary>The outcome of a verification: a license, or the reason it was refused. Exactly one is set.</summary>
public sealed class LicenseResult
{
    private LicenseResult(License? license, LicenseReason? reason)
    {
        License = license;
        Reason = reason;
    }

    /// <summary>The verified license, when <see cref="IsValid"/>.</summary>
    public License? License { get; }

    /// <summary>Why the key was refused, when not <see cref="IsValid"/>.</summary>
    public LicenseReason? Reason { get; }

    /// <summary>Whether the key verified.</summary>
    [MemberNotNullWhen(true, nameof(License))]
    [MemberNotNullWhen(false, nameof(Reason))]
    public bool IsValid => License is not null;

    internal static LicenseResult Valid(License license) => new(license, null);

    internal static LicenseResult Refused(LicenseReason reason) => new(null, reason);
}
