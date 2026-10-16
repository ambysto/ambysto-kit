using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Ambysto.Kit.Licensing;

/// <summary>
/// Verifies Ambysto license keys offline, in the order of docs/spec/license.md ("Verification order").
/// Stateless after construction and safe to use from several threads. No network, no writes.
/// </summary>
public sealed class LicenseVerifier
{
    /// <summary>The largest <c>.ambystokey</c> file accepted, in raw bytes (a byte order mark included).</summary>
    public const int MaxFileBytes = 4096;

    private static readonly byte[] Domain = "AMBYSTO-LICENSE-1"u8.ToArray();

    private readonly TrustedKeys trusted;
    private readonly FrozenSet<string> revoked;

    /// <summary>A verifier with the keys and revocations compiled into this release.</summary>
    public LicenseVerifier()
        : this(TrustedKeys.Production, Revocations.All)
    {
    }

    internal LicenseVerifier(TrustedKeys trusted, IEnumerable<string> revoked)
    {
        this.trusted = trusted;
        this.revoked = revoked.Select(id => id.ToLowerInvariant()).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Verifies a pasted key text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
    public LicenseResult Verify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Step 1: text to bytes.
        var key = CrockfordText.Decode(text);
        if (key is null)
        {
            return LicenseResult.Refused(LicenseReason.Malformed);
        }

        // Steps 2 to 4: version, length, licensee.
        var payload = LicensePayload.Parse(key, out var reason);
        if (payload is null)
        {
            return LicenseResult.Refused(reason ?? LicenseReason.Malformed);
        }

        // Step 5: the signing key is trusted.
        var signingKeyId = (byte)payload.SigningKeyId;
        if (!trusted.Trusts(signingKeyId))
        {
            return LicenseResult.Refused(LicenseReason.UnknownSigningKey);
        }

        // Step 6: the signature verifies over "AMBYSTO-LICENSE-1" ‖ payload.
        var message = new byte[Domain.Length + payload.Payload.Length];
        Domain.CopyTo(message, 0);
        payload.Payload.CopyTo(message.AsSpan(Domain.Length));
        if (!trusted.Verifies(signingKeyId, message, payload.Signature))
        {
            return LicenseResult.Refused(LicenseReason.BadSignature);
        }

        // Step 7: not revoked.
        if (revoked.Contains(payload.KeyId))
        {
            return LicenseResult.Refused(LicenseReason.Revoked);
        }

        return LicenseResult.Valid(payload.ToLicense());
    }
}
