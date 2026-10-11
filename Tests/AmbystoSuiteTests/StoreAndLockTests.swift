import Darwin
import Foundation
import Testing
@testable import AmbystoSuite

/// A suite folder in a fresh temporary directory. Tests delete only their own temporary directories.
func temporarySuiteFolder() throws -> SuiteFolder {
    let url = FileManager.default.temporaryDirectory.appendingPathComponent("ambysto-suite-\(UUID().uuidString)")
    let folder = SuiteFolder(url: url)
    try folder.prepare()
    return folder
}

func waitUntil(seconds: Double = 3, _ condition: () -> Bool) async -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    while Date() < deadline {
        if condition() { return true }
        try? await Task.sleep(for: .milliseconds(20))
    }
    return condition()
}

@Suite("Glance store")
struct GlanceStoreTests {
    @Test func writesAndReadsBack() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let store = GlanceStore(folder: folder)
        let record = GlanceRecord(app: .devbox, at: 5, metrics: [GlanceMetric(id: "servers", label: "Servers", value: 3)])
        try store.write(record)
        #expect(store.readAll() == [.devbox: .valid(record)])
    }

    @Test func folderIsOwnerOnly() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let attributes = try FileManager.default.attributesOfItem(atPath: folder.glancesURL.path)
        #expect((attributes[.posixPermissions] as? Int) == 0o700)
    }

    @Test func skipsAFifoWithoutBlocking() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        #expect(mkfifo(folder.glanceURL(for: .power).path, 0o600) == 0)
        let started = Date()
        #expect(GlanceStore(folder: folder).read(.power) == .invalid(reason: "not a regular file"))
        #expect(Date().timeIntervalSince(started) < 1)
    }

    @Test func doesNotFollowASymlink() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let target = folder.url.appendingPathComponent("elsewhere.json")
        try Data(#"{"contract":1}"#.utf8).write(to: target)
        try FileManager.default.createSymbolicLink(at: folder.glanceURL(for: .guard), withDestinationURL: target)
        guard case .invalid = GlanceStore(folder: folder).read(.guard) else {
            Issue.record("a symlink was followed")
            return
        }
    }

    @Test func readsAtMostTheLimit() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        try Data(repeating: 0x20, count: 1_000_000).write(to: folder.glanceURL(for: .optimer))
        guard case .data(let data) = GlanceStore.safeRead(path: folder.glanceURL(for: .optimer).path, limit: 2049) else {
            Issue.record("expected data")
            return
        }
        #expect(data.count == 2049)
        #expect(GlanceStore(folder: folder).read(.optimer) == .invalid(reason: "size: at most 2,048 bytes"))
    }

    @Test func ignoresFilesThatAreNotCatalogNames() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        try Data("junk".utf8).write(to: folder.glancesURL.appendingPathComponent(".optimer.json.tmp"))
        try Data("junk".utf8).write(to: folder.glancesURL.appendingPathComponent("clip.json"))
        #expect(GlanceStore(folder: folder).readAll().isEmpty)
    }

    @Test func publishScheduleWritesOnChangeAndHeartbeat() {
        var schedule = PublishSchedule()
        let first = GlanceRecord(app: .optimer, at: 100, ttl: 10, metrics: [GlanceMetric(id: "m", label: "M", value: 1)])
        #expect(schedule.isDue(first, now: 100))
        schedule.didWrite(first)
        var same = first
        same.at = 104
        #expect(!schedule.isDue(same, now: 104))
        same.at = 105
        #expect(schedule.isDue(same, now: 105))   // ttl / 2
        var changed = first
        changed.at = 101
        changed.metrics[0].value = .number(2)
        #expect(schedule.isDue(changed, now: 101))
        schedule.forceNext()
        #expect(schedule.isDue(same, now: 101))
    }
}

