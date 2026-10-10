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
        #expect(usage.cpuTicks == 2)

        let file = ProcessReader.VnodeRef(path: "/tmp/x", identity: FileIdentity(device: 1, inode: 2))
        #expect(file.identity.inode == 2)

        let socket = ListeningSocket(pid: 7, fd: 3, socketID: 9, port: 3000, family: AF_INET, addressClass: .loopback, address: "127.0.0.1")
        #expect(socket.port == 3000)

        let process = ProcessUsage(identity: ProcessIdentity(pid: 7, startTime: 10), ppid: 1, uid: 501, name: "node", path: nil, footprint: 5)
        #expect(process.pid == 7 && process.cpuPercent == nil)
    }
}
