// Proof helper for scripts/compare-sockets.sh and scripts/compare-cpu.sh. Prints JSON. Not used by any app.
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
default:
    FileHandle.standardError.write(Data("usage: ProcessProbe sockets | cpu [seconds]\n".utf8))
    exit(2)
}
