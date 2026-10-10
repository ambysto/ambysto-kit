// From Ambysto Space (ambysto/ambysto-space@1ab11ea, macos/Sources/SpaceCore/Safety), merged into Ambysto Kit 0.2 (APS-11).
import AmbystoProcess
import Darwin
import Foundation

/// Identity plus what changes when a file changes. Recorded at scan time; checked again right before a delete.
public struct FileStamp: Sendable, Hashable, Codable {
    public var identity: FileIdentity
    /// Logical length (`st_size`).
    public var size: UInt64
    /// Modification time in nanoseconds since 1970 (signed: files dated 1970 and earlier exist).
    public var modifiedNanos: Int64
    public var isDirectory: Bool

    public init(identity: FileIdentity, size: UInt64, modifiedNanos: Int64, isDirectory: Bool) {
        self.identity = identity
        self.size = size
        self.modifiedNanos = modifiedNanos
        self.isDirectory = isDirectory
    }

    public static func of(_ path: String) -> FileStamp? {
        var info = stat()
        guard lstat(path, &info) == 0 else { return nil }
        return FileStamp(info)
    }

    public init(_ info: stat) {
        self.init(
            identity: FileIdentity(info),
            size: UInt64(max(info.st_size, 0)),
            modifiedNanos: Int64(info.st_mtimespec.tv_sec) &* 1_000_000_000 &+ Int64(info.st_mtimespec.tv_nsec),
            isDirectory: (info.st_mode & S_IFMT) == S_IFDIR
        )
    }

    /// Files must keep identity, size and modification time. Folders must keep identity and modification
    /// time (it changes when an entry is added or removed directly inside).
    public func isUnchanged(comparedTo current: FileStamp) -> Bool {
        guard identity == current.identity, isDirectory == current.isDirectory, modifiedNanos == current.modifiedNanos else { return false }
        return isDirectory || size == current.size
    }
}
