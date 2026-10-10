import Foundation

/// Payload layout, format version 1 (big-endian). docs/spec/license.md → "Payload".
struct LicensePayload: Equatable, Sendable {
    static let currentVersion: UInt8 = 1
    static let headerLength = 20
    static let signatureLength = 64
    static let maxLicenseeLength = 64
    /// 2026-01-01T00:00:00Z.
    static let epoch = Date(timeIntervalSince1970: 1_767_225_600)
    static let domain = Data("AMBYSTO-LICENSE-1".utf8)

    var version: UInt8 = currentVersion
    var signingKeyID: UInt8
    var keyID: [UInt8]          // 8 bytes
    var issuedDay: UInt16
    var updatesUntilDay: UInt16 // 0 = for life
    var seats: UInt8
    var productMask: UInt32
    var licensee: [UInt8]       // UTF-8, 1...64 bytes

    var bytes: Data {
        var data = Data([version, signingKeyID])
        data.append(contentsOf: keyID)
        data.append(contentsOf: [UInt8(issuedDay >> 8), UInt8(issuedDay & 0xFF)])
        data.append(contentsOf: [UInt8(updatesUntilDay >> 8), UInt8(updatesUntilDay & 0xFF)])
        data.append(seats)
        data.append(contentsOf: [UInt8(productMask >> 24), UInt8((productMask >> 16) & 0xFF),
                                 UInt8((productMask >> 8) & 0xFF), UInt8(productMask & 0xFF)])
        data.append(UInt8(licensee.count))
        data.append(contentsOf: licensee)
        return data
    }

    /// The message that is signed: the domain string, then the payload.
    var signedMessage: Data { Self.domain + bytes }

    /// Whole UTC days since the epoch, clamped so extreme or non-finite dates never trap.
    static func day(of date: Date) -> Int {
        let days = (date.timeIntervalSince(epoch) / 86_400).rounded(.down)
        guard days.isFinite else { return days > 0 ? Int.max : Int.min }
        return Int(max(min(days, 1e15), -1e15))
    }

    static func date(ofDay day: UInt16) -> Date {
        epoch.addingTimeInterval(TimeInterval(day) * 86_400)
    }

    /// Splits `payload ‖ signature`. Checks version, exact length and strict UTF-8, in that order.
    static func parse(_ data: Data) -> Result<(payload: LicensePayload, signature: Data), LicenseError> {
        let bytes = [UInt8](data)
        guard let version = bytes.first else { return .failure(.malformed) }
        guard version == currentVersion else { return .failure(.unsupportedVersion) }
        guard bytes.count > headerLength + signatureLength else { return .failure(.malformed) }
        let nameLength = Int(bytes[19])
        guard (1 ... maxLicenseeLength).contains(nameLength),
              bytes.count == headerLength + nameLength + signatureLength else { return .failure(.malformed) }
        let name = Array(bytes[headerLength ..< headerLength + nameLength])
        guard isValidUTF8(name) else { return .failure(.malformed) }
        let payload = LicensePayload(
            version: version, signingKeyID: bytes[1], keyID: Array(bytes[2 ..< 10]),
            issuedDay: UInt16(bytes[10]) << 8 | UInt16(bytes[11]),
            updatesUntilDay: UInt16(bytes[12]) << 8 | UInt16(bytes[13]),
            seats: bytes[14],
            productMask: UInt32(bytes[15]) << 24 | UInt32(bytes[16]) << 16 | UInt32(bytes[17]) << 8 | UInt32(bytes[18]),
            licensee: name)
        return .success((payload, Data(bytes[(headerLength + nameLength)...])))
    }

    static func isValidUTF8(_ bytes: [UInt8]) -> Bool {
        var iterator = bytes.makeIterator()
        var codec = UTF8()
        while true {
            switch codec.decode(&iterator) {
            case .scalarValue: continue
            case .emptyInput: return true
            case .error: return false
            }
        }
    }
}
