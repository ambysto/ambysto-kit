internal static class Revocations
{
    private static readonly string[] Ids =
    [
        RevokedIds.First,
    ];

    internal static FrozenSet<string> All { get; } = Ids.ToFrozenSet(StringComparer.Ordinal);
}
