import AmbystoLicense
import CryptoKit
import Foundation

/// Generates docs/spec/license-test-vectors.json with the published test key only.
/// Each vector carries the raw bytes so another platform can check its implementation (plan §1).
enum TestVectors {
    struct Vector: Codable {
        var name: String
        var text: String
        var payloadHex: String?
        var messageHex: String?
        var signatureHex: String?
        var trustedKeys: [String: String]
        var revoked: [String]
        var expect: Expect
    }

    struct Expect: Codable, Equatable {
        var valid: Bool
        var error: String?
        var licensee: String?
        var products: [String]?
        var keyID: String?
        var seats: Int?
        var issuedDay: Int?
        var updatesUntilDay: Int?
    }

    /// P-256 group order n (for the high-S twin s' = n − s).
    static let order: [UInt8] = Array(Hex.decode("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551")!)

    static func generate() -> [Vector] {
        let key = try! P256.Signing.PrivateKey(rawRepresentation: Hex.decode(TestKey.privateHex)!)
        let signer = LicenseSigner(privateKey: key, signingKeyID: TestKey.id)
        let trusted = ["0": TestKey.publicHex]
        var vectors: [Vector] = []

        func payload(_ name: String = "Nguyen Van A", products: UInt32 = 1 << 2, version: UInt8 = 1, keyID: [UInt8] = [1, 2, 3, 4, 5, 6, 7, 8],
                     until: UInt16 = 0, seats: UInt8 = 3, signingKeyID: UInt8 = 0, nameBytes: [UInt8]? = nil) -> LicensePayloadView {
            LicensePayloadView(version: version, signingKeyID: signingKeyID, keyID: keyID, issuedDay: 282, updatesUntilDay: until,
                               seats: seats, productMask: products, licensee: nameBytes ?? Array(name.precomposedStringWithCanonicalMapping.utf8))
        }

        /// Adds a vector. `bytes` replaces the key bytes (payload ‖ signature) when a case needs odd bytes; the hex fields
        /// always describe exactly what the text decodes to.
        func add(_ name: String, _ p: LicensePayloadView, signature: Data? = nil, bytes: Data? = nil, expect: Expect,
                 revoked: [String] = [], text: ((String) -> String)? = nil) {
            let sig = signature ?? signer.signature(for: p.signedMessage)
            let keyBytes = bytes ?? (p.bytes + sig)
            var keyText = LicensePayloadView.text(keyBytes)
            if let text { keyText = text(keyText) }
            var payloadHex: String?, messageHex: String?, signatureHex: String?
            if keyBytes.count > 20 + 64 {
                let payloadBytes = keyBytes.prefix(keyBytes.count - 64)
                payloadHex = Hex.encode(payloadBytes)
                messageHex = Hex.encode(Data("AMBYSTO-LICENSE-1".utf8) + payloadBytes)
                signatureHex = Hex.encode(keyBytes.suffix(64))
            }
            vectors.append(Vector(name: name, text: keyText, payloadHex: payloadHex, messageHex: messageHex, signatureHex: signatureHex,
                                  trustedKeys: trusted, revoked: revoked, expect: expect))
        }

        func ok(_ licensee: String = "Nguyen Van A", products: [String] = ["space"], keyID: String = "0102030405060708",
                until: Int = 0, seats: Int = 3) -> Expect {
            Expect(valid: true, licensee: licensee, products: products, keyID: keyID, seats: seats, issuedDay: 282, updatesUntilDay: until)
        }
        func fail(_ error: LicenseError) -> Expect { Expect(valid: false, error: error.rawValue) }
        let base = payload()
        let baseBytes = base.bytes + signer.signature(for: base.signedMessage)

        // Valid keys.
        add("valid single app", base, expect: ok())
        add("valid suite", payload(products: 0b111), expect: ok(products: ["optimer", "space", "steady"]))
        add("valid with updates until", payload(until: 647), expect: ok(until: 647))
        add("valid Vietnamese licensee", payload("Nguyễn Văn Ánh"), expect: ok("Nguyễn Văn Ánh"))
        add("lowercase, spaces and line breaks", base, bytes: baseBytes, expect: ok(), text: { text in
            let lower = text.lowercased().replacingOccurrences(of: "-", with: " ")
            let mid = lower.index(lower.startIndex, offsetBy: lower.count / 2)
            return String(lower[..<mid]) + "\r\n  " + String(lower[mid...])
        })
        add("no AMB prefix", base, bytes: baseBytes, expect: ok(), text: { String($0.dropFirst(4)) })
        add("leading spaces before a lowercase prefix", base, bytes: baseBytes, expect: ok(), text: { "  amb-" + $0.dropFirst(4) })
        add("O, I and L read as 0 and 1", base, bytes: baseBytes, expect: ok(), text: { text in
            var chars = Array(text)
            if let zero = chars.indices.dropFirst(4).first(where: { chars[$0] == "0" }) { chars[zero] = "O" }
            if let one = chars.indices.dropFirst(4).first(where: { chars[$0] == "1" }) { chars[one] = "l" }
            return String(chars)
        })
        // r with a leading zero byte: naive DER converters break on it.
        let nines = payload(keyID: [9, 9, 9, 9, 9, 9, 9, 9])
        var leading = signer.signature(for: nines.signedMessage)
        while leading.first != 0 { leading = signer.signature(for: nines.signedMessage) }
        add("signature r starts with 0x00", nines, signature: leading, expect: ok(keyID: "0909090909090909"))
        // High-S: s' = max(s, n − s), so the vector is high-S whatever CryptoKit returned.
        let sevens = payload(keyID: [7, 7, 7, 7, 7, 7, 7, 7])
        let lowOrHigh = signer.signature(for: sevens.signedMessage)
        let s = Array(lowOrHigh.suffix(32)), twin = subtract(order, s)
        let high = s.lexicographicallyPrecedes(twin) ? twin : s
        add("high-S signature", sevens, signature: lowOrHigh.prefix(32) + Data(high), expect: ok(keyID: "0707070707070707"))
        add("unknown product bit 20 is ignored", payload(products: (1 << 2) | (1 << 20)), expect: ok())
        // Issuer-only rules: verifiers do not enforce them.
        add("updates until before issued (issuer rule only)", payload(until: 100), expect: ok(until: 100))
        add("empty product mask (issuer rule only)", payload(products: 0), expect: ok(products: []))
        add("zero seats (issuer rule only)", payload(seats: 0), expect: ok(seats: 0))
        add("NUL in licensee (issuer rule only)", payload(nameBytes: Array("A\u{0}B".utf8)), expect: ok("A\u{0}B"))

        // Refused keys.
        var tampered = baseBytes
        tampered[14] ^= 0x01
        add("tampered payload byte", base, bytes: tampered, expect: fail(.badSignature))
        var badSignature = baseBytes
        badSignature[badSignature.count - 1] ^= 0x01
        add("tampered signature byte", base, bytes: badSignature, expect: fail(.badSignature))
        add("tampered and revoked: the signature is checked first", base, bytes: tampered, expect: fail(.badSignature),
            revoked: ["0102030405060708"])
        add("unknown signing key id", payload(signingKeyID: 9), expect: fail(.unknownSigningKey))
        add("revoked key id", base, expect: fail(.revoked), revoked: ["0102030405060708"])
        add("version 2", payload(version: 2), expect: fail(.unsupportedVersion))
        add("version 2 with a short length: version is checked first", base, bytes: Data([2, 0, 1, 2, 3]), expect: fail(.unsupportedVersion))
        add("wrong length", base, bytes: baseBytes + Data([0]), expect: fail(.malformed))
        var mismatch = baseBytes
        mismatch[19] += 1
        add("licensee length byte does not match the total", base, bytes: mismatch, expect: fail(.malformed))
        add("licensee length 0", payload(nameBytes: []), expect: fail(.malformed))
        add("licensee length 65", payload(nameBytes: Array(repeating: 0x41, count: 65)), expect: fail(.malformed))
        add("invalid UTF-8 licensee", payload(nameBytes: [0xC3, 0x28]), expect: fail(.malformed))
        add("non-zero trailing bits", base, bytes: baseBytes, expect: fail(.malformed), text: { text in
            // 96 bytes = 768 bits = 154 symbols carrying 770 bits: the last symbol has 2 unused bits; set one.
            var chars = Array(text)
            let alphabet = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ")
            chars[chars.count - 1] = alphabet[alphabet.firstIndex(of: chars[chars.count - 1])! | 1]
            return String(chars)
        })
        for drop in [1, 3, 6] {
            add("symbol count with remainder \(drop) modulo 8", base, bytes: baseBytes, expect: fail(.malformed), text: { text in
                let symbols = Array(text.dropFirst(4).filter { $0 != "-" })
                let target = (symbols.count / 8 - 1) * 8 + drop   // always shorter than the real key
                return "AMB-" + String(symbols.prefix(target))
            })
        }
        add("letter U", base, bytes: baseBytes, expect: fail(.malformed), text: { $0 + "U" })
        add("character # in the text", base, bytes: baseBytes, expect: fail(.malformed), text: { $0 + "#" })
        add("non-ASCII digit in the text", base, bytes: baseBytes, expect: fail(.malformed),
            text: { "AMB-１" + $0.dropFirst(4) })
        vectors.append(Vector(name: "empty", text: "", payloadHex: nil, messageHex: nil, signatureHex: nil,
                              trustedKeys: trusted, revoked: [], expect: fail(.malformed)))
        return vectors
    }

