import CryptoKit
import Foundation

/// Verifies `AMB-` license keys offline. docs/spec/license.md → "Verification order".
public struct LicenseVerifier: Sendable {
    public static let maxFileBytes = 4_096

    let trusted: TrustedKeys
    let revoked: Set<String>

    public init(trusted: TrustedKeys = .production, revoked: Set<String> = Revocations.all) {
        self.trusted = trusted
        self.revoked = revoked
    }

    public func verify(_ text: String) -> Result<License, LicenseError> {
        guard let data = Base32.decodeLicenseText(text) else { return .failure(.malformed) }
        let parsed: (payload: LicensePayload, signature: Data)
        switch LicensePayload.parse(data) {
        case .failure(let error): return .failure(error)
        case .success(let value): parsed = value
        }
        guard let key = trusted.keys[parsed.payload.signingKeyID] else { return .failure(.unknownSigningKey) }
        guard let signature = try? P256.Signing.ECDSASignature(rawRepresentation: parsed.signature),
              key.isValidSignature(signature, for: parsed.payload.signedMessage) else { return .failure(.badSignature) }
        let license = License(parsed.payload)
        guard !revoked.contains(license.keyID) else { return .failure(.revoked) }
        return .success(license)
    }

    /// Reads an `.ambystokey` file (at most 4 KB, UTF-8 text).
    public func verify(fileAt url: URL) -> Result<License, LicenseError> {
        guard let size = (try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize, size <= Self.maxFileBytes,
              let data = try? Data(contentsOf: url), LicensePayload.isValidUTF8([UInt8](data)) else { return .failure(.malformed) }
        return verify(String(decoding: data, as: UTF8.self))
    }
}
