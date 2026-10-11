import Darwin
import Foundation
import Testing
@testable import AmbystoProcess

@Suite("MachClock")
struct MachClockTests {
    @Test func convertsAppleSiliconTicks() {
        #expect(MachClock(numer: 125, denom: 3).nanoseconds(fromTicks: 24_000_000) == 1_000_000_000)
    }

    @Test func identityTimebase() {
        #expect(MachClock(numer: 1, denom: 1).nanoseconds(fromTicks: 12_345) == 12_345)
    }

    @Test func doesNotOverflowOnLargeValues() {
        let ticks = UInt64.max / 100
        #expect(MachClock(numer: 125, denom: 3).nanoseconds(fromTicks: ticks) > ticks)
    }
}

@Suite("ProcessReader")
struct ProcessReaderTests {
    @Test func parsesProcArgs() {
        var bytes: [UInt8] = [3, 0, 0, 0]
        bytes += Array("/bin/zsh".utf8) + [0, 0, 0]
        bytes += Array("zsh".utf8) + [0] + Array("-c".utf8) + [0] + Array("echo hi".utf8) + [0]
        #expect(ProcessReader.parseProcArgs(bytes[...]) == ["zsh", "-c", "echo hi"])
    }

    @Test func findsThisProcessWithItsPathAndDirectory() throws {
        let me = getpid()
        #expect(ProcessReader.allPIDs().contains(me))
        let info = try #require(ProcessReader.bsdInfo(me))
        #expect(info.uid == getuid())
        #expect(!info.isZombie)
        #expect(ProcessReader.executablePath(me) != nil)
        #expect(ProcessReader.currentDirectory(me) == RealPath(FileManager.default.currentDirectoryPath))
        var buffer: [UInt8] = []
        #expect(ProcessReader.arguments(me, buffer: &buffer)?.isEmpty == false)
        #expect(ProcessIdentity.current(of: me)?.startTime == info.startTime)
    }

    @Test func unreadableProcessesReturnNil() {
        // pid 1 (launchd) belongs to root.
        #expect(ProcessReader.openFiles(1) == nil)
        #expect(ProcessReader.usage(1) == nil || getuid() == 0)
        #expect(ListeningSockets.read(pid: 1).isEmpty)
    }

    private func RealPath(_ path: String) -> String? {
        guard let resolved = realpath(path, nil) else { return nil }
        defer { free(resolved) }
        return String(cString: resolved)
    }
}

