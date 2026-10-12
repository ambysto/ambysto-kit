import AmbystoSuite
import Foundation
import Testing
@testable import AmbystoUI

// APS-15: numbers the app stopped updating expire; the heartbeat keeps the row without them.

private func written(_ folder: SuiteFolder) -> GlanceRecord? {
    guard case .valid(let record)? = GlanceStore(folder: folder).read(.optimer) else { return nil }
    return record
}

private let memory = [GlanceMetric(id: "mem", label: "Memory", value: 62, unit: "%", level: .ok)]

@MainActor
@Suite("Expired numbers", .serialized)
struct ExpiredNumbersTests {
    /// Advances the clock tick by tick, refreshing like the timer does.
    private func tick(_ rig: Rig, _ clock: ClockBox, for seconds: Double, step: Double = 1) {
        var elapsed = 0.0
        while elapsed < seconds {
            clock.advance(step)
            elapsed += step
            rig.participant.refresh(tick: true)
        }
    }

    @Test func numbersExpireAfterThreeTtlsByDefaultAndComeBackOnTheNextPublish() throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let clock = ClockBox(10_000)
        let rig = Rig(.optimer, folder: folder, clock: clock.read)
        defer { rig.finish() }
        rig.participant.publish(level: .notice, metrics: memory, ttl: 10)
        tick(rig, clock, for: 29)
        #expect(written(folder)?.metrics == memory)
        tick(rig, clock, for: 3)   // past 3 × ttl = 30 s
        let expired = try #require(written(folder))
        #expect(expired.metrics.isEmpty)
        #expect(expired.level == .notice)   // the level stays
        #expect(expired.isFresh(now: clock.read()))   // the row stays
        rig.participant.publish(level: .notice, metrics: memory, ttl: 10)
        #expect(written(folder)?.metrics == memory)
    }

    @Test func anExplicitStaleAfterWinsAndIsNeverBelowTheTtl() throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let rig = Rig(.optimer, folder: folder)
        defer { rig.finish() }
        rig.participant.staleAfter = 150
        #expect(rig.participant.effectiveStaleAfter(ttl: 30) == 150)
        rig.participant.staleAfter = 2
        #expect(rig.participant.effectiveStaleAfter(ttl: 10) == 10)
        rig.participant.staleAfter = nil
        #expect(rig.participant.effectiveStaleAfter(ttl: 10) == 30)
    }

    @Test func aRefusedPublishNeverBecomesTheSourceOfARecord() throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let clock = ClockBox(20_000)
        let rig = Rig(.optimer, folder: folder, clock: clock.read)
        defer { rig.finish() }
        rig.participant.publish(level: .ok, metrics: memory, ttl: 10)
        rig.participant.publish(level: .alert, metrics: [GlanceMetric(id: "Bad Id", label: "x", value: 1)], ttl: 10)
        tick(rig, clock, for: 35)   // past the expiry of the valid publish
        let record = try #require(written(folder))
        #expect(record.metrics.isEmpty)
        #expect(record.level == .ok)   // the refused publish's level never reached the file
    }

    @Test func aLongSleepWritesNoNumbersUntilTheAppPublishes() throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let clock = ClockBox(30_000)
        let rig = Rig(.optimer, folder: folder, clock: clock.read)
        defer { rig.finish() }
        rig.participant.publish(metrics: memory, ttl: 10)
        clock.advance(600)   // asleep
        rig.participant.refresh(tick: true)   // the first heartbeat on wake
        #expect(written(folder)?.metrics.isEmpty == true)
        rig.participant.publish(metrics: memory, ttl: 10)
        #expect(written(folder)?.metrics == memory)
    }

    @Test func reopenOpensTheMainWindowOnlyWhileTheOwnIconIsHidden() throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        var opened = 0
        let defaultsName = "ambysto-ui-test-\(UUID().uuidString)"
        defer { UserDefaults(suiteName: defaultsName)?.removePersistentDomain(forName: defaultsName) }
        let alone = SuiteParticipant(app: .optimer, folders: [folder], defaults: UserDefaults(suiteName: defaultsName)!,
                                     statusItems: SuiteStatusItemFactory { _ in FakeStatusItem(visible: true) },
                                     observeSystem: false, onOpen: { opened += 1 })
        defer { alone.shutdown() }
        alone.publish(metrics: memory)
        #expect(alone.showsOwnIcon)
        #expect(alone.handleReopen() == true)   // the system's default reopen
        #expect(opened == 0)
        let other = Rig(.devbox, folder: folder)
        defer { other.finish() }
        #expect(try settleSync { alone.refresh(); return !alone.showsOwnIcon })
        #expect(alone.handleReopen() == false)   // handled: the app's main window
        #expect(opened == 1)
    }

    private func settleSync(_ condition: () -> Bool) throws -> Bool {
        for _ in 0..<100 where !condition() { RunLoop.main.run(until: Date().addingTimeInterval(0.02)) }
        return condition()
    }
}
