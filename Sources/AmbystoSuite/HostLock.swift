import Darwin
import Foundation
import os

/// The host lock (spec "The host"): one dedicated thread blocks in `flock(LOCK_EX)` on `host.lock` of every suite
/// folder, **in a fixed order (sorted paths)**, so two apps on different folders can never each hold one and wait
/// forever for the other. No polling. The kernel releases every lock when the process quits or crashes.
public final class HostLock: Sendable {
    public let urls: [URL]
    private let state = OSAllocatedUnfairLock(initialState: State())
    private static let log = Logger(subsystem: "com.ambysto.kit", category: "suite.lock")
    /// When a lock file cannot be opened (missing folder, a link planted at its path), try again after this long.
    static let retryDelay: TimeInterval = 5

    private struct State {
        var started = false
        var fds: [Int32] = []
        var held = false
        var cancelled = false
    }

    public convenience init(url: URL) {
        self.init(urls: [url])
    }

    public init(urls: [URL]) {
        self.urls = Array(Set(urls.map(\.standardizedFileURL))).sorted { $0.path < $1.path }
    }

    /// True once every lock is held.
    public var isHeld: Bool { state.withLock { $0.held } }

    /// Starts waiting on a dedicated thread. `onAcquire` runs on that thread once every lock is held.
    public func start(onAcquire: @escaping @Sendable () -> Void) {
        let alreadyStarted = state.withLock { state -> Bool in
            defer { state.started = true }
            return state.started
        }
        guard !alreadyStarted, !urls.isEmpty else { return }
        let thread = Thread { [self] in
            for url in urls {
                guard let fd = waitForLock(url) else { return }
                let keep = state.withLock { state -> Bool in
                    if state.cancelled { return false }
                    state.fds.append(fd)
                    return true
                }
                if !keep {
                    close(fd)   // released while waiting: pass the lock on at once
                    return
                }
            }
            let acquired = state.withLock { state -> Bool in
                if state.cancelled { return false }
                state.held = true
                return true
            }
            if acquired { onAcquire() }
        }
        thread.name = "AmbystoSuite.HostLock"
        thread.qualityOfService = .utility
        thread.start()
    }

    /// Lets go of every lock it holds (closing the descriptors); while still waiting, gives each up as soon as it arrives.
    public func release() {
        let fds = state.withLock { state -> [Int32] in
            state.cancelled = true
            state.held = false
            defer { state.fds = [] }
            return state.fds
        }
        fds.forEach { close($0) }
    }

    /// Blocks until this process holds the lock on the file that is at `url` now; `nil` once released.
    private func waitForLock(_ url: URL) -> Int32? {
        var loggedOpenFailure = false
        var loggedLockFailure = false
        while true {
            if state.withLock({ $0.cancelled }) { return nil }
            let fd = HostLock.openLockFile(url.path)
            guard fd >= 0 else {
                let code = errno
                if !loggedOpenFailure {
                    HostLock.log.error("cannot open \(url.path, privacy: .public): errno \(code); retrying every 5 s")
                    loggedOpenFailure = true
                }
                Thread.sleep(forTimeInterval: HostLock.retryDelay)
                continue
            }
            var result: Int32
            repeat { result = flock(fd, LOCK_EX) } while result != 0 && errno == EINTR
            guard result == 0 else {
                let code = errno
                close(fd)
                if !loggedLockFailure {
                    HostLock.log.error("cannot lock \(url.path, privacy: .public): errno \(code); retrying every 5 s")
                    loggedLockFailure = true
                }
                Thread.sleep(forTimeInterval: HostLock.retryDelay)
                continue
            }
            if HostLock.isSameFile(fd: fd, path: url.path) { return fd }
            close(fd)   // the file was replaced while we waited: lock the new one
        }
    }

    static func openLockFile(_ path: String) -> Int32 {
        open(path, O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0o600)
    }

    /// The locked descriptor still refers to the file at `path` (device + inode).
    static func isSameFile(fd: Int32, path: String) -> Bool {
        var held = stat()
        var current = stat()
        guard fstat(fd, &held) == 0, lstat(path, &current) == 0 else { return false }
        return held.st_dev == current.st_dev && held.st_ino == current.st_ino
    }
}
