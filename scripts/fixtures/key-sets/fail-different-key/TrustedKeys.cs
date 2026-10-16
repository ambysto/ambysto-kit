internal sealed class TrustedKeys
{
    private static readonly Dictionary<byte, string> ProductionHex = new()
    {
        [1] = "11111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111111",
    };

    internal static TrustedKeys Production { get; } = new(ProductionHex);
}
