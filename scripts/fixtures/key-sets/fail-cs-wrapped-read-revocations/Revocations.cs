internal static class Revocations
{
    private static readonly string[] Ids =
    [
        "0102030405060708",
    ];

    internal static FrozenSet<string> All { get; } = Ids.Concat(["a1a2a3a4a5a6a7a8"]).ToFrozenSet(StringComparer.Ordinal);
}
