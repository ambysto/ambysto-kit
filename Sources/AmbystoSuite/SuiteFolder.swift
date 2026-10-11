import Darwin
import Foundation

/// One shared suite folder (spec "The shared folders"): `glances/`, `host.lock`, `settings.json`.
public struct SuiteFolder: Sendable, Hashable {
    public let url: URL

    public init(url: URL) {
        self.url = url.standardizedFileURL
    }

    public var glancesURL: URL { url.appendingPathComponent("glances", isDirectory: true) }
    public var lockURL: URL { url.appendingPathComponent("host.lock", isDirectory: false) }
    public var settingsURL: URL { url.appendingPathComponent("settings.json", isDirectory: false) }

    public func glanceURL(for app: SuiteApp) -> URL {
        glancesURL.appendingPathComponent(app.glanceFileName, isDirectory: false)
    }

    /// `~/Library/Application Support/Ambysto/Suite/` of the user (not of a sandbox container).
    public static func plain(home: URL = SuiteFolder.realHome) -> SuiteFolder {
        SuiteFolder(url: home.appendingPathComponent("Library/Application Support/Ambysto/Suite", isDirectory: true))
    }

    /// The team's shared container, when this app has the `<team id>.com.ambysto.suite` group entitlement.
    public static func group(identifier: String) -> SuiteFolder? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: identifier).map(SuiteFolder.init(url:))
    }

    /// Every suite folder this app can reach: the plain folder unless sandboxed, plus the group folder when entitled.
    public static func reachable(groupIdentifier: String? = nil) -> [SuiteFolder] {
        var folders: [SuiteFolder] = []
        if !isSandboxed { folders.append(.plain()) }
        if let groupIdentifier, let group = group(identifier: groupIdentifier) { folders.append(group) }
        return folders
    }

    /// Creates the folder and `glances/` with owner-only permissions. Never removes anything.
    public func prepare() throws {
        let manager = FileManager.default
        for directory in [url, glancesURL] {
            try manager.createDirectory(at: directory, withIntermediateDirectories: true,
                                        attributes: [.posixPermissions: 0o700])
        }
    }

    public static var isSandboxed: Bool {
        ProcessInfo.processInfo.environment["APP_SANDBOX_CONTAINER_ID"] != nil
    }

    /// The account's home folder from the user database, so a sandbox's redirected `HOME` is never used.
    public static var realHome: URL {
        if let entry = getpwuid(getuid()), let dir = entry.pointee.pw_dir {
            return URL(fileURLWithPath: String(cString: dir), isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
    }
}
