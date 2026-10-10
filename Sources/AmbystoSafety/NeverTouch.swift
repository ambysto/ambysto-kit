// From Ambysto Space (ambysto/ambysto-space@1ab11ea, macos/Sources/SpaceCore/Safety), merged into Ambysto Kit 0.2 (APS-11).
import Darwin
import Foundation

/// Paths refused at selection and at delete time, including everything beneath them, and refused when an item
/// *contains* one of them. Suite-wide; apps add their own data folders through
/// `SafetyGate(additionalProtectedRoots:)`. docs/spec/safety.md → "Never touch".
public enum NeverTouch {
    /// Relative to the home folder.
    public static let homeRelativeRoots = [
        ".claude/projects",
        ".ssh",
        ".gnupg",
        ".Trash",
        "Pictures",
        "Library/Keychains",
        "Library/Mail",
        "Library/Messages",
        "Library/Mobile Documents",
        "Library/CloudStorage",
        "Library/Calendars",
        "Library/Accounts",
        "Library/Application Support/MobileSync/Backup",
        "Library/Application Support/com.apple.TCC",
        "Library/Application Support/AddressBook",
        "Library/Safari",
        "Library/Containers/com.apple.Safari",
        "Library/Application Support/Google/Chrome",
        "Library/Application Support/Arc",
        "Library/Application Support/Firefox",
        "Library/Application Support/BraveSoftware",
        "Library/Application Support/Microsoft Edge",
    ]

    /// System locations, absolute.
    public static let defaultSystemRoots = ["/System", "/Library", "/private", "/usr", "/opt", "/cores", "/bin", "/sbin", "/Volumes"]

    /// Any path with one of these components is refused wherever it lives.
    public static let protectedComponentSuffixes = [".photoslibrary", ".backupbundle", "Backups.backupdb", ".keychain-db", ".keychain"]

    /// Children of these `~/Library` folders whose names start with `com.apple.` / `group.com.apple.` belong to Apple.
    static let appleOwnedParents = ["Library/Containers", "Library/Group Containers", "Library/Application Support"]

    public static func roots(home: URL, additional: [String] = []) -> [String] {
        let homePath = RealPath.resolve(home.path) ?? home.standardizedFileURL.path
        return (homeRelativeRoots + additional).map { relative in
            let candidate = homePath + "/" + relative
            return RealPath.resolve(candidate) ?? candidate
        }
    }

    public static func hasProtectedComponent(_ path: String) -> Bool {
        path.split(separator: "/").contains { component in
            protectedComponentSuffixes.contains { component.hasSuffix($0) }
        }
    }

    /// `~/Library/Containers/com.apple.X/...` and friends.
    static func isAppleOwned(_ realPath: String, homePath: String) -> Bool {
        for parent in appleOwnedParents {
            let prefix = homePath + "/" + parent + "/"
            guard realPath.hasPrefix(prefix) else { continue }
            let child = realPath.dropFirst(prefix.count).prefix { $0 != "/" }
            if child.hasPrefix("com.apple.") || child.hasPrefix("group.com.apple.") { return true }
        }
        return false
    }

    /// The OS marks SIP-protected items `restricted`.
    static func isRestricted(_ path: String) -> Bool {
        var info = stat()
        guard lstat(path, &info) == 0 else { return false }
        return (info.st_flags & UInt32(SF_RESTRICTED)) != 0
    }
}

public enum RealPath {
    /// `realpath(3)`: resolves every symlink in the path and normalizes letter case. `nil` if the path does not exist.
    public static func resolve(_ path: String) -> String? {
        guard let resolved = realpath(path, nil) else { return nil }
        defer { free(resolved) }
        return String(cString: resolved)
    }

    /// `path` equals `root` or lies beneath it.
    public static func isInside(_ path: String, _ root: String) -> Bool {
        path == root || path.hasPrefix(root.hasSuffix("/") ? root : root + "/")
    }
}
