import Darwin
import Foundation

/// Reads and writes glance records in one suite folder (spec "Reading the folder safely").
public struct GlanceStore: Sendable {
    public let folder: SuiteFolder

    public init(folder: SuiteFolder) {
        self.folder = folder
    }

    /// Validates and writes atomically (a temporary file, then a replace). Never deletes.
    public func write(_ record: GlanceRecord) throws {
        let data = try GlanceCodec.encode(record)
        try data.write(to: folder.glanceURL(for: record.app), options: .atomic)
    }

    /// Every catalog app's record that exists. Files are opened by catalog name only, so temporary files and
    /// anything else in `glances/` are never read.
    public func readAll() -> [SuiteApp: GlanceReadResult] {
        var results: [SuiteApp: GlanceReadResult] = [:]
        for app in SuiteApp.allCases {
            if let result = read(app) { results[app] = result }
        }
        return results
    }

    /// `nil` when the file does not exist.
    public func read(_ app: SuiteApp) -> GlanceReadResult? {
        switch GlanceStore.safeRead(path: folder.glanceURL(for: app).path, limit: GlanceRecord.maxBytes + 1) {
        case .missing: nil
        case .refused(let reason): .invalid(reason: reason)
        case .data(let data): GlanceCodec.decode(data, fileName: app.glanceFileName)
        }
    }

    enum SafeRead: Equatable {
        case missing
        case refused(String)
        case data(Data)
    }

    /// Opens without following links and without blocking; reads only a regular file of this user, at most `limit` bytes.
    static func safeRead(path: String, limit: Int) -> SafeRead {
        let fd = open(path, O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC)
        if fd < 0 {
            return errno == ENOENT ? .missing : .refused("unreadable (errno \(errno))")
        }
        defer { close(fd) }
        var info = stat()
        guard fstat(fd, &info) == 0 else { return .refused("unreadable") }
        guard (info.st_mode & S_IFMT) == S_IFREG else { return .refused("not a regular file") }
        guard info.st_uid == getuid() else { return .refused("not owned by this user") }

        var data = Data()
        var buffer = [UInt8](repeating: 0, count: 4096)
        while data.count < limit {
            let wanted = min(buffer.count, limit - data.count)
            let count = buffer.withUnsafeMutableBytes { Darwin.read(fd, $0.baseAddress, wanted) }
            if count < 0 {
                if errno == EINTR { continue }
                return .refused("unreadable")
            }
            if count == 0 { break }
            data.append(buffer, count: count)
        }
        return .data(data)
    }
}

/// Decides when a publisher writes: on a change of content, and as a heartbeat every `ttl / 2` seconds.
public struct PublishSchedule: Sendable {
    public private(set) var lastWritten: GlanceRecord?

    public init() {}

    public func isDue(_ record: GlanceRecord, now: Double) -> Bool {
        guard let last = lastWritten else { return true }
        if !last.sameContent(as: record) { return true }
        return now - last.at >= Double(record.ttl) / 2
    }

    public mutating func didWrite(_ record: GlanceRecord) {
        lastWritten = record
    }

    /// After a wake from sleep the next record is written at once.
    public mutating func forceNext() {
        lastWritten = nil
    }
}
