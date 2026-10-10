import Darwin
import Foundation

/// What makes a file "the same file": device + inode. Paths can be spelled many ways (firmlinks, case,
/// symlinked parents); identity cannot. docs/spec/process.md. (From Ambysto Space.)
public struct FileIdentity: Sendable, Hashable, Codable, CustomStringConvertible {
    public var device: UInt64
    public var inode: UInt64

    public init(device: UInt64, inode: UInt64) {
        self.device = device
        self.inode = inode
    }

    public init(_ info: stat) {
        self.init(device: UInt64(UInt32(bitPattern: info.st_dev)), inode: UInt64(info.st_ino))
    }

    /// Identity of the item itself (`lstat`: a symbolic link is not followed). `nil` if it does not exist.
    public static func of(_ path: String) -> FileIdentity? {
        var info = stat()
        guard lstat(path, &info) == 0 else { return nil }
        return FileIdentity(info)
    }

    public var description: String { "\(device):\(inode)" }
}
