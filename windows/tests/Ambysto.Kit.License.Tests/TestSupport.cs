using System;
using System.Collections.Generic;
using System.Text;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>The published test key (docs/spec/license.md, "The published test key"). Tests only: the library never holds it.</summary>
internal static class TestKey
{
    public const byte Id = 0;
    public const string PublicHex = "3ccd24def13196dc0f90a3f2b94f180276d17fc24a5fa5388ecfa9508c87e5830aa31040704d5454ffd188c08994a46d0e41235cc249b181199492882ae479be";

    public static LicenseVerifier Verifier(Vector vector) => new(new TrustedKeys(vector.TrustedKeys), vector.Revoked);
}

/// <summary>Bytes to the key's text form, for tests that change key bytes. The library never encodes.</summary>
internal static class CrockfordEncoder
{
    public static string Encode(byte[] bytes)
    {
        var symbols = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                symbols.Append(CrockfordText.Alphabet[(buffer >> bits) & 31]);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0)
        {
            symbols.Append(CrockfordText.Alphabet[(buffer << (5 - bits)) & 31]);
        }
        var groups = new List<string>();
        for (var i = 0; i < symbols.Length; i += 5)
        {
            groups.Add(symbols.ToString(i, Math.Min(5, symbols.Length - i)));
        }
        return "AMB-" + string.Join('-', groups);
    }
}
