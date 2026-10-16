using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace Ambysto.Kit.Licensing;

/// <summary>
/// Steps 2 to 4 of the verification order on the key bytes (docs/spec/license.md, "Payload" and
/// "Verification order"). Signature, trust and revocation are the verifier's steps 5 to 7.
/// </summary>
internal sealed class LicensePayload
{
    internal const byte FormatVersion = 1;
    internal const int HeaderLength = 20;
    internal const int SignatureLength = 64;
    internal const int MaxLicenseeLength = 64;
    internal static readonly DateOnly DayZero = new(2026, 1, 1);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private LicensePayload(byte[] key, int licenseeLength, string licensee)
    {
        Key = key;
        LicenseeLength = licenseeLength;
        Licensee = licensee;
    }

    /// <summary>The whole key: payload followed by the 64-byte signature.</summary>
    internal byte[] Key { get; }

    internal int LicenseeLength { get; }

    internal string Licensee { get; }

    internal int SigningKeyId => Key[1];

    internal ReadOnlySpan<byte> Payload => Key.AsSpan(0, HeaderLength + LicenseeLength);

    internal ReadOnlySpan<byte> Signature => Key.AsSpan(HeaderLength + LicenseeLength, SignatureLength);

    /// <summary>The license key id as 16 lowercase hex characters.</summary>
    internal string KeyId => Convert.ToHexStringLower(Key, 2, 8);

    internal int IssuedDay => (Key[10] << 8) | Key[11];

    internal int UpdatesUntilDay => (Key[12] << 8) | Key[13];

    internal int Seats => Key[14];

    internal uint ProductsMask => ((uint)Key[15] << 24) | ((uint)Key[16] << 16) | ((uint)Key[17] << 8) | Key[18];

    /// <summary>Steps 2, 3 and 4. Returns the payload, or the reason (unsupported version or malformed).</summary>
    internal static LicensePayload? Parse(byte[] key, out LicenseReason? reason)
    {
        reason = null;
        // Step 2: the version byte is known. (Step 1 guarantees at least one byte.)
        if (key.Length == 0)
        {
            reason = LicenseReason.Malformed;
            return null;
        }
        if (key[0] != FormatVersion)
        {
            reason = LicenseReason.UnsupportedVersion;
            return null;
        }

        // Step 3: exact length, licensee length 1 to 64.
        if (key.Length < HeaderLength)
        {
            reason = LicenseReason.Malformed;
            return null;
        }
        int n = key[HeaderLength - 1];
        if (n is < 1 or > MaxLicenseeLength || key.Length != HeaderLength + n + SignatureLength)
        {
            reason = LicenseReason.Malformed;
            return null;
        }

        // Step 4: the licensee is valid UTF-8, decoded strictly and never re-normalised.
        string licensee;
        try
        {
            licensee = StrictUtf8.GetString(key, HeaderLength, n);
        }
        catch (DecoderFallbackException)
        {
            reason = LicenseReason.Malformed;
            return null;
        }
        return new LicensePayload(key, n, licensee);
    }

    /// <summary>The license these fields describe. Called only after steps 5 to 7 pass.</summary>
    internal License ToLicense() => new(
        Licensee,
        ProductsFrom(ProductsMask),
        KeyId,
        DayZero.AddDays(IssuedDay),
        UpdatesUntilDay == 0 ? null : DayZero.AddDays(UpdatesUntilDay),
        Seats,
        SigningKeyId);

    /// <summary>The known products in a mask; bits this build does not know are ignored.</summary>
    internal static FrozenSet<Product> ProductsFrom(uint mask)
    {
        var products = new List<Product>();
        foreach (var product in Enum.GetValues<Product>())
        {
            if ((mask & (1u << (int)product)) != 0)
            {
                products.Add(product);
            }
        }
        return products.ToFrozenSet();
    }
}