@Suite("Listening sockets", .serialized)
struct ListeningSocketTests {
    /// Binds a listening socket in this process on port 0 and returns (fd, port).
    private func listen(family: Int32, address: String, v6only: Bool = true) throws -> (Int32, UInt16) {
        let fd = socket(family, SOCK_STREAM, 0)
        #expect(fd >= 0)
        var yes: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size))
        if family == AF_INET {
            var addr = sockaddr_in()
            addr.sin_family = sa_family_t(AF_INET)
            addr.sin_port = 0
            inet_pton(AF_INET, address, &addr.sin_addr)
            let ok = withUnsafePointer(to: &addr) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
            #expect(ok == 0)
        } else {
            var only: Int32 = v6only ? 1 : 0
            setsockopt(fd, IPPROTO_IPV6, IPV6_V6ONLY, &only, socklen_t(MemoryLayout<Int32>.size))
            var addr = sockaddr_in6()
            addr.sin6_family = sa_family_t(AF_INET6)
            addr.sin6_port = 0
            inet_pton(AF_INET6, address, &addr.sin6_addr)
            let ok = withUnsafePointer(to: &addr) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in6>.size)) } }
            #expect(ok == 0)
        }
        #expect(Darwin.listen(fd, 4) == 0)
        var bound = sockaddr_storage()
        var length = socklen_t(MemoryLayout<sockaddr_storage>.size)
        _ = withUnsafeMutablePointer(to: &bound) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { getsockname(fd, $0, &length) } }
        let port: UInt16 = withUnsafePointer(to: &bound) { pointer in
            family == AF_INET
                ? pointer.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { UInt16(bigEndian: $0.pointee.sin_port) }
                : pointer.withMemoryRebound(to: sockaddr_in6.self, capacity: 1) { UInt16(bigEndian: $0.pointee.sin6_port) }
        }
        return (fd, port)
    }

    @Test(arguments: [
        (AF_INET, "127.0.0.1", ListeningSocket.AddressClass.loopback),
        (AF_INET, "0.0.0.0", .any),
        (AF_INET6, "::1", .loopback),
        (AF_INET6, "::", .any),
    ])
    func findsAListenerWithItsPortAndAddressClass(family: Int32, address: String, expected: ListeningSocket.AddressClass) throws {
        let (fd, port) = try listen(family: family, address: address)
        defer { close(fd) }
        let mine = ListeningSockets.read(pid: getpid()).filter { $0.port == port }
        #expect(mine.count == 1, "\(address):\(port) → \(mine)")
        #expect(mine.first?.addressClass == expected)
        #expect(mine.first?.fd == fd)
        #expect(mine.first?.socketID != 0)
    }

    @Test func anIPv4MappedLoopbackIsLoopback() throws {
        let (fd, port) = try listen(family: AF_INET6, address: "::ffff:127.0.0.1", v6only: false)
        defer { close(fd) }
        let mine = ListeningSockets.read(pid: getpid()).filter { $0.port == port }
        #expect(mine.first?.addressClass == .loopback, "\(mine)")
    }

    @Test func aSocketSharedByTwoDescriptorsHasOneSocketID() throws {
        let (fd, port) = try listen(family: AF_INET, address: "127.0.0.1")
        let copy = dup(fd)
        defer { close(fd); close(copy) }
        let mine = ListeningSockets.read(pid: getpid()).filter { $0.port == port }
        #expect(mine.count == 2)
        #expect(Set(mine.map(\.socketID)).count == 1)
    }

    @Test func theAllProcessesPassIncludesThisProcess() throws {
        let (fd, port) = try listen(family: AF_INET, address: "127.0.0.1")
        defer { close(fd) }
        #expect(ListeningSockets.read().contains { $0.pid == getpid() && $0.port == port })
    }
}

/// A fake process table for the sampler.
final class FakeSource: ProcessSource, @unchecked Sendable {
    var processes: [pid_t: (start: UInt64, ticks: UInt64)] = [:]
    func allPIDs() -> [pid_t] { Array(processes.keys).sorted() }
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? {
        processes[pid].map { ProcessReader.BSDInfo(ppid: 1, uid: 501, startTime: $0.start, name: "p\(pid)", status: 2) }
    }
    func usage(_ pid: pid_t) -> ProcessReader.Usage? {
        processes[pid].map { ProcessReader.Usage(footprint: 1, cpuTicks: $0.ticks, diskRead: 0, diskWritten: 0) }
    }
    func executablePath(_ pid: pid_t) -> String? { "/bin/p\(pid)" }
}

final class FakeTime: @unchecked Sendable { var value: UInt64 = 1_000_000_000 }

