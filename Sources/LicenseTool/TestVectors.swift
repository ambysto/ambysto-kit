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
                     until: UInt16 = 0, signingKeyID: UInt8 = 0, nameBytes: [UInt8]? = nil) -> LicensePayloadView {
            LicensePayloadView(version: version, signingKeyID: signingKeyID, keyID: keyID, issuedDay: 282, updatesUntilDay: until,
                               seats: 3, productMask: products, licensee: nameBytes ?? Array(name.precomposedStringWithCanonicalMapping.utf8))
        }

        func signed(_ name: String, _ p: LicensePayloadView, signature: Data? = nil, expect: Expect,
                    revoked: [String] = [], mutateText: ((String) -> String)? = nil, trusted: [String: String] = trusted) {
            let message = p.signedMessage
            let sig = signature ?? signer.signature(for: message)
            var text = LicensePayloadView.text(p.bytes + sig)
            if let mutateText { text = mutateText(text) }
            vectors.append(Vector(name: name, text: text, payloadHex: Hex.encode(p.bytes), messageHex: Hex.encode(message),
                                  signatureHex: Hex.encode(sig), trustedKeys: trusted, revoked: revoked, expect: expect))
        }

        func ok(_ licensee: String, _ products: [String], keyID: String = "0102030405060708", until: Int = 0) -> Expect {
            Expect(valid: true, licensee: licensee, products: products, keyID: keyID, seats: 3, issuedDay: 282, updatesUntilDay: until)
        }
        func fail(_ error: LicenseError) -> Expect { Expect(valid: false, error: error.rawValue) }

        signed("valid single app", payload(), expect: ok("Nguyen Van A", ["space"]))
        signed("valid suite", payload(products: 0b111), expect: ok("Nguyen Van A", ["optimer", "space", "steady"]))
        signed("valid with updates until", payload(until: 647), expect: ok("Nguyen Van A", ["space"], until: 647))
        signed("valid Vietnamese licensee", payload("Nguyễn Văn Ánh"), expect: ok("Nguyễn Văn Ánh", ["space"]))
        signed("lowercase, spaces and line breaks", payload(), expect: ok("Nguyen Van A", ["space"]), mutateText: { text in
            let lower = text.lowercased().replacingOccurrences(of: "-", with: " ")
            let mid = lower.index(lower.startIndex, offsetBy: lower.count / 2)
            return String(lower[..<mid]) + "\r\n  " + String(lower[mid...])
        })
        signed("O, I and L read as 0 and 1", payload(), expect: ok("Nguyen Van A", ["space"]), mutateText: { text in
            var chars = Array(text)
            if let zero = chars.indices.dropFirst(4).first(where: { chars[$0] == "0" }) { chars[zero] = "O" }
            if let one = chars.indices.dropFirst(4).first(where: { chars[$0] == "1" }) { chars[one] = "l" }
            return String(chars)
        })
        // r with a leading zero byte: naive DER converters break on it.
        var leading = signer.signature(for: payload(keyID: [9, 9, 9, 9, 9, 9, 9, 9]).signedMessage)
        while leading.first != 0 { leading = signer.signature(for: payload(keyID: [9, 9, 9, 9, 9, 9, 9, 9]).signedMessage) }
        signed("signature r starts with 0x00", payload(keyID: [9, 9, 9, 9, 9, 9, 9, 9]), signature: leading,
               expect: ok("Nguyen Van A", ["space"], keyID: "0909090909090909"))
        // High-S twin of a valid signature: accepted.
        let base = signer.signature(for: payload(keyID: [7, 7, 7, 7, 7, 7, 7, 7]).signedMessage)
        let highS = base.prefix(32) + subtract(order, Array(base.suffix(32)))
        signed("high-S twin of a valid signature", payload(keyID: [7, 7, 7, 7, 7, 7, 7, 7]), signature: Data(highS),
               expect: ok("Nguyen Van A", ["space"], keyID: "0707070707070707"))
        signed("unknown product bit 20 is ignored", payload(products: (1 << 2) | (1 << 20)), expect: ok("Nguyen Van A", ["space"]))

        signed("tampered payload byte", payload(), expect: fail(.badSignature), mutateText: { _ in
            var bytes = payload().bytes + signer.signature(for: payload().signedMessage)
            bytes[14] ^= 0x01   // seats
            return LicensePayloadView.text(bytes)
        })
        signed("tampered signature byte", payload(), expect: fail(.badSignature), mutateText: { _ in
            var bytes = payload().bytes + signer.signature(for: payload().signedMessage)
            bytes[bytes.count - 1] ^= 0x01
            return LicensePayloadView.text(bytes)
        })
        signed("unknown signing key id", payload(signingKeyID: 9), expect: fail(.unknownSigningKey))
        signed("revoked key id", payload(), expect: fail(.revoked), revoked: ["0102030405060708"])
        signed("version 2", payload(version: 2), expect: fail(.unsupportedVersion))
        signed("wrong length", payload(), expect: fail(.malformed), mutateText: { _ in
            LicensePayloadView.text(payload().bytes + signer.signature(for: payload().signedMessage) + Data([0]))
        })
        signed("invalid UTF-8 licensee", payload(nameBytes: [0xC3, 0x28]), expect: fail(.malformed))
        signed("non-zero trailing bits", payload(), expect: fail(.malformed), mutateText: { text in
            var chars = Array(text)
            // 84 + 12 bytes = 96 bytes = 768 bits = 153.6 symbols → the last symbol carries 2 unused bits; set one.
            let last = chars.count - 1
            let alphabet = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZ")
            let value = alphabet.firstIndex(of: chars[last])!
            chars[last] = alphabet[value | 1]
            return String(chars)
        })
        signed("character # in the text", payload(), expect: fail(.malformed), mutateText: { $0 + "#" })
        signed("non-ASCII digit in the text", payload(), expect: fail(.malformed), mutateText: { $0.replacingOccurrences(of: "-", with: "-１", options: [], range: $0.range(of: "-")) })
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
