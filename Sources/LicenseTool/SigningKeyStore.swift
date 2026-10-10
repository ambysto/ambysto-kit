import Foundation
import KeychainACL
import Security

/// The one place a signing key is stored or read (intent §3: switching to a password manager later changes only this).
protocol SigningKeyStore {
    func exists(id: UInt8) -> Bool
    func store(_ key: Data, id: UInt8) throws
    func read(id: UInt8) throws -> Data
}

struct ToolError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

/// The file-based login keychain, never the data-protection keychain (it ignores this access list).
/// The item trusts no application, so macOS asks for the login password on every read.
struct KeychainStore: SigningKeyStore {
    static let serviceName = "com.ambysto.license-signing"
    private var service: CFString { Self.serviceName as CFString }

    private func account(_ id: UInt8) -> CFString { "signing-key-\(id)" as CFString }

    func exists(id: UInt8) -> Bool {
        // Attributes only: no secret is read, so no prompt.
        let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword, kSecAttrService: service,
                                      kSecAttrAccount: account(id), kSecReturnAttributes: true, kSecMatchLimit: kSecMatchLimitOne]
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }

    func store(_ key: Data, id: UInt8) throws {
        guard !exists(id: id) else { throw ToolError("A signing key with id \(id) already exists in the Keychain; nothing was changed.") }
        let status = ambysto_add_untrusted_generic_password(service, account(id), key as CFData)
        guard status == errSecSuccess else { throw ToolError("The Keychain refused the key (\(Self.message(status))).") }
    }

    func read(id: UInt8) throws -> Data {
        let query: [CFString: Any] = [kSecClass: kSecClassGenericPassword, kSecAttrService: service,
                                      kSecAttrAccount: account(id), kSecReturnData: true, kSecMatchLimit: kSecMatchLimitOne]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let data = result as? Data else { throw ToolError("The Keychain item is empty.") }
            return data
        case errSecInteractionNotAllowed:
            throw ToolError("Run this in a logged-in session on the Mac; macOS must ask for your password.")
        case errSecItemNotFound:
            throw ToolError("No signing key with id \(id) in this Mac's Keychain. Run new-signing-key or import-signing-key.")
        case errSecUserCanceled, errSecAuthFailed:
            throw ToolError("Cancelled: the Keychain was not unlocked for this read.")
        default:
            throw ToolError("The Keychain refused the read (\(Self.message(status))).")
        }
    }

    static func message(_ status: OSStatus) -> String {
        (SecCopyErrorMessageString(status, nil) as String?) ?? "OSStatus \(status)"
    }
}

#if DEBUG
/// Development only: a plain file holding the published test key. Refuses any other signing key id.
struct FileStore: SigningKeyStore {
    let url: URL

    func exists(id: UInt8) -> Bool { FileManager.default.fileExists(atPath: url.path) }

    func store(_ key: Data, id: UInt8) throws {
        guard id == 0 else { throw ToolError("The file store holds only the published test key (id 0).") }
        try key.write(to: url, options: .withoutOverwriting)
    }

    func read(id: UInt8) throws -> Data {
        guard id == 0 else { throw ToolError("The file store holds only the published test key (id 0).") }
        return try Data(contentsOf: url)
    }
}
#endif