@Suite("ProcessSampler", .serialized)
struct ProcessSamplerTests {
    @Test func computesPerCoreCPUFromDeltas() {
        let source = FakeSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: MachClock(numer: 1, denom: 1), now: { time.value })
        source.processes[10] = (start: 1, ticks: 0)
        #expect(sampler.sample().processes.first?.cpuPercent == nil)   // first sample: no delta yet
        source.processes[10] = (start: 1, ticks: 500_000_000)          // 0.5 s of CPU
        time.value += 1_000_000_000                                    // over 1 s
        #expect(sampler.sample().processes.first?.cpuPercent == 50)
    }

    @Test func aReusedPidNeverInheritsTheOldDelta() {
        let source = FakeSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: MachClock(numer: 1, denom: 1), now: { time.value })
        source.processes[10] = (start: 1, ticks: 9_000_000_000)
        _ = sampler.sample()
        source.processes[10] = (start: 2, ticks: 100)                  // same pid, new process
        time.value += 1_000_000_000
        #expect(sampler.sample().processes.first?.cpuPercent == nil)
    }

    @Test func aDecreasingCounterGivesNoValueInsteadOfAHugeOne() {
        let source = FakeSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: MachClock(numer: 1, denom: 1), now: { time.value })
        source.processes[10] = (start: 1, ticks: 5_000)
        _ = sampler.sample()
        source.processes[10] = (start: 1, ticks: 4_000)
        time.value += 1_000_000_000
        #expect(sampler.sample().processes.first?.cpuPercent == nil)
    }

    @Test func aBusyProcessShowsCPU() throws {
        let busy = Process()
        busy.executableURL = URL(filePath: "/usr/bin/yes")
        busy.standardOutput = FileHandle.nullDevice
        try busy.run()
        defer { busy.terminate(); busy.waitUntilExit() }
        var best = 0.0
        for _ in 0 ..< 2 {   // generous and retried once: the dev Mac may be loaded
            var sampler = ProcessSampler()
            _ = sampler.sample()
            Thread.sleep(forTimeInterval: 1)
            best = max(best, sampler.sample().processes.first { $0.pid == busy.processIdentifier }?.cpuPercent ?? 0)
            if best > 20 { break }
        }
        #expect(best > 20, "yes used \(best)%")
    }

    // MARK: Deltas, wakeups, energy (fake source, injected clock)

    private let unitClock = MachClock(numer: 1, denom: 1)

    private func usage(_ counter: Counter? = nil, drop: Bool = false, energy: UInt64? = 1_000) -> ProcessReader.Usage {
        // Baseline values; `drop` lowers the named counter by one.
        func value(_ base: UInt64, _ own: Counter) -> UInt64 { drop && counter == own ? base - 1 : base }
        return ProcessReader.Usage(footprint: 7, cpuTicks: value(1_000, .cpu), diskRead: value(2_000, .diskRead),
                                   diskWritten: value(3_000, .diskWritten), idleWakeups: value(40, .idleWakeups),
                                   interruptWakeups: value(50, .interruptWakeups), energy: energy.map { value($0, .energy) })
    }

    @Test func deltasIntervalAndRatesAreExact() throws {
        let source = FakeUsageSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: unitClock, now: { time.value })
        source.processes[10] = (start: 1, usage: usage())
        let first = try #require(sampler.sample().processes.first)
        #expect(first.deltas == nil && first.idleWakeupsPerSecond == nil && first.interruptWakeupsPerSecond == nil)

        source.processes[10] = (start: 1, usage: ProcessReader.Usage(
            footprint: 9, cpuTicks: 1_000 + 500_000_000, diskRead: 2_000 + 4_000, diskWritten: 3_000 + 8_000,
            idleWakeups: 40 + 30, interruptWakeups: 50 + 10, energy: 1_000 + 2_000_000))
        time.value += 2_000_000_000
        let second = try #require(sampler.sample().processes.first)
        #expect(second.deltas == UsageDeltas(interval: 2_000_000_000, cpuNanoseconds: 500_000_000, idleWakeups: 30,
                                             interruptWakeups: 10, energy: 2_000_000, diskRead: 4_000, diskWritten: 8_000))
        #expect(second.cpuPercent == 25)
        #expect(abs((second.idleWakeupsPerSecond ?? 0) - 15) < 1e-9 && abs((second.interruptWakeupsPerSecond ?? 0) - 5) < 1e-9)
        #expect(second.diskReadPerSecond == 2_000 && second.diskWritePerSecond == 4_000)
    }

    @Test func aReusedIdentityHasNoDeltasOrRates() throws {
        let source = FakeUsageSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: unitClock, now: { time.value })
        source.processes[10] = (start: 1, usage: usage())
        _ = sampler.sample()
        source.processes[10] = (start: 2, usage: usage())   // same pid, new process
        time.value += 1_000_000_000
        let reused = try #require(sampler.sample().processes.first)
        #expect(reused.deltas == nil && reused.cpuPercent == nil && reused.idleWakeupsPerSecond == nil)
    }

    enum Counter: CaseIterable, Sendable { case cpu, diskRead, diskWritten, idleWakeups, interruptWakeups, energy }

    @Test(arguments: Counter.allCases)
    private func oneDecreasingCounterVoidsEverythingForThatProcess(counter: Counter) throws {
        let source = FakeUsageSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: unitClock, now: { time.value })
        // The first sample is the baseline; the second equals it except one counter, 1 lower.
        source.processes[10] = (start: 1, usage: usage())
        _ = sampler.sample()
        source.processes[10] = (start: 1, usage: usage(counter, drop: true))
        time.value += 1_000_000_000
        let process = try #require(sampler.sample().processes.first)
        #expect(process.deltas == nil)
        #expect(process.cpuPercent == nil && process.diskReadPerSecond == nil && process.diskWritePerSecond == nil)
        #expect(process.idleWakeupsPerSecond == nil && process.interruptWakeupsPerSecond == nil)
    }

    @Test func energyZeroInBothSamplesIsZeroNotAbsent() throws {
        let source = FakeUsageSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: unitClock, now: { time.value })
        source.processes[10] = (start: 1, usage: usage(energy: 0))
        _ = sampler.sample()
        source.processes[10] = (start: 1, usage: usage(energy: 0))
        time.value += 1_000_000_000
        #expect(try #require(sampler.sample().processes.first).deltas?.energy == 0)
    }

    @Test(arguments: [(nil, 5), (5, nil), (nil, nil)] as [(UInt64?, UInt64?)])
    func missingEnergyInEitherSampleLeavesEnergyNilAndTheRestPresent(before: UInt64?, after: UInt64?) throws {
        let source = FakeUsageSource(), time = FakeTime()
        var sampler = ProcessSampler(source: source, clock: unitClock, now: { time.value })
        source.processes[10] = (start: 1, usage: usage(energy: before))
        _ = sampler.sample()
        var later = usage(energy: after)
        later.cpuTicks += 100
        later.idleWakeups += 3
        source.processes[10] = (start: 1, usage: later)
        time.value += 1_000_000_000
        let deltas = try #require(sampler.sample().processes.first?.deltas)
        #expect(deltas.energy == nil)
        #expect(deltas.cpuNanoseconds == 100 && deltas.idleWakeups == 3)
    }

    @Test func aZombieIsFlagged() throws {
        let source = FakeUsageSource()
        source.processes[10] = (start: 1, usage: usage())
        source.zombies = [10]
        var sampler = ProcessSampler(source: source, clock: unitClock)
        #expect(try #require(sampler.sample().processes.first).isZombie)
        source.zombies = []
        #expect(try #require(sampler.sample().processes.first).isZombie == false)
    }

    // MARK: Live

    @Test func ownThreadShowsCPUAndWakeupsAndTheIntervalIsBounded() throws {
        var sampler = ProcessSampler(source: OwnProcessSource(), clock: .current)
        let beforeFirst = DispatchTime.now().uptimeNanoseconds
        _ = sampler.sample()
        let firstDone = DispatchTime.now().uptimeNanoseconds
        runSleepySpinner()
        let secondStart = DispatchTime.now().uptimeNanoseconds
        let process = try #require(sampler.sample().processes.first { $0.pid == getpid() })
        let afterSecond = DispatchTime.now().uptimeNanoseconds

        let deltas = try #require(process.deltas)
        #expect(deltas.cpuNanoseconds > 0)
        #expect(deltas.idleWakeups + deltas.interruptWakeups > 0, "idle \(deltas.idleWakeups), interrupt \(deltas.interruptWakeups)")
        #expect(deltas.interval >= secondStart - firstDone)
        #expect(deltas.interval <= afterSecond - beforeFirst)
        #expect(process.isZombie == false)
    }

    @Test(.enabled(if: energyIsAvailable, "needs Apple Silicon and RUSAGE_INFO_V6"))
    func ownBusyLoopRaisesEnergy() throws {
        var sampler = ProcessSampler(source: OwnProcessSource(), clock: .current)
        _ = sampler.sample()
        let start = DispatchTime.now().uptimeNanoseconds
        var sink = 0.0
        while DispatchTime.now().uptimeNanoseconds - start < 500_000_000 { sink += sin(sink + 1) }
        #expect(sink.isFinite)
        let deltas = try #require(sampler.sample().processes.first { $0.pid == getpid() }?.deltas)
        #expect((deltas.energy ?? 0) > 0)
    }
}

