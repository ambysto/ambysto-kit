using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ambysto.Kit.Licensing;

/// <summary>
/// A verified license. Only <c>LicenseVerifier</c> creates one. The license key id is its identity:
/// two licenses are equal exactly when their key ids are equal (docs/spec/license.md, "Signature").
/// It exposes no key text, signature or hash of them, because several texts can carry the same license.
/// </summary>
public sealed class License : IEquatable<License>
{
    internal License(string licensee, FrozenSet<Product> products, string keyId, DateOnly issued,
                      DateOnly? updatesUntil, int seats, int signingKeyId)
    {
        Licensee = licensee;
        Products = products;
        KeyId = keyId;
        Issued = issued;
        UpdatesUntil = updatesUntil;
        Seats = seats;
        SigningKeyId = signingKeyId;
    }

    /// <summary>Who the license is registered to, as issued (never re-normalised).</summary>
    public string Licensee { get; }

    /// <summary>The products this build knows that the license lists. Unknown bits are ignored.</summary>
    public IReadOnlySet<Product> Products { get; }

    /// <summary>The license key id: 16 lowercase hex characters. The identity of the license.</summary>
    public string KeyId { get; }

    /// <summary>The day the license was issued (UTC).</summary>
    public DateOnly Issued { get; }

    /// <summary>The last day (UTC, inclusive) whose builds the license covers; <c>null</c> means updates for life.</summary>
    public DateOnly? UpdatesUntil { get; }

    /// <summary>Seats, informational only.</summary>
    public int Seats { get; }

    /// <summary>The id of the trusted key that signed the license.</summary>
    public int SigningKeyId { get; }

    /// <summary>Whether the license lists <paramref name="product"/>.</summary>
    public bool Covers(Product product) => Products.Contains(product);

    /// <summary>Whether a build made on <paramref name="builtOn"/> (a UTC day) is covered by the license's updates.</summary>
    public bool AllowsUpdates(DateOnly builtOn) => UpdatesUntil is not { } until || builtOn <= until;

    /// <inheritdoc/>
    public bool Equals(License? other) => other is not null && string.Equals(KeyId, other.KeyId, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as License);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(KeyId);

    /// <inheritdoc/>
    public override string ToString() => $"License {KeyId}";
}