@Suite("Host lock", .serialized)
struct HostLockTests {
    @Test func secondWaiterGetsTheLockWhenTheFirstLetsGo() async throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let first = HostLock(url: folder.lockURL)
        let second = HostLock(url: folder.lockURL)
        first.start {}
        #expect(await waitUntil { first.isHeld })
        second.start {}
        try await Task.sleep(for: .milliseconds(200))
        #expect(!second.isHeld)
        first.release()
        #expect(await waitUntil(seconds: 1) { second.isHeld })
        second.release()
    }

    @Test func aWaiterReleasedEarlyPassesTheLockOn() async throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let first = HostLock(url: folder.lockURL)
        let cancelled = HostLock(url: folder.lockURL)
        let third = HostLock(url: folder.lockURL)
        first.start {}
        #expect(await waitUntil { first.isHeld })
        cancelled.start {}
        try await Task.sleep(for: .milliseconds(100))
        cancelled.release()
        third.start {}
        try await Task.sleep(for: .milliseconds(100))
        first.release()
        #expect(await waitUntil(seconds: 2) { third.isHeld })
        #expect(!cancelled.isHeld)
        third.release()
    }

    @Test func aReplacedLockFileIsLockedAgain() async throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let first = HostLock(url: folder.lockURL)
        first.start {}
        #expect(await waitUntil { first.isHeld })
        let second = HostLock(url: folder.lockURL)
        second.start {}
        try await Task.sleep(for: .milliseconds(100))   // second now waits on the old file
        try Data().write(to: folder.lockURL, options: .atomic)   // the path now names a new file
        first.release()
        #expect(await waitUntil(seconds: 2) { second.isHeld })
        // The lock second holds is on the file at the path now: a non-blocking try on it fails.
        let probe = HostLock.openLockFile(folder.lockURL.path)
        defer { close(probe) }
        #expect(flock(probe, LOCK_EX | LOCK_NB) != 0)
        second.release()
    }

    @Test func descriptorIsNotInherited() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let fd = HostLock.openLockFile(folder.lockURL.path)
        defer { close(fd) }
        #expect(fd >= 0)
        #expect(fcntl(fd, F_GETFD) & FD_CLOEXEC != 0)
    }

    @Test func doesNotFollowASymlinkedLockFile() throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        try FileManager.default.createSymbolicLink(at: folder.lockURL, withDestinationURL: folder.url.appendingPathComponent("x"))
        #expect(HostLock.openLockFile(folder.lockURL.path) < 0)
    }
}

@Suite("Host lock, several folders", .serialized)
struct HostLockFoldersTests {
    @Test func locksAreTakenInOneOrderSoNobodyDeadlocks() async throws {
        let a = try temporarySuiteFolder()
        let b = try temporarySuiteFolder()
        defer {
            try? FileManager.default.removeItem(at: a.url)
            try? FileManager.default.removeItem(at: b.url)
        }
        // Given in opposite orders: both sort to the same order, so one holds both and the other waits.
        let first = HostLock(urls: [a.lockURL, b.lockURL])
        let second = HostLock(urls: [b.lockURL, a.lockURL])
        #expect(first.urls == second.urls)
        first.start {}
        second.start {}
        #expect(await waitUntil { first.isHeld || second.isHeld })
        try await Task.sleep(for: .milliseconds(200))
        #expect(first.isHeld != second.isHeld)
        let (holder, waiter) = first.isHeld ? (first, second) : (second, first)
        holder.release()
        #expect(await waitUntil(seconds: 2) { waiter.isHeld })
        waiter.release()
    }

    @Test func noDeadlockWhenTheLocksArePreHeldByDifferentApps() async throws {
        let a = try temporarySuiteFolder()
        let b = try temporarySuiteFolder()
        defer {
            try? FileManager.default.removeItem(at: a.url)
            try? FileManager.default.removeItem(at: b.url)
        }
        let sorted = [a.lockURL, b.lockURL].sorted { $0.path < $1.path }
        let holdsFirst = HostLock(url: sorted[0])   // two older apps, one per folder
        let holdsSecond = HostLock(url: sorted[1])
        holdsFirst.start {}
        holdsSecond.start {}
        #expect(await waitUntil { holdsFirst.isHeld && holdsSecond.isHeld })
        let p = HostLock(urls: [b.lockURL, a.lockURL])
        let q = HostLock(urls: [a.lockURL, b.lockURL])
        p.start {}
        q.start {}
        holdsFirst.release()   // whoever gets the first lock then waits for the second; the other waits for the first
        try await Task.sleep(for: .milliseconds(200))
        #expect(!p.isHeld && !q.isHeld)
        holdsSecond.release()
        #expect(await waitUntil(seconds: 2) { p.isHeld || q.isHeld })
        #expect(p.isHeld != q.isHeld)
        let (winner, other) = p.isHeld ? (p, q) : (q, p)
        winner.release()
        #expect(await waitUntil(seconds: 2) { other.isHeld })
        other.release()
    }

    @Test func anUnopenableLockFileIsRetriedNotAbandoned() async throws {
        let folder = try temporarySuiteFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        try FileManager.default.createSymbolicLink(at: folder.lockURL, withDestinationURL: folder.url.appendingPathComponent("x"))
        let lock = HostLock(url: folder.lockURL)
        lock.start {}
        try await Task.sleep(for: .milliseconds(200))
        #expect(!lock.isHeld)
        lock.release()
    }
}

@Suite("Glance numbers")
struct GlanceNumberTests {
    @Test func valuesAreWrittenRoundedToTwoDecimals() throws {
        for (value, written) in [(0.1 + 0.2, "0.3"), (0.07, "0.07"), (12.25, "12.25"), (61.5, "61.5")] {
            let record = GlanceRecord(app: .optimer, at: 1, metrics: [GlanceMetric(id: "v", label: "V", value: value)])
            let text = String(decoding: try GlanceCodec.encode(record), as: UTF8.self)
            #expect(text.contains(#""value":\#(written)}"#), "\(text)")
        }
    }
}
