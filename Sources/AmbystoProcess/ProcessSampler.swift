import Darwin
import Foundation

/// One readable process at one sample.
public struct ProcessUsage: Sendable, Equatable {
    public var identity: ProcessIdentity
    public var ppid: pid_t
    public var uid: uid_t
    public var name: String
    public var path: String?
    public var footprint: UInt64
    /// Per-core percent (one busy core = 100). `nil` until two samples of the same identity exist, and `nil`
    /// when a counter went down (never a wrapped, huge value).
    public var cpuPercent: Double?
    public var diskReadPerSecond: Double?
    public var diskWritePerSecond: Double?

    public var pid: pid_t { identity.pid }
}

/// Where the sampler reads from. Injectable so tests can feed reused pids and decreasing counters.
public protocol ProcessSource: Sendable {
    func allPIDs() -> [pid_t]
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo?
    func usage(_ pid: pid_t) -> ProcessReader.Usage?
    func executablePath(_ pid: pid_t) -> String?
}

public struct LiveProcessSource: ProcessSource {
    public init() {}
    public func allPIDs() -> [pid_t] { ProcessReader.allPIDs() }
    public func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? { ProcessReader.bsdInfo(pid) }
    public func usage(_ pid: pid_t) -> ProcessReader.Usage? { ProcessReader.usage(pid) }
    public func executablePath(_ pid: pid_t) -> String? { ProcessReader.executablePath(pid) }
}

/// CPU % and footprint from usage deltas keyed by identity, so a reused pid never corrupts a delta.
/// (From Ambysto Optimer, without its app grouping.) docs/spec/process.md → "Sampling".
public struct ProcessSampler: Sendable {
    private struct Counters: Sendable {
        var cpuTicks: UInt64
        var diskRead: UInt64
        var diskWritten: UInt64
    }

    private let source: any ProcessSource
    private let clock: MachClock
    private let now: @Sendable () -> UInt64
    private var previous: [ProcessIdentity: Counters] = [:]
    private var previousTime: UInt64?

    public init(source: any ProcessSource = LiveProcessSource(), clock: MachClock = .current,
                now: @escaping @Sendable () -> UInt64 = { DispatchTime.now().uptimeNanoseconds }) {
        self.source = source
        self.clock = clock
        self.now = now
    }

    /// Readable processes and the number that could not be read.
    public mutating func sample() -> (processes: [ProcessUsage], hidden: Int) {
        let time = now()
        let elapsed = previousTime.map { time > $0 ? Double(time - $0) : 0 }
        var current: [ProcessIdentity: Counters] = [:]
        var result: [ProcessUsage] = []
        var hidden = 0

        for pid in source.allPIDs() {
            guard let usage = source.usage(pid), let bsd = source.bsdInfo(pid) else {
                hidden += 1
                continue
            }
            let identity = ProcessIdentity(pid: pid, startTime: bsd.startTime)
            let counters = Counters(cpuTicks: usage.cpuTicks, diskRead: usage.diskRead, diskWritten: usage.diskWritten)
            current[identity] = counters

            var cpu: Double?, read: Double?, write: Double?
            if let elapsed, elapsed > 0, let before = previous[identity],
               counters.cpuTicks >= before.cpuTicks, counters.diskRead >= before.diskRead, counters.diskWritten >= before.diskWritten {
                cpu = Double(clock.nanoseconds(fromTicks: counters.cpuTicks - before.cpuTicks)) / elapsed * 100
                read = Double(counters.diskRead - before.diskRead) / elapsed * 1e9
                write = Double(counters.diskWritten - before.diskWritten) / elapsed * 1e9
            }
            result.append(ProcessUsage(identity: identity, ppid: bsd.ppid, uid: bsd.uid, name: bsd.name,
                                       path: source.executablePath(pid), footprint: usage.footprint,
                                       cpuPercent: cpu, diskReadPerSecond: read, diskWritePerSecond: write))
        }
        previous = current
        previousTime = time
        return (result, hidden)
    }
}
