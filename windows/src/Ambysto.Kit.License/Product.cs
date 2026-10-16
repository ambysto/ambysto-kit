namespace Ambysto.Kit.Licensing;

/// <summary>
/// The products a license can list. The value is the product's bit in the license's products mask
/// (docs/spec/license.md, "Products"). Positions never change, and they never follow a rename.
/// </summary>
public enum Product
{
    /// <summary>Bit 0, id <c>optimer</c>: Ambysto Optimer.</summary>
    Optimer = 0,

    /// <summary>Bit 1, id <c>steady</c>: Ambysto Steady.</summary>
    Steady = 1,

    /// <summary>Bit 2, id <c>space</c>: shown as Ambysto Lean.</summary>
    Space = 2,

    /// <summary>Bit 3, id <c>devbox</c>: Ambysto DevBox.</summary>
    DevBox = 3,

    /// <summary>Bit 4, id <c>power</c>: Ambysto Power.</summary>
    Power = 4,

    /// <summary>Bit 5, id <c>tune</c>: Ambysto Tune.</summary>
    Tune = 5,

    /// <summary>Bit 6, id <c>gate</c>: Ambysto Gate.</summary>
    Gate = 6,
}

/// <summary>The fixed id and display name of each <see cref="Product"/>.</summary>
public static class ProductInfo
{
    /// <summary>The product id from the spec, for example <c>space</c>.</summary>
    public static string Id(this Product product) => product switch
    {
        Product.Optimer => "optimer",
        Product.Steady => "steady",
        Product.Space => "space",
        Product.DevBox => "devbox",
        Product.Power => "power",
        Product.Tune => "tune",
        Product.Gate => "gate",
        _ => throw new System.ArgumentOutOfRangeException(nameof(product)),
    };

    /// <summary>The name users see, for example "Ambysto Lean" for <see cref="Product.Space"/>.</summary>
    public static string DisplayName(this Product product) => product switch
    {
        Product.Optimer => "Ambysto Optimer",
        Product.Steady => "Ambysto Steady",
        Product.Space => "Ambysto Lean",
        Product.DevBox => "Ambysto DevBox",
        Product.Power => "Ambysto Power",
        Product.Tune => "Ambysto Tune",
        Product.Gate => "Ambysto Gate",
        _ => throw new System.ArgumentOutOfRangeException(nameof(product)),
    };
}
