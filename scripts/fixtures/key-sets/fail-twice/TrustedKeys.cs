internal sealed class TrustedKeys
{
    private static readonly Dictionary<byte, string> ProductionHex = new()
    {
    };

    internal static TrustedKeys Production { get; } = new(ProductionHex);
}
