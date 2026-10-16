using System;
using System.Collections.Frozen;

namespace Ambysto.Kit.Licensing;

/// <summary>Withdrawn license key ids (docs/spec/license.md, "Rotation and revocation").</summary>
internal static class Revocations
{
    // License key ids, 16 lowercase hex characters each. Edited in the same PR as the Swift copy
    // (Revocations.all); scripts/check-key-sets.py fails when the two differ. One entry per line: "id",
    private static readonly string[] Ids =
    [
    ];

    /// <summary>The ids release builds refuse.</summary>
    internal static FrozenSet<string> All { get; } = Ids.ToFrozenSet(StringComparer.Ordinal);
}
