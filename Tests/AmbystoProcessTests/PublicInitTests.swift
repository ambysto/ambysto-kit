import Darwin
import Testing
import AmbystoProcess   // deliberately not @testable: an app sees only the public surface

/// Apps build these records in their own tests (fake process tables), so every record needs a public initialiser.
/// This file fails to compile if one is lost.
@Suite("Public records")
struct PublicInitTests {
    @Test func appsCanBuildEveryProcessRecord() {
        let bsd = ProcessReader.BSDInfo(ppid: 1, uid: 501, startTime: 10, name: "node", status: UInt32(SZOMB))
        #expect(bsd.isZombie)

        let usage = ProcessReader.Usage(footprint: 1, cpuTicks: 2, diskRead: 3, diskWritten: 4)
        #expect(usage.cpuTicks == 2 && usage.idleWakeups == 0 && usage.interruptWakeups == 0 && usage.energy == nil)
        let full = ProcessReader.Usage(footprint: 1, cpuTicks: 2, diskRead: 3, diskWritten: 4,
                                       idleWakeups: 5, interruptWakeups: 6, energy: 7)
        #expect(full.idleWakeups == 5 && full.interruptWakeups == 6 && full.energy == 7)

        let deltas = UsageDeltas(interval: 1_000, cpuNanoseconds: 2, idleWakeups: 3, interruptWakeups: 4,
                                 energy: nil, diskRead: 5, diskWritten: 6)
        #expect(deltas.interval == 1_000 && deltas.energy == nil)

        let file = ProcessReader.VnodeRef(path: "/tmp/x", identity: FileIdentity(device: 1, inode: 2))
        #expect(file.identity.inode == 2)

        let socket = ListeningSocket(pid: 7, fd: 3, socketID: 9, port: 3000, family: AF_INET, addressClass: .loopback, address: "127.0.0.1")
        #expect(socket.port == 3000)

        let process = ProcessUsage(identity: ProcessIdentity(pid: 7, startTime: 10), ppid: 1, uid: 501, name: "node", path: nil, footprint: 5)
        #expect(process.pid == 7 && process.cpuPercent == nil)
        #expect(process.deltas == nil && process.idleWakeupsPerSecond == nil
                && process.interruptWakeupsPerSecond == nil && process.isZombie == false)

        let sampled = ProcessUsage(identity: ProcessIdentity(pid: 7, startTime: 10), ppid: 1, uid: 501, name: "node", path: nil,
                                   footprint: 5, cpuPercent: 1, diskReadPerSecond: 2, diskWritePerSecond: 3,
                                   deltas: deltas, idleWakeupsPerSecond: 4, interruptWakeupsPerSecond: 5, isZombie: true)
        #expect(sampled.deltas == deltas && sampled.idleWakeupsPerSecond == 4 && sampled.isZombie)
    }
}
