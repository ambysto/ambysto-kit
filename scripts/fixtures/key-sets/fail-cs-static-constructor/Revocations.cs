internal static class Revocations
{
    private static readonly string[] Ids =
    [
        "0102030405060708",
    ];

    internal static FrozenSet<string> All { get; } = Ids.ToFrozenSet(StringComparer.Ordinal);
}
