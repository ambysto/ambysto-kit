internal static class Revocations
{
    private static readonly string[] Ids =
    [
        "0102030405060708",
        "a1a2a3a4a5a6a7a8",
    ];

    internal static FrozenSet<string> All { get; } = Ids.ToFrozenSet(StringComparer.Ordinal);
}
