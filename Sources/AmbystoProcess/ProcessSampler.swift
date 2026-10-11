import Darwin
import Foundation

/// What changed between two samples of the same identity. Callers sum these over processes and time windows.
/// docs/spec/process.md → "Sampling".
public struct UsageDeltas: Sendable, Equatable {
    /// Nanoseconds between the two samples: a monotonic clock that does not advance while the Mac sleeps.
    public var interval: UInt64
    /// CPU time, ticks converted through `MachClock`.
    public var cpuNanoseconds: UInt64
    public var idleWakeups: UInt64
    public var interruptWakeups: UInt64
    /// `nil` when either sample lacked energy; 0 stays 0.
    public var energy: UInt64?
    public var diskRead: UInt64
    public var diskWritten: UInt64

    public init(interval: UInt64, cpuNanoseconds: UInt64, idleWakeups: UInt64, interruptWakeups: UInt64,
                energy: UInt64?, diskRead: UInt64, diskWritten: UInt64) {
        self.interval = interval
        self.cpuNanoseconds = cpuNanoseconds
        self.idleWakeups = idleWakeups
        self.interruptWakeups = interruptWakeups
        self.energy = energy
        self.diskRead = diskRead
        self.diskWritten = diskWritten
    }
}

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
    /// Deltas since the previous sample of this identity. `nil` on the first sample and, all or nothing, when
    /// any counter went down; `cpuPercent`, the disk rates and the wakeup rates follow the same rule.
    public var deltas: UsageDeltas?
    public var idleWakeupsPerSecond: Double?
    public var interruptWakeupsPerSecond: Double?
    /// Exited but not yet collected (`BSDInfo.isZombie`).
    public var isZombie: Bool

    public var pid: pid_t { identity.pid }

    public init(identity: ProcessIdentity, ppid: pid_t, uid: uid_t, name: String, path: String?, footprint: UInt64,
                cpuPercent: Double? = nil, diskReadPerSecond: Double? = nil, diskWritePerSecond: Double? = nil,
                deltas: UsageDeltas? = nil, idleWakeupsPerSecond: Double? = nil,
                interruptWakeupsPerSecond: Double? = nil, isZombie: Bool = false) {
        self.identity = identity
        self.ppid = ppid
        self.uid = uid
        self.name = name
        self.path = path
        self.footprint = footprint
        self.cpuPercent = cpuPercent
        self.diskReadPerSecond = diskReadPerSecond
        self.diskWritePerSecond = diskWritePerSecond
        self.deltas = deltas
        self.idleWakeupsPerSecond = idleWakeupsPerSecond
        self.interruptWakeupsPerSecond = interruptWakeupsPerSecond
        self.isZombie = isZombie
    }
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
        var idleWakeups: UInt64
        var interruptWakeups: UInt64
        var energy: UInt64?

        /// Every counter present in both samples is at least its previous value (energy only when both have it).
        func isNotBelow(_ before: Counters) -> Bool {
            if let latest = energy, let was = before.energy, latest < was { return false }
            return cpuTicks >= before.cpuTicks && diskRead >= before.diskRead && diskWritten >= before.diskWritten
                && idleWakeups >= before.idleWakeups && interruptWakeups >= before.interruptWakeups
        }
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
            let counters = Counters(cpuTicks: usage.cpuTicks, diskRead: usage.diskRead, diskWritten: usage.diskWritten,
                                    idleWakeups: usage.idleWakeups, interruptWakeups: usage.interruptWakeups,
                                    energy: usage.energy)
            current[identity] = counters

            // All or nothing: one counter going down (a reused identity, a reset) voids every value of this sample.
            var cpu: Double?, read: Double?, write: Double?, idle: Double?, interrupt: Double?
            var deltas: UsageDeltas?
            if let elapsed, elapsed > 0, let before = previous[identity], counters.isNotBelow(before) {
                let cpuNanoseconds = clock.nanoseconds(fromTicks: counters.cpuTicks - before.cpuTicks)
                cpu = Double(cpuNanoseconds) / elapsed * 100
                read = Double(counters.diskRead - before.diskRead) / elapsed * 1e9
                write = Double(counters.diskWritten - before.diskWritten) / elapsed * 1e9
                idle = Double(counters.idleWakeups - before.idleWakeups) / elapsed * 1e9
                interrupt = Double(counters.interruptWakeups - before.interruptWakeups) / elapsed * 1e9
                deltas = UsageDeltas(
                    interval: time - previousTime!, cpuNanoseconds: cpuNanoseconds,
                    idleWakeups: counters.idleWakeups - before.idleWakeups,
                    interruptWakeups: counters.interruptWakeups - before.interruptWakeups,
                    energy: counters.energy.flatMap { latest in before.energy.map { latest - $0 } },
                    diskRead: counters.diskRead - before.diskRead, diskWritten: counters.diskWritten - before.diskWritten)
            }
            result.append(ProcessUsage(identity: identity, ppid: bsd.ppid, uid: bsd.uid, name: bsd.name,
                                       path: source.executablePath(pid), footprint: usage.footprint,
                                       cpuPercent: cpu, diskReadPerSecond: read, diskWritePerSecond: write,
                                       deltas: deltas, idleWakeupsPerSecond: idle, interruptWakeupsPerSecond: interrupt,
                                       isZombie: bsd.isZombie))
        }
        previous = current
        previousTime = time
        return (result, hidden)
    }
}
