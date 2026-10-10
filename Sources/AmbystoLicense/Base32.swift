import Foundation

/// Crockford Base32, ASCII only. docs/spec/license.md → "Text".
enum Base32 {
    static let alphabet = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ".utf8)

    static func encode(_ data: Data) -> String {
        var out: [UInt8] = []
        var buffer = 0, bits = 0
        for byte in data {
            buffer = (buffer << 8) | Int(byte)
            bits += 8
            while bits >= 5 {
                out.append(alphabet[(buffer >> (bits - 5)) & 31])
                bits -= 5
            }
            buffer &= (1 << bits) - 1
        }
        if bits > 0 { out.append(alphabet[(buffer << (5 - bits)) & 31]) }
        return String(decoding: out, as: UTF8.self)
    }

    /// The license text form: `AMB-` and groups of five.
    static func licenseText(_ data: Data) -> String {
        let chars = Array(encode(data))
        let groups = stride(from: 0, to: chars.count, by: 5).map { String(chars[$0 ..< min($0 + 5, chars.count)]) }
        return "AMB-" + groups.joined(separator: "-")
    }

    /// Parses a license text. `nil` = malformed.
    static func decodeLicenseText(_ text: String) -> Data? {
        let ignorable: Set<UInt8> = [UInt8(ascii: "-"), UInt8(ascii: " "), 9, 13, 10]
        var symbols = Array(text.utf8.filter { !ignorable.contains($0) })
        // Strip a leading "AMB" in any case (v1 data always starts with "0", so this is unambiguous).
        if symbols.count >= 3, symbols[0] | 0x20 == UInt8(ascii: "a"), symbols[1] | 0x20 == UInt8(ascii: "m"),
           symbols[2] | 0x20 == UInt8(ascii: "b") {
            symbols.removeFirst(3)
        }
        guard !symbols.isEmpty, ![1, 3, 6].contains(symbols.count % 8) else { return nil }
        var values: [Int] = []
        values.reserveCapacity(symbols.count)
        for symbol in symbols {
            guard let value = value(of: symbol) else { return nil }
            values.append(value)
        }
        var out = Data()
        var buffer = 0, bits = 0
        for value in values {
            buffer = (buffer << 5) | value
            bits += 5
            if bits >= 8 {
                out.append(UInt8((buffer >> (bits - 8)) & 0xFF))
                bits -= 8
                buffer &= (1 << bits) - 1
            }
        }
        // Unused trailing bits must be zero.
        guard buffer == 0 else { return nil }
        return out
    }

    private static func value(of symbol: UInt8) -> Int? {
        switch symbol {
        case UInt8(ascii: "0") ... UInt8(ascii: "9"): return Int(symbol - UInt8(ascii: "0"))
        case UInt8(ascii: "a") ... UInt8(ascii: "z"): return value(of: symbol - 32)
        case UInt8(ascii: "O"): return 0
        case UInt8(ascii: "I"), UInt8(ascii: "L"): return 1
        case UInt8(ascii: "A") ... UInt8(ascii: "Z"):
            guard let index = alphabet.firstIndex(of: symbol) else { return nil }   // U
            return index
        default: return nil
        }
    }
}
