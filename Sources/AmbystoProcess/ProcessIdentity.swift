import Darwin

/// The same process across samples: pid + start time (pids get reused; this pair does not).
/// It is the same **process**, not the same program: `exec` keeps both, so callers that act on a process
/// re-check its executable path right before acting. docs/spec/process.md.
public struct ProcessIdentity: Sendable, Hashable, Codable, CustomStringConvertible {
    public var pid: pid_t
    /// Microseconds since 1970.
    public var startTime: UInt64

    public init(pid: pid_t, startTime: UInt64) {
        self.pid = pid
        self.startTime = startTime
    }

    /// The identity a pid has right now, or `nil` if it is gone or unreadable.
    public static func current(of pid: pid_t) -> ProcessIdentity? {
        ProcessReader.bsdInfo(pid).map { ProcessIdentity(pid: pid, startTime: $0.startTime) }
    }

    public var description: String { "\(pid)@\(startTime)" }
}
