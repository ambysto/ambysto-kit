import CryptoKit
import Foundation

/// Issues licenses. `package` access: the owner's tool and the tests only, never apps.
package struct LicenseSigner {
    package enum InputError: Error, Equatable, CustomStringConvertible {
        case licenseeEmpty, licenseeTooLong, licenseeHasControlCharacters, noProducts, seats, updatesBeforeIssued, dateOutOfRange

        package var description: String {
            switch self {
            case .licenseeEmpty: "The licensee name is empty."
            case .licenseeTooLong: "The licensee name is longer than 64 bytes; shorten it (it is never truncated)."
            case .licenseeHasControlCharacters: "The licensee name has control or text-direction characters."
            case .noProducts: "Name at least one product."
            case .seats: "Seats must be 1 to 255."
            case .updatesBeforeIssued: "--updates-until is before today."
            case .dateOutOfRange: "The date is outside 2026-01-01 … 2205."
            }
        }
    }

    let privateKey: P256.Signing.PrivateKey
    let signingKeyID: UInt8

    package var publicKeyRaw: Data { privateKey.publicKey.rawRepresentation }

    package init(privateKey: P256.Signing.PrivateKey, signingKeyID: UInt8) {
        self.privateKey = privateKey
        self.signingKeyID = signingKeyID
    }

    /// Bidi and other formatting controls that would make a name display misleadingly.
    static let forbiddenScalars: Set<UInt32> = [0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E, 0x2066, 0x2067, 0x2068, 0x2069, 0x061C]

    package static func normalizedLicensee(_ name: String) -> Result<[UInt8], InputError> {
        let nfc = name.precomposedStringWithCanonicalMapping
        let bytes = Array(nfc.utf8)
        guard !bytes.isEmpty else { return .failure(.licenseeEmpty) }
        guard bytes.count <= LicensePayload.maxLicenseeLength else { return .failure(.licenseeTooLong) }
        for scalar in nfc.unicodeScalars where scalar.properties.generalCategory == .control || forbiddenScalars.contains(scalar.value) {
            _ = scalar
            return .failure(.licenseeHasControlCharacters)
        }
        return .success(bytes)
    }

    /// Builds and signs a license; returns its `AMB-` text and key id.
    package func issue(licensee: String, products: Set<Product>, seats: Int = 3, updatesUntil: Date? = nil,
                       issued: Date = Date(), keyID: [UInt8]? = nil) -> Result<(text: String, keyID: String), InputError> {
        let name: [UInt8]
        switch Self.normalizedLicensee(licensee) {
        case .failure(let error): return .failure(error)
        case .success(let bytes): name = bytes
        }
        guard !products.isEmpty else { return .failure(.noProducts) }
        guard (1 ... 255).contains(seats) else { return .failure(.seats) }
        let issuedDay = LicensePayload.day(of: issued)
        guard (0 ... Int(UInt16.max)).contains(issuedDay) else { return .failure(.dateOutOfRange) }
        var untilDay = 0
        if let updatesUntil {
            untilDay = LicensePayload.day(of: updatesUntil)
            guard untilDay >= issuedDay else { return .failure(.updatesBeforeIssued) }
            guard (1 ... Int(UInt16.max)).contains(untilDay) else { return .failure(.dateOutOfRange) }
        }
        let id = keyID ?? (0 ..< 8).map { _ in UInt8.random(in: 0 ... 255) }
        let payload = LicensePayload(signingKeyID: signingKeyID, keyID: id, issuedDay: UInt16(issuedDay),
                                     updatesUntilDay: UInt16(untilDay), seats: UInt8(seats),
                                     productMask: Product.mask(products), licensee: name)
        return .success((sign(payload), Hex.encode(id)))
    }

    /// Signs any payload bytes (test vectors use this for deliberately odd payloads).
    func sign(_ payload: LicensePayload) -> String {
        Base32.licenseText(payload.bytes + signature(for: payload.signedMessage))
    }

    package func signature(for message: Data) -> Data {
        // CryptoKit's P-256 signature over SHA-256 of the message; 64 bytes raw r‖s.
        (try? privateKey.signature(for: message).rawRepresentation) ?? Data()
    }
}
