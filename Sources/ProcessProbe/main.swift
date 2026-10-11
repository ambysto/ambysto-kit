// Proof helper for scripts/compare-sockets.sh, scripts/compare-cpu.sh and scripts/energy-spike.sh. Prints JSON. Not used by any app.
import AmbystoProcess
import Foundation

let mode = CommandLine.arguments.dropFirst().first ?? "sockets"
let encoder = JSONEncoder()
encoder.outputFormatting = [.sortedKeys]
switch mode {
case "sockets":
    let me = getuid()
    let rows = ListeningSockets.read().filter { ProcessReader.bsdInfo($0.pid)?.uid == me }
        .map { ["pid": String($0.pid), "port": String($0.port), "address": $0.address, "class": $0.addressClass.rawValue] }
    FileHandle.standardOutput.write(try encoder.encode(rows))
case "cpu":
    let seconds = Double(CommandLine.arguments.dropFirst(2).first ?? "2") ?? 2
    var sampler = ProcessSampler()
    _ = sampler.sample()
    Thread.sleep(forTimeInterval: seconds)
    let rows = sampler.sample().processes.compactMap { p in p.cpuPercent.map { ["pid": String(p.pid), "cpu": String(format: "%.1f", $0)] } }
    FileHandle.standardOutput.write(try encoder.encode(rows))
case "energy":
    let seconds = max(Double(CommandLine.arguments.dropFirst(2).first ?? "10") ?? 10, 1)
    FileHandle.standardOutput.write(try energySpike(seconds: seconds))
default:
    FileHandle.standardError.write(Data("usage: ProcessProbe sockets | cpu [seconds] | energy [seconds]\n".utf8))
    exit(2)
}

/// Records the raw usage the sampler reads, so the spike can count counters that went down (the sampler hides them).
final class RecordingSource: ProcessSource, @unchecked Sendable {
    private let live = LiveProcessSource()
    private let lock = NSLock()
    private var usages: [pid_t: ProcessReader.Usage] = [:]
    private var starts: [pid_t: UInt64] = [:]

    func allPIDs() -> [pid_t] { live.allPIDs() }
    func bsdInfo(_ pid: pid_t) -> ProcessReader.BSDInfo? {
        let info = live.bsdInfo(pid)
        lock.lock(); starts[pid] = info?.startTime; lock.unlock()
        return info
    }
    func usage(_ pid: pid_t) -> ProcessReader.Usage? {
        let usage = live.usage(pid)
        lock.lock(); usages[pid] = usage; lock.unlock()
        return usage
    }
    func executablePath(_ pid: pid_t) -> String? { live.executablePath(pid) }

    /// The usage of every identity read since the last call.
    func take() -> [ProcessIdentity: ProcessReader.Usage] {
        lock.lock(); defer { lock.unlock() }
        var result: [ProcessIdentity: ProcessReader.Usage] = [:]
        for (pid, usage) in usages { if let start = starts[pid] { result[ProcessIdentity(pid: pid, startTime: start)] = usage } }
        usages = [:]; starts = [:]
        return result
    }
}

func sysctlString(_ name: String) -> String {
    var size = 0
    guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return "unknown" }
    var buffer = [CChar](repeating: 0, count: size)
    guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return "unknown" }
    return String(decoding: buffer.prefix(while: { $0 != 0 }).map { UInt8(bitPattern: $0) }, as: UTF8.self)
}

/// Two samples `seconds` apart, with the probe's own one-second busy loop inside the window. Prints what the
/// energy spike (scripts/energy-spike.sh) checks. Nothing leaves the machine.
func energySpike(seconds: Double) throws -> Data {
    var arm64: Int32 = 0
    var armSize = MemoryLayout<Int32>.size
    let appleSilicon = sysctlbyname("hw.optional.arm64", &arm64, &armSize, nil, 0) == 0 && arm64 == 1

    let source = RecordingSource()
    var sampler = ProcessSampler(source: source)
    _ = sampler.sample()
    let first = source.take()

    let ownBefore = ProcessReader.usage(getpid())
    let start = DispatchTime.now().uptimeNanoseconds
    var sink = 0.0
    while DispatchTime.now().uptimeNanoseconds - start < 1_000_000_000 { sink += sin(sink + 1) }
    let ownAfter = ProcessReader.usage(getpid())
    if sink.isNaN { exit(3) }   // keeps the loop from being optimised away
    if seconds > 1 { Thread.sleep(forTimeInterval: seconds - 1) }

    let (processes, hidden) = sampler.sample()
    let second = source.take()

    var down = ["cpuTicks": 0, "diskRead": 0, "diskWritten": 0, "idleWakeups": 0, "interruptWakeups": 0, "energy": 0]
    for (identity, before) in first {
        guard let after = second[identity] else { continue }
        if after.cpuTicks < before.cpuTicks { down["cpuTicks", default: 0] += 1 }
        if after.diskRead < before.diskRead { down["diskRead", default: 0] += 1 }
        if after.diskWritten < before.diskWritten { down["diskWritten", default: 0] += 1 }
        if after.idleWakeups < before.idleWakeups { down["idleWakeups", default: 0] += 1 }
        if after.interruptWakeups < before.interruptWakeups { down["interruptWakeups", default: 0] += 1 }
        if let a = after.energy, let b = before.energy, a < b { down["energy", default: 0] += 1 }
    }

    let deltas = processes.compactMap(\.deltas)
    var ownEnergy: Any = NSNull()
    if let a = ownAfter?.energy, let b = ownBefore?.energy, a >= b { ownEnergy = a - b }
    let report: [String: Any] = [
        "model": sysctlString("hw.model"),
        "architecture": appleSilicon ? "apple-silicon" : "intel",
        "v6Answered": ProcessReader.usage(getpid())?.energy != nil,
        "seconds": seconds,
        "readableProcesses": processes.count,
        "hiddenProcesses": hidden,
        "processesWithDeltas": deltas.count,
        "processesWithEnergy": deltas.filter { ($0.energy ?? 0) > 0 }.count,
        "energyDeltaSum": deltas.reduce(UInt64(0)) { $0 &+ ($1.energy ?? 0) },
        "idleWakeupsDeltaSum": deltas.reduce(UInt64(0)) { $0 &+ $1.idleWakeups },
        "interruptWakeupsDeltaSum": deltas.reduce(UInt64(0)) { $0 &+ $1.interruptWakeups },
        "ownBusyLoopEnergyDelta": ownEnergy,
        "countersWentDown": down,
    ]
    return try JSONSerialization.data(withJSONObject: report, options: [.sortedKeys])
}
