/// Why a key was refused. One plain reason per failure. docs/spec/license.md → "Reasons".
public enum LicenseError: String, Error, Sendable, CaseIterable {
    case malformed, unsupportedVersion, unknownSigningKey, badSignature, revoked

    public var message: String {
        switch self {
        case .malformed: "This key was mistyped or damaged. Paste it again, or open the .ambystokey file."
        case .unsupportedVersion: "This key needs a newer version of the app."
        case .unknownSigningKey: "This key was not issued by Ambysto, or needs a newer version of the app."
        case .badSignature: "This key was changed after it was issued."
        case .revoked: "This key has been withdrawn. Contact Ambysto support."
        }
    }
}