    /// Big-endian a − b for 32-byte numbers with a > b.
    static func subtract(_ a: [UInt8], _ b: [UInt8]) -> [UInt8] {
        var result = [UInt8](repeating: 0, count: 32)
        var borrow = 0
        for i in stride(from: 31, through: 0, by: -1) {
            var value = Int(a[i]) - Int(b[i]) - borrow
            borrow = value < 0 ? 1 : 0
            if value < 0 { value += 256 }
            result[i] = UInt8(value)
        }
        return result
    }

    static func json() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(generate())
    }
}

/// The payload layout, rebuilt here from the spec (not from the library) so vectors also test the library's parser.
struct LicensePayloadView {
    var version: UInt8, signingKeyID: UInt8, keyID: [UInt8], issuedDay: UInt16, updatesUntilDay: UInt16, seats: UInt8
    var productMask: UInt32, licensee: [UInt8]

    var bytes: Data {
        var d = Data([version, signingKeyID]) + Data(keyID)
        d += Data([UInt8(issuedDay >> 8), UInt8(issuedDay & 0xFF), UInt8(updatesUntilDay >> 8), UInt8(updatesUntilDay & 0xFF), seats])
        d += Data([UInt8(productMask >> 24), UInt8((productMask >> 16) & 0xFF), UInt8((productMask >> 8) & 0xFF), UInt8(productMask & 0xFF)])
        return d + Data([UInt8(licensee.count)]) + Data(licensee)
    }

    var signedMessage: Data { Data("AMBYSTO-LICENSE-1".utf8) + bytes }

    static func text(_ data: Data) -> String {
        let alphabet = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ")
        var out: [Character] = []
        var buffer = 0, bits = 0
        for byte in data {
            buffer = (buffer << 8) | Int(byte); bits += 8
            while bits >= 5 { out.append(alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5 }
            buffer &= (1 << bits) - 1
        }
        if bits > 0 { out.append(alphabet[(buffer << (5 - bits)) & 31]) }
        return "AMB-" + stride(from: 0, to: out.count, by: 5).map { String(out[$0 ..< min($0 + 5, out.count)]) }.joined(separator: "-")
    }
}