/// A thread that sleeps 5 ms two hundred times with a short spin between sleeps; returns when it is done.
private func runSleepySpinner() {
    let done = DispatchSemaphore(value: 0)
    let thread = Thread {
        for _ in 0 ..< 200 {
            let spinStart = DispatchTime.now().uptimeNanoseconds
            var sink = 0.0
            while DispatchTime.now().uptimeNanoseconds - spinStart < 200_000 { sink += sin(sink + 1) }
            Thread.sleep(forTimeInterval: 0.005)
        }
        done.signal()
    }
    thread.start()
    done.wait()
}

private let energyIsAvailable: Bool = {
    var arm64: Int32 = 0
    var size = MemoryLayout<Int32>.size
    let isArm = sysctlbyname("hw.optional.arm64", &arm64, &size, nil, 0) == 0 && arm64 == 1
    return isArm && ProcessReader.usage(getpid())?.energy != nil
}()

/// The live source restricted to this process, so the assertions are about a process the test controls.
private struct OwnProcessSource: ProcessSource {
    private let live = LiveProcessSource()
    func allPIDs() -> [pid_t] { [getpid()] }
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? { live.bsdInfo(pid) }
    func usage(_ pid: pid_t) -> ProcessReader.Usage? { live.usage(pid) }
    func executablePath(_ pid: pid_t) -> String? { live.executablePath(pid) }
}

