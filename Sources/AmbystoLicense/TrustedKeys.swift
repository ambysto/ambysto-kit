import CryptoKit
import Foundation

/// Public keys that may sign licenses, by signing key id. docs/spec/license.md → "Rotation".
public struct TrustedKeys: Sendable {
    let keys: [UInt8: P256.Signing.PublicKey]

    package init(rawHex: [UInt8: String]) {
        var keys: [UInt8: P256.Signing.PublicKey] = [:]
        for (id, hex) in rawHex {
            if let bytes = Hex.decode(hex), let key = try? P256.Signing.PublicKey(rawRepresentation: bytes) { keys[id] = key }
        }
        self.keys = keys
    }

    public var ids: [Int] { keys.keys.map(Int.init).sorted() }

    /// The keys release builds trust. Id 1 is added at release, after the owner creates it (plan, group 6).
    public static let production = TrustedKeys(rawHex: [:])

    /// The published test key (id 0). Never trusted by release builds of apps; `package` access only.
    package static let test = TrustedKeys(rawHex: [0: TestKey.publicHex])
}

/// The test signing key, published on purpose in docs/spec/license.md. Never trusted by release builds.
package enum TestKey {
    package static let id: UInt8 = 0
    package static let privateHex = "f8de81f5c79f55a23fb098212cd946c8655dba4139b180d7b2d9a2d65c683d8e"
    package static let publicHex = "3ccd24def13196dc0f90a3f2b94f180276d17fc24a5fa5388ecfa9508c87e5830aa31040704d5454ffd188c08994a46d0e41235cc249b181199492882ae479be"
}

package enum Hex {
    package static func encode(_ bytes: some Sequence<UInt8>) -> String {
        bytes.map { String(format: "%02x", $0) }.joined()
    }

    package static func decode(_ text: String) -> Data? {
        let chars = Array(text.utf8)
        guard chars.count % 2 == 0 else { return nil }
        var out = Data(capacity: chars.count / 2)
        var i = 0
        while i < chars.count {
            guard let hi = nibble(chars[i]), let lo = nibble(chars[i + 1]) else { return nil }
            out.append(hi << 4 | lo)
            i += 2
        }
        return out
    }

    private static func nibble(_ c: UInt8) -> UInt8? {
        switch c {
        case UInt8(ascii: "0") ... UInt8(ascii: "9"): c - UInt8(ascii: "0")
        case UInt8(ascii: "a") ... UInt8(ascii: "f"): c - UInt8(ascii: "a") + 10
        case UInt8(ascii: "A") ... UInt8(ascii: "F"): c - UInt8(ascii: "A") + 10
        default: nil
        }
    }
}
