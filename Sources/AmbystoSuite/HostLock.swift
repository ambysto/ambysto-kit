import Darwin
import Foundation
import os

/// The host lock of one suite folder (spec "The host"): one dedicated thread blocks in `flock(LOCK_EX)` until this
/// process holds the lock, with no polling. The kernel releases it when the process quits or crashes.
public final class HostLock: Sendable {
    public let url: URL
    private let state = OSAllocatedUnfairLock(initialState: State())

    private struct State {
        var started = false
        var fd: Int32 = -1
        var cancelled = false
    }

    public init(url: URL) {
        self.url = url
    }

    public var isHeld: Bool { state.withLock { $0.fd >= 0 } }

    /// Starts waiting on a dedicated thread. `onAcquire` runs on that thread once the lock is held.
    public func start(onAcquire: @escaping @Sendable () -> Void) {
        let alreadyStarted = state.withLock { state -> Bool in
            defer { state.started = true }
            return state.started
        }
        guard !alreadyStarted else { return }
        let thread = Thread { [self] in
            guard let fd = waitForLock() else { return }
            let keep = state.withLock { state -> Bool in
                if state.cancelled { return false }
                state.fd = fd
                return true
            }
            if keep {
                onAcquire()
            } else {
                close(fd)   // released while waiting: pass the lock on at once
            }
        }
        thread.name = "AmbystoSuite.HostLock"
        thread.qualityOfService = .utility
        thread.start()
    }

    /// Lets go of the lock (closing the descriptor), or, while still waiting, gives it up as soon as it arrives.
    public func release() {
        let fd = state.withLock { state -> Int32 in
            state.cancelled = true
            defer { state.fd = -1 }
            return state.fd
        }
        if fd >= 0 { close(fd) }
    }

    /// Blocks until this process holds the lock on the file that is at `url` now. `nil` if the file cannot be opened.
    private func waitForLock() -> Int32? {
        while true {
            if state.withLock({ $0.cancelled }) { return nil }
            let fd = HostLock.openLockFile(url.path)
            guard fd >= 0 else { return nil }
            var result: Int32
            repeat { result = flock(fd, LOCK_EX) } while result != 0 && errno == EINTR
            guard result == 0 else {
                close(fd)
                return nil
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
