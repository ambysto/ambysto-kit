using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Ambysto.Kit.Licensing;

/// <summary>
/// The public keys this build trusts, by signing key id (docs/spec/license.md, "Rotation and revocation").
/// The library holds only the production set; the published test key (id 0) lives in the tests, never here.
/// </summary>
internal sealed class TrustedKeys
{
    // The production signing keys: id → raw public key (x ‖ y), 128 hex characters.
    // Edited only by the owner's key PR, in the same PR as the Swift copy (TrustedKeys.production);
    // scripts/check-key-sets.py fails when the two differ. One entry per line: [id] = "hex",
    private static readonly Dictionary<byte, string> ProductionHex = new()
    {
    };

    private readonly FrozenDictionary<byte, ECParameters> keys;

    /// <summary>Builds the set and checks every key once. A broken key is a build defect, so it throws.</summary>
    internal TrustedKeys(IReadOnlyDictionary<byte, string> rawHex)
    {
        var keys = new Dictionary<byte, ECParameters>();
        foreach (var (id, hex) in rawHex)
        {
            var raw = Convert.FromHexString(hex);
            if (raw.Length != 64)
            {
                throw new ArgumentException($"Trusted signing key {id} is not 64 bytes.", nameof(rawHex));
            }
            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = raw[..32], Y = raw[32..] },
            };
            try
            {
                using var check = ECDsa.Create(parameters);
            }
            catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException)
            {
                // Windows CNG reports some invalid points (for example 0, 0) as PlatformNotSupportedException.
                throw new ArgumentException($"Trusted signing key {id} is not a P-256 public key.", nameof(rawHex), e);
            }
            keys[id] = parameters;
        }
        this.keys = keys.ToFrozenDictionary();
    }

    /// <summary>The keys release builds trust. Empty until the owner's key PR.</summary>
    internal static TrustedKeys Production { get; } = new(ProductionHex);

    /// <summary>The trusted ids, ascending.</summary>
    internal IReadOnlyList<int> Ids => keys.Keys.Select(id => (int)id).Order().ToArray();

    /// <summary>Step 6: whether <paramref name="signature"/> (raw r ‖ s) signs <paramref name="message"/> under key <paramref name="id"/>.</summary>
    internal bool Verifies(byte id, byte[] message, ReadOnlySpan<byte> signature)
    {
        if (!keys.TryGetValue(id, out var parameters))
        {
            return false;
        }
        try
        {
            // One instance per call: an ECDsa object is not shared across threads.
            using var ecdsa = ECDsa.Create(parameters);
            return ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Step 5: whether this build trusts the signing key <paramref name="id"/>.</summary>
    internal bool Trusts(byte id) => keys.ContainsKey(id);
}
