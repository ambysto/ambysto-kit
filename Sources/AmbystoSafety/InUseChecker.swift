// From Ambysto Space (ambysto/ambysto-space@1ab11ea, macos/Sources/SpaceCore/Safety), merged into Ambysto Kit 0.2 (APS-11).
import AmbystoProcess
import Darwin
import Foundation

public struct ProcessUse: Sendable, Equatable, Codable {
    public var pid: Int32
    public var name: String

    public init(pid: Int32, name: String) {
        self.pid = pid
        self.name = name
    }
}

public struct InUseReport: Sendable, Equatable {
    public var users: [ProcessUse]
    /// Processes that could not be read (system processes). Information only; never blocks.
    public var uncheckedProcesses: Int

    public var isInUse: Bool { !users.isEmpty }

    public var reason: String {
        let names = users.prefix(3).map { "\($0.name) (pid \($0.pid))" }.joined(separator: ", ")
        return "In use by \(names)" + (users.count > 3 ? " and \(users.count - 3) more" : "")
    }
}

/// Files mapped into memory by every readable process. Expensive (≈ 0.8 s), so one snapshot per
/// deletion session, refreshed when older than `maxAge`.
public struct MappedFilesSnapshot: Sendable {
    public static let maxAge: TimeInterval = 10
    public let takenAt: Date
    let byProcess: [(use: ProcessUse, files: [ProcessReader.VnodeRef])]

    @_spi(Testing) public init(takenAt: Date, byProcess: [(use: ProcessUse, files: [ProcessReader.VnodeRef])]) {
        self.takenAt = takenAt
        self.byProcess = byProcess
    }

    public static func take(now: Date = Date(), excluding me: pid_t = getpid()) -> MappedFilesSnapshot {
        var entries: [(ProcessUse, [ProcessReader.VnodeRef])] = []
        for pid in ProcessReader.allPIDs() where pid != me {
            guard let files = ProcessReader.mappedFiles(pid), !files.isEmpty else { continue }
            entries.append((ProcessUse(pid: pid, name: ProcessReader.bsdInfo(pid)?.name ?? "pid \(pid)"), files))
        }
        return MappedFilesSnapshot(takenAt: now, byProcess: entries)
    }

    public static let empty = MappedFilesSnapshot(takenAt: .distantFuture, byProcess: [])

    public func isStale(now: Date = Date()) -> Bool { now.timeIntervalSince(takenAt) > Self.maxAge }
}

/// Native equivalent of the playbook's `pgrep -f <path>` + `lsof` checks. A path is in use when any readable
/// process has its executable, working directory or a command-line argument inside it (Optimer's rule), or
/// has a file inside it open or mapped into memory. Files are matched by identity as well as by path.
public enum InUseChecker {
    public static func check(
        realPath: String, identity: FileIdentity, isDirectory: Bool,
        mapped: MappedFilesSnapshot, canonicalizer: PathCanonicalizer = .shared
    ) -> InUseReport {
        let me = getpid()
        var users: [ProcessUse] = []
        var unchecked = 0
        var buffer = [UInt8](repeating: 0, count: ProcessReader.argumentsBufferSize)   // one buffer for every process
        for pid in ProcessReader.allPIDs() where pid != me {
            guard let open = ProcessReader.openFiles(pid) else { unchecked += 1; continue }
            let hit = open.contains { matches($0, realPath, identity, isDirectory, canonicalizer) }
                || usesByArguments(pid: pid, realPath: realPath, buffer: &buffer)
            if hit { users.append(ProcessUse(pid: pid, name: ProcessReader.bsdInfo(pid)?.name ?? "pid \(pid)")) }
        }
        for entry in mapped.byProcess where !users.contains(where: { $0.pid == entry.use.pid }) {
            if entry.files.contains(where: { matches($0, realPath, identity, isDirectory, canonicalizer) }) {
                users.append(entry.use)
            }
        }
        return InUseReport(users: users, uncheckedProcesses: unchecked)
    }

    /// One snapshot of every open file, for showing "Open by <process>" in lists.
    public struct OpenFiles: Sendable {
        public var byIdentity: [FileIdentity: [ProcessUse]] = [:]
        /// Visible paths, for matching files open inside a package (a running VM's disk inside its bundle).
        public var byPath: [(path: String, use: ProcessUse)] = []

        public init() {}

        public func users(identity: FileIdentity, path: String, isPackage: Bool) -> [ProcessUse] {
            var users = byIdentity[identity] ?? []
            if isPackage {
                for entry in byPath where RealPath.isInside(entry.path, path) && !users.contains(entry.use) { users.append(entry.use) }
            }
            return users
        }
    }

    public static func openFileSnapshot(excluding me: pid_t = getpid()) -> OpenFiles {
        var result = OpenFiles()
        for pid in ProcessReader.allPIDs() where pid != me {
            guard let open = ProcessReader.openFiles(pid) else { continue }
            let use = ProcessUse(pid: pid, name: ProcessReader.bsdInfo(pid)?.name ?? "pid \(pid)")
            for ref in open {
                if result.byIdentity[ref.identity]?.contains(use) != true { result.byIdentity[ref.identity, default: []].append(use) }
                if ref.path.hasPrefix("/") { result.byPath.append((PathCanonicalizer.shared.visiblePath(ref.path), use)) }
            }
        }
        return result
    }

    static func matches(
        _ ref: ProcessReader.VnodeRef, _ realPath: String, _ identity: FileIdentity, _ isDirectory: Bool,
        _ canonicalizer: PathCanonicalizer
    ) -> Bool {
        if ref.identity == identity { return true }
        guard isDirectory, ref.path.hasPrefix("/") else { return false }
        return RealPath.isInside(canonicalizer.visiblePath(ref.path), realPath)
    }

    static func usesByArguments(pid: pid_t, realPath: String, buffer: inout [UInt8]) -> Bool {
        if let path = ProcessReader.executablePath(pid), touches(path, realPath) { return true }
        let cwd = ProcessReader.currentDirectory(pid)
        if let cwd, touches(cwd, realPath) { return true }
        guard let args = ProcessReader.arguments(pid, buffer: &buffer) else { return false }
        return args.contains { argument in
            if argument.contains(realPath) || touches(argument, realPath) { return true }
            // Relative arguments such as `node_modules/.bin/vite` are resolved against the process's cwd.
            guard let cwd, !argument.hasPrefix("/"), !argument.hasPrefix("-"), argument.contains("/") || argument == "node_modules" else {
                return false
            }
            return touches((cwd as NSString).appendingPathComponent(argument), realPath)
        }
    }

    /// Compares the literal, the firmlink-collapsed and the symlink-resolved form (e.g. /tmp vs /private/tmp).
    private static func touches(_ candidate: String, _ realPath: String) -> Bool {
        guard candidate.hasPrefix("/") else { return false }
        let candidate = (candidate as NSString).standardizingPath
        if RealPath.isInside(candidate, realPath) { return true }
        if RealPath.isInside(PathCanonicalizer.shared.visiblePath(candidate), realPath) { return true }
        if let resolved = PathCanonicalizer.shared.canonical(candidate), RealPath.isInside(resolved, realPath) { return true }
        return false
    }
}
