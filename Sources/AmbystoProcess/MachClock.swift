import Darwin

/// Converts mach absolute-time ticks to nanoseconds. (From Ambysto Optimer.)
///
/// `proc_pid_rusage` reports CPU time in ticks. On Apple Silicon the timebase is 125/3,
/// so skipping this conversion under-reports CPU by ~41×.
public struct MachClock: Sendable, Equatable {
    public let numer: UInt64
    public let denom: UInt64

    public init(numer: UInt32, denom: UInt32) {
        self.numer = UInt64(numer)
        self.denom = UInt64(max(denom, 1))
    }

    public static let current: MachClock = {
        var info = mach_timebase_info_data_t()
        mach_timebase_info(&info)
        return MachClock(numer: info.numer, denom: info.denom)
    }()

    /// Overflow-safe `ticks * numer / denom`.
    public func nanoseconds(fromTicks ticks: UInt64) -> UInt64 {
        (ticks / denom) * numer + (ticks % denom) * numer / denom
    }
}
