import AmbystoSuite
import Foundation
import os
import Testing
@testable import AmbystoUI

/// A clock the test moves by hand.
final class ClockBox: Sendable {
    private let value: OSAllocatedUnfairLock<Double>
    init(_ start: Double) { value = OSAllocatedUnfairLock(initialState: start) }
    func read() -> Double { value.withLock { $0 } }
    func advance(_ seconds: Double) { value.withLock { $0 += seconds } }
}

@MainActor
private func record(in folder: SuiteFolder, _ app: SuiteApp) -> GlanceRecord? {
    guard case .valid(let record)? = GlanceStore(folder: folder).read(app) else { return nil }
    return record
}

@MainActor
@Suite("SuiteParticipant flows")
struct ParticipantFlowTests {
    // MARK: Several folders

    @Test func oneParticipantHostsOverTwoFoldersAndWritesToBoth() async throws {
        let one = try makeTempFolder(), two = try makeTempFolder()
        defer {
            try? FileManager.default.removeItem(at: one.url)
            try? FileManager.default.removeItem(at: two.url)
        }
        let a = Rig(.optimer, folder: one, folders: [one, two])
        let b = Rig(.devbox, folder: one)   // reads and publishes to one folder only
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]

        #expect(await settle(refreshing: both) { a.participant.isActive && b.participant.isActive && a.participant.isHost })
        #expect(a.participant.lock?.urls.count == 2)
        #expect(!b.participant.isHost)   // b waits on the lock in `one`, which a holds
        #expect(record(in: one, .optimer) != nil)
        #expect(record(in: two, .optimer) != nil)
        #expect(b.participant.rows.contains { $0.app == .optimer })
        #expect(record(in: two, .devbox) == nil)
    }

    @Test func theNewestRecordAmongFoldersWins() async throws {
        let one = try makeTempFolder(), two = try makeTempFolder()
        defer {
            try? FileManager.default.removeItem(at: one.url)
            try? FileManager.default.removeItem(at: two.url)
        }
        func reader(older: SuiteFolder, newer: SuiteFolder) throws -> GlanceRecord? {
            try GlanceStore(folder: older).write(GlanceRecord(app: .devbox, at: 999, metrics: [GlanceMetric(id: "m", label: "Old", value: 1)]))
            try GlanceStore(folder: newer).write(GlanceRecord(app: .devbox, at: 1000, metrics: [GlanceMetric(id: "m", label: "New", value: 2)]))
            let rig = Rig(.power, folder: one, folders: [one, two], clock: { 1000 })
            defer { rig.finish() }
            for case .running(let record) in rig.participant.rows where record.app == .devbox { return record }
            return nil
        }
        #expect(try reader(older: one, newer: two)?.at == 1000)
        #expect(try reader(older: two, newer: one)?.at == 1000)
    }

    // MARK: Heartbeat

    @Test func theTickIsOftenEnoughForEveryAllowedTtl() {
        for ttl in 3...60 {
            let interval = SuiteParticipant.tickInterval(ttl: ttl)
            let tolerance = SuiteParticipant.tickTolerance(ttl: ttl)
            #expect(interval <= 5)
            #expect(tolerance <= interval * 0.1 + 1e-9)
            #expect(interval + tolerance <= Double(ttl) / 2, "ttl \(ttl)")
        }
        #expect(SuiteParticipant.tickInterval(ttl: 3) == 1)
    }

    @Test func heartbeatsAreNeverFurtherApartThanHalfTheTtl() async throws {
        for ttl in [3, 4, 5, 7, 10, 30, 60] {
            let folder = try makeTempFolder()
            defer { try? FileManager.default.removeItem(at: folder.url) }
            let clock = ClockBox(5_000)
            let rig = Rig(.optimer, folder: folder, clock: clock.read)
            defer { rig.finish() }
            rig.participant.publish(metrics: [GlanceMetric(id: "m", label: "L", value: 1)], ttl: ttl)

            // The timer is re-armed for this ttl.
            let timer = try #require(rig.participant.timer)
            #expect(timer.timeInterval == SuiteParticipant.tickInterval(ttl: ttl))
            #expect(timer.tolerance <= timer.timeInterval * 0.1 + 1e-9)

            // Ticks arrive as late as the tolerance allows; unchanged metrics, so only the heartbeat writes.
            let spacing = timer.timeInterval + timer.tolerance
            var writes: [Double] = []
            let start = clock.read()
            while clock.read() - start < Double(ttl) * 2.5 {   // below the 3 × ttl expiry point (APS-15)
                if let at = record(in: folder, .optimer)?.at, writes.last != at { writes.append(at) }
                clock.advance(spacing)
                rig.participant.refresh(tick: true)
            }
            #expect(writes.count >= 3, "ttl \(ttl)")
            for (earlier, later) in zip(writes, writes.dropFirst()) {
                #expect(later - earlier <= Double(ttl) / 2 + 1e-9, "ttl \(ttl): \(later - earlier)")
            }
        }
    }

    // MARK: Own icon

    @Test func aSecondAppDoesNotFlashItsIconBeforeItsFirstWrite() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        let name = "ambysto-ui-test-\(UUID().uuidString)"
        defer { UserDefaults(suiteName: name)?.removePersistentDomain(forName: name) }
        let b = SuiteParticipant(app: .devbox, folders: [folder], defaults: UserDefaults(suiteName: name)!,
                                 statusItems: SuiteStatusItemFactory { _ in FakeStatusItem() }, observeSystem: false, onOpen: {})
        defer { b.shutdown() }
        // No publish yet: b is running, so it counts itself.
        #expect(b.isActive)
        #expect(!b.showsOwnIcon)
        #expect(record(in: folder, .devbox) == nil)
    }

    // MARK: Hosting

    @Test func anItemThatIsNotVisibleIsNeverPublishedAsHosting() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder, visible: false), b = Rig(.devbox, folder: folder, visible: false)
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]
        #expect(await settle(refreshing: both) { a.participant.isActive && b.participant.isActive })
        let hostElect = (a.log.items + b.log.items).count
        #expect(hostElect == 1)   // one item was made, by the host-elect; it reports invisible
        #expect(!a.participant.isHost && !b.participant.isHost)
        #expect(record(in: folder, .optimer)?.hosting == false)
        #expect(record(in: folder, .devbox)?.hosting == false)
    }

    @Test func withoutAVisibleHostBothShowTheirOwnIconAfterFiveSeconds() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let clock = ClockBox(1_000)
        let a = Rig(.optimer, folder: folder, visible: false, clock: clock.read)
        let b = Rig(.devbox, folder: folder, visible: false, clock: clock.read)
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]
        #expect(await settle(refreshing: both) { both.allSatisfy { $0.isActive } })
        #expect(both.allSatisfy { !$0.showsOwnIcon })

        clock.advance(3)
        both.forEach { $0.refresh() }
        #expect(both.allSatisfy { !$0.showsOwnIcon })   // inside the 5 s grace
        clock.advance(3)
        both.forEach { $0.refresh() }
        #expect(both.allSatisfy { $0.isActive })
        #expect(both.allSatisfy { $0.showsOwnIcon })
    }

    // MARK: Clean quit

    @Test func quitRemovesTheItemThenPublishesStoppedThenReleasesTheLock() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder), b = Rig(.devbox, folder: folder)
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]
        #expect(await settle(refreshing: both) { both.allSatisfy { $0.isActive } && both.filter(\.isHost).count == 1 })

        let hostRig = a.participant.isHost ? a : b
        let app = hostRig.participant.app
        let item = try #require(hostRig.log.items.last)
        var atWithdraw: (stopped: Bool, held: Bool)?
        item.onWithdraw = {
            atWithdraw = (record(in: folder, app)?.state == .stopped, hostRig.participant.lock?.isHeld == true)
        }
        #expect(hostRig.participant.lock?.isHeld == true)

        hostRig.participant.shutdown()

        let seen = try #require(atWithdraw)
        #expect(!seen.stopped)   // the item goes first: `stopped` is not on disk yet
        #expect(seen.held)       // and the lock is still held
        #expect(record(in: folder, app)?.state == .stopped)
        #expect(hostRig.participant.lock?.isHeld == false)
        #expect(item.withdrawn)
    }
}