/// A fake process table with full usage values, for the deltas tests.
final class FakeUsageSource: ProcessSource, @unchecked Sendable {
    var processes: [pid_t: (start: UInt64, usage: ProcessReader.Usage)] = [:]
    var zombies: Set<pid_t> = []
    func allPIDs() -> [pid_t] { Array(processes.keys).sorted() }
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? {
        processes[pid].map {
            ProcessReader.BSDInfo(ppid: 1, uid: 501, startTime: $0.start, name: "p\(pid)",
                                  status: zombies.contains(pid) ? UInt32(SZOMB) : UInt32(SRUN))
        }
    }
    func usage(_ pid: pid_t) -> ProcessReader.Usage? { processes[pid]?.usage }
    func executablePath(_ pid: pid_t) -> String? { "/bin/p\(pid)" }
}

/// A pid reused between the two reads of one sample: the counters may belong to another process.
final class ReusedBetweenReadsSource: ProcessSource, @unchecked Sendable {
    var start: UInt64 = 100
    func allPIDs() -> [pid_t] { [42] }
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? {
        ProcessReader.BSDInfo(ppid: 1, uid: 501, startTime: start, name: "p", status: UInt32(SRUN))
    }
    func usage(_ pid: pid_t) -> ProcessReader.Usage? {
        start += 1   // the id now names a new process
        return ProcessReader.Usage(footprint: 1, cpuTicks: 10, diskRead: 0, diskWritten: 0)
    }
    func executablePath(_ pid: pid_t) -> String? { "/bin/p" }
}

@Suite("ProcessSampler, reads that belong together")
struct ProcessSamplerReadOrderTests {
    @Test func aPidReusedBetweenReadsIsSkipped() {
        var sampler = ProcessSampler(source: ReusedBetweenReadsSource())
        let (processes, hidden) = sampler.sample()
        #expect(processes.isEmpty)
        #expect(hidden == 1)
    }
}
