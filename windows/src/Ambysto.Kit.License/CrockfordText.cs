using System;

namespace Ambysto.Kit.Licensing;

/// <summary>
/// The key's text form to bytes (docs/spec/license.md, "Text"). ASCII only: characters are compared as
/// code units, never through Unicode letter, digit or case rules (full-width and Arabic-Indic digits are
/// digits to <c>char.IsDigit</c>, and casing rules differ between cultures).
/// </summary>
internal static class CrockfordText
{
    internal const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The key bytes, or <c>null</c> when the text is malformed.</summary>
    internal static byte[]? Decode(string text)
    {
        // Rule 1: drop '-', space, tab, CR and LF.
        var symbols = new char[text.Length];
        var count = 0;
        foreach (var c in text)
        {
            if (c is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }
            symbols[count++] = c;
        }

        // Rule 2: drop a leading "AMB" in any ASCII case.
        var start = 0;
        if (count >= 3 && IsAscii(symbols[0], 'A') && IsAscii(symbols[1], 'M') && IsAscii(symbols[2], 'B'))
        {
            start = 3;
        }

        var length = count - start;
        // Rule 4 (owner decision 2026-10-16): no symbols left is malformed. Then the remainder rule.
        if (length == 0 || (length % 8) is 1 or 3 or 6)
        {
            return null;
        }

        var bytes = new byte[length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var written = 0;
        for (var i = start; i < count; i++)
        {
            // Rule 3: ASCII letters and digits only; O reads as 0, I and L as 1; U and anything else is malformed.
            var value = SymbolValue(symbols[i]);
            if (value < 0)
            {
                return null;
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[written++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        // Rule 4: the unused bits of the last symbol must be zero.
        return buffer == 0 ? bytes : null;
    }

    /// <summary>Whether <paramref name="c"/> is the ASCII upper-case letter <paramref name="upper"/> in either case.</summary>
    private static bool IsAscii(char c, char upper) => c == upper || c == (char)(upper + 32);

    private static int SymbolValue(char c)
    {
        if (c is >= 'a' and <= 'z')
        {
            c = (char)(c - 32);
        }
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }
        if (c is < 'A' or > 'Z')
        {
            return -1;
        }
        return c switch
        {
            'O' => 0,
            'I' or 'L' => 1,
            'U' => -1,
            _ => Alphabet.IndexOf(c, StringComparison.Ordinal),
        };
    }
}
