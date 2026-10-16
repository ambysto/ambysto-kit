namespace Ambysto.Kit.Licensing;

/// <summary>Why a key was refused (docs/spec/license.md, "Verification order" and "Reasons").</summary>
public enum LicenseReason
{
    /// <summary>The text or file is not a well-formed key.</summary>
    Malformed,

    /// <summary>The key's format version is not one this build knows.</summary>
    UnsupportedVersion,

    /// <summary>This build does not trust the key that signed the license.</summary>
    UnknownSigningKey,

    /// <summary>The signature does not match the license.</summary>
    BadSignature,

    /// <summary>The license has been withdrawn.</summary>
    Revoked,
}

/// <summary>The message shown to the user for each <see cref="LicenseReason"/>.</summary>
public static class LicenseReasonInfo
{
    /// <summary>The spec's message for this reason, verbatim.</summary>
    public static string Message(this LicenseReason reason) => reason switch
    {
        LicenseReason.Malformed => "This key was mistyped or damaged. Paste it again, or open the .ambystokey file.",
        LicenseReason.UnsupportedVersion => "This key needs a newer version of the app.",
        LicenseReason.UnknownSigningKey => "This key was not issued by Ambysto, or needs a newer version of the app.",
        LicenseReason.BadSignature => "This key was changed after it was issued.",
        LicenseReason.Revoked => "This key has been withdrawn. Contact Ambysto support.",
        _ => throw new System.ArgumentOutOfRangeException(nameof(reason)),
    };
}
