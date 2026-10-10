import Darwin
import Foundation

/// The AES-256 encrypted disk image that backs up the signing key and the ledger (plan §4).
enum BackupImage {
    static let hdiutil = "/usr/bin/hdiutil"
    static let keyFileName = "ambysto-signing-key.txt"

    /// The backup must be written to a local external drive: under /Volumes, on a different device than the Mac's
    /// own volumes, local (not a network or cloud mount), not internal, and not inside a git work tree.
    static func checkBackupPath(_ path: String) throws {
        let url = URL(filePath: path).standardizedFileURL
        guard url.path.hasPrefix("/Volumes/"), url.pathExtension == "dmg" else {
            throw ToolError("Write the backup to an external drive: --backup /Volumes/<drive>/ambysto-signing-key.dmg")
        }
        let parent = url.deletingLastPathComponent().path
        var parentInfo = stat(), rootInfo = stat(), dataInfo = stat()
        guard stat(parent, &parentInfo) == 0 else { throw ToolError("The folder \(parent) does not exist.") }
        stat("/", &rootInfo)
        stat("/System/Volumes/Data", &dataInfo)
        guard parentInfo.st_dev != rootInfo.st_dev, parentInfo.st_dev != dataInfo.st_dev else {
            throw ToolError("\(parent) is on this Mac's own disk; use an external drive.")
        }
        let volume = try? URL(filePath: parent).resourceValues(forKeys: [.volumeIsLocalKey, .volumeIsInternalKey])
        guard volume?.volumeIsLocal == true, volume?.volumeIsInternal == false else {
            throw ToolError("\(parent) is not a local external drive (network, cloud and internal volumes are refused).")
        }
        var current = URL(filePath: parent)
        while current.path != "/" {
            if FileManager.default.fileExists(atPath: current.appending(path: ".git").path) {
                throw ToolError("\(parent) is inside a git work tree; choose another folder.")
            }
            current = current.deletingLastPathComponent()
        }
        guard !FileManager.default.fileExists(atPath: url.path) else { throw ToolError("\(url.path) already exists; nothing was changed.") }
    }

    static func create(at path: String, passphrase: [UInt8], contents: [String: Data]) throws {
        try run([hdiutil, "create", "-quiet", "-encryption", "AES-256", "-stdinpass", "-size", "10m", "-fs", "APFS",
                 "-volname", "Ambysto Signing Key", path], stdin: passphrase)
        try withMounted(path, passphrase: passphrase, readOnly: false) { mount in
            for (name, data) in contents { try writePrivate(data, to: mount.appending(path: name)) }
        }
    }

    /// Attaches owner-only at a fresh 0700 mountpoint, runs `body`, and always detaches. Ctrl-C is ignored while mounted.
    static func withMounted<T>(_ path: String, passphrase: [UInt8], readOnly: Bool, _ body: (URL) throws -> T) throws -> T {
        var template = Array((FileManager.default.temporaryDirectory.path + "/ambysto-backup.XXXXXX").utf8CString)
        guard let created = mkdtemp(&template) else { throw ToolError("Could not create a private mount folder.") }
        let folder = URL(filePath: String(cString: created))
        let mountpoint = folder.appending(path: "mount")
        mkdir(mountpoint.path, 0o700)
        let signals = [SIGINT, SIGTERM, SIGHUP]
        let previous = signals.map { signal($0, SIG_IGN) }
        defer {
            if (try? run([hdiutil, "detach", "-quiet", "-force", mountpoint.path], stdin: nil)) == nil {
                note("Could not detach the backup at \(mountpoint.path). Eject it in Finder now.")
            }
            rmdir(mountpoint.path)
            rmdir(folder.path)
            for (sig, handler) in zip(signals, previous) { signal(sig, handler) }
        }
        var arguments = [hdiutil, "attach", "-quiet", "-stdinpass", "-nobrowse", "-owners", "on", "-mountpoint", mountpoint.path]
        if readOnly { arguments.append("-readonly") }
        arguments.append(path)
        do {
            try run(arguments, stdin: passphrase)
        } catch {
            throw ToolError("Could not open the backup (wrong passphrase?).")
        }
        return try body(mountpoint)
    }

    static func writePrivate(_ data: Data, to url: URL) throws {
        let fd = open(url.path, O_WRONLY | O_CREAT | O_TRUNC, 0o600)
        guard fd >= 0 else { throw ToolError("Could not write \(url.lastPathComponent) in the backup.") }
        defer { close(fd) }
        let written = data.withUnsafeBytes { write(fd, $0.baseAddress, data.count) }
        guard written == data.count, fcntl(fd, F_FULLFSYNC) == 0 else { throw ToolError("Could not write \(url.lastPathComponent) in the backup.") }
    }

    @discardableResult
    static func run(_ arguments: [String], stdin: [UInt8]?) throws -> Int32 {
        let result = try Spawn.run(arguments, stdin: stdin)
        guard result.status == 0 else { throw ToolError("\((arguments[0] as NSString).lastPathComponent) failed (\(result.status)).") }
        return result.status
    }
}
