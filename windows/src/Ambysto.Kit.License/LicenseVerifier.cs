using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

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
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

    /// <summary>
    /// Verifies an <c>.ambystokey</c> file (docs/spec/license.md, "File"): at most 4096 raw bytes, strict UTF-8,
    /// one leading UTF-8 byte order mark ignored. A larger, missing or unreadable file is malformed.
    /// The file is read as raw bytes, never through a reader that detects the encoding.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    public LicenseResult VerifyFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Length comes first: a device or pipe (for example CON) has none and throws instead of blocking on a read.
            if (stream.Length > MaxFileBytes)
            {
                return LicenseResult.Refused(LicenseReason.Malformed);
            }
            // Read one byte more than allowed, so a file that grows while it is read is still refused.
            bytes = new byte[MaxFileBytes + 1];
            var total = 0;
            int read;
            while (total < bytes.Length && (read = stream.Read(bytes, total, bytes.Length - total)) > 0)
            {
                total += read;
            }
            if (total > MaxFileBytes)
            {
                return LicenseResult.Refused(LicenseReason.Malformed);
            }
            Array.Resize(ref bytes, total);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or SecurityException)
        {
            return LicenseResult.Refused(LicenseReason.Malformed);
        }

        string text;
        try
        {
            // The whole file must be UTF-8; a UTF-16 byte order mark (FF FE or FE FF) is not.
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return LicenseResult.Refused(LicenseReason.Malformed);
        }
        // One leading UTF-8 byte order mark (EF BB BF, decoded as U+FEFF) is ignored; any other U+FEFF is malformed in Verify.
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }
        return Verify(text);
    }
}
