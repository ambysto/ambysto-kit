// From Ambysto Space (ambysto/ambysto-space@1ab11ea, macos/Sources/SpaceCore/Safety), merged into Ambysto Kit 0.2 (APS-11).
import Foundation

/// Maps Data-volume spellings to the paths users see and the gate compares.
/// `/System/Volumes/Data/Users/x` and `/Users/x` are one inode but two strings; `realpath` keeps both.
/// The table comes from `/usr/share/firmlinks` (lines "<path on root>\t<path relative to Data>").
public struct PathCanonicalizer: Sendable {
    public static let dataVolumePrefix = "/System/Volumes/Data"
    public static let shared = PathCanonicalizer(firmlinksTable: (try? String(contentsOfFile: "/usr/share/firmlinks", encoding: .utf8)) ?? "")

    /// Relative path on the Data volume → user-facing absolute path, longest first.
    let mappings: [(dataRelative: String, visible: String)]

    public init(firmlinksTable: String) {
        var mappings: [(String, String)] = []
        for line in firmlinksTable.split(whereSeparator: \.isNewline) {
            let parts = line.split(separator: "\t", maxSplits: 1).map(String.init)
            guard parts.count == 2, parts[0].hasPrefix("/") else { continue }
            mappings.append((parts[1], parts[0]))
        }
        self.mappings = mappings.sorted { $0.0.count > $1.0.count }
    }

    /// Converts a `/System/Volumes/Data/...` path to its firmlinked form when one exists. Other paths are returned unchanged.
    public func visiblePath(_ path: String) -> String {
        let prefix = Self.dataVolumePrefix + "/"
        guard path.hasPrefix(prefix) else { return path }
        let relative = String(path.dropFirst(prefix.count))
        for mapping in mappings where relative == mapping.dataRelative || relative.hasPrefix(mapping.dataRelative + "/") {
            return mapping.visible + relative.dropFirst(mapping.dataRelative.count)
        }
        return path
    }

    /// The reverse of `visiblePath`: `/Users/x` → `/System/Volumes/Data/Users/x` when `/Users` is a firmlink.
    /// Paths that are not under a firmlink are returned unchanged.
    public func dataVolumePath(_ path: String) -> String {
        for mapping in mappings where path == mapping.visible || path.hasPrefix(mapping.visible + "/") {
            return Self.dataVolumePrefix + "/" + mapping.dataRelative + path.dropFirst(mapping.visible.count)
        }
        return path
    }

    /// Visible form of the real path: symlinks resolved, letter case normalized, firmlinks collapsed.
    /// `nil` if the path does not exist.
    public func canonical(_ path: String) -> String? {
        guard let real = RealPath.resolve(path) else { return nil }
        return visiblePath(real)
    }
}
