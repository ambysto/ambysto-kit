import Darwin
import Foundation
import Testing
import AmbystoProcess
@_spi(Testing) @testable import AmbystoSafety

@Suite("InUseChecker", .serialized)
struct InUseCheckerTests {
    private func check(_ url: URL, mapped: MappedFilesSnapshot = .empty) throws -> InUseReport {
        let real = try #require(PathCanonicalizer.shared.canonical(url.path))
        let s = try #require(FileStamp.of(real))
        return InUseChecker.check(realPath: real, identity: s.identity, isDirectory: s.isDirectory, mapped: mapped)
    }

    @Test func aFileHeldOpenIsReportedWithItsPid() throws {
        let home = try TempHome()
        let log = try home.file("Documents/live.log")
        let tail = try Helper("/usr/bin/tail", ["-f", log.path])
        #expect(tail.waitUntil { tail.hasOpen(log) })
        let report = try check(log)
        #expect(report.users.contains { $0.pid == tail.pid })
        #expect(report.reason.contains("pid \(tail.pid)"))
    }

    @Test func aFolderContainingAnOpenFileIsInUse() throws {
        let home = try TempHome()
        let log = try home.file("Documents/project/logs/live.log")
        let tail = try Helper("/usr/bin/tail", ["-f", log.path])
        #expect(tail.waitUntil { tail.hasOpen(log) })
        #expect(try check(home.url.appending(path: "Documents/project")).users.contains { $0.pid == tail.pid })
    }

    @Test func aProcessWorkingInsideAFolderMakesItInUse() throws {
        let home = try TempHome()
        let folder = try home.dir("Documents/workdir")
        let sleeper = try Helper("/bin/sleep", ["30"], cwd: folder)
        #expect(sleeper.waitUntil { sleeper.isWorking(in: folder) })
        #expect(try check(folder).users.contains { $0.pid == sleeper.pid })
    }

    @Test func aMappedFileIsInUse() throws {
        let home = try TempHome()
        let file = try home.file("Documents/mapped.bin", bytes: 16_384)
        let fd = open(file.path, O_RDONLY)
        #expect(fd >= 0)
        let region = mmap(nil, 16_384, PROT_READ, MAP_PRIVATE, fd, 0)
        close(fd)   // only the mapping keeps it in use now
        defer { munmap(region, 16_384) }
        let snapshot = MappedFilesSnapshot.take(excluding: -1)
        #expect(try check(file, mapped: snapshot).users.contains { $0.pid == getpid() })
        #expect(try check(file).users.isEmpty)   // without the snapshot, the open-file scan alone does not see it
    }

    @Test func anUnusedFileIsNotInUse() throws {
        let home = try TempHome()
        let file = try home.file("Documents/quiet.bin")
        let report = try check(file)
        #expect(!report.isInUse)
        #expect(report.uncheckedProcesses >= 0)
    }
}
