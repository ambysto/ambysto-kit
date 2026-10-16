using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>One entry of docs/spec/license-test-vectors.json, read in place.</summary>
internal sealed record Vector(string Name, string Text, JsonElement Expect, Dictionary<byte, string> TrustedKeys, List<string> Revoked)
{
    public bool ExpectValid => Expect.TryGetProperty("valid", out var v) && v.GetBoolean();

    public string? ExpectError => Expect.TryGetProperty("error", out var e) ? e.GetString() : null;

    public static IReadOnlyList<Vector> All { get; } = Load();

    private static List<Vector> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(SpecFiles.Path("docs/spec/license-test-vectors.json")));
        var list = new List<Vector>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var trusted = new Dictionary<byte, string>();
            foreach (var key in item.GetProperty("trustedKeys").EnumerateObject())
            {
                trusted[byte.Parse(key.Name, System.Globalization.CultureInfo.InvariantCulture)] = key.Value.GetString()!;
            }
            var revoked = new List<string>();
            foreach (var id in item.GetProperty("revoked").EnumerateArray())
            {
                revoked.Add(id.GetString()!);
            }
            list.Add(new Vector(item.GetProperty("name").GetString()!, item.GetProperty("text").GetString()!,
                                item.GetProperty("expect").Clone(), trusted, revoked));
        }
        return list;
    }
}
