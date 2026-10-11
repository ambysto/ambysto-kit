import AmbystoSuite
import Foundation
import Testing
@testable import AmbystoUI

@MainActor
final class FakeStatusItem: SuiteStatusItem {
    var isVisible: Bool
    var withdrawn = false
    var lastWorst: GlanceLevel = .ok
    /// Runs inside `withdraw()`, to look at the world at that moment.
    var onWithdraw: (() -> Void)?
    init(visible: Bool = true) { isVisible = visible }
    func update(values: [(MetricRef, GlanceMetric)], worst: GlanceLevel) { lastWorst = worst }
    func withdraw() {
        withdrawn = true
        onWithdraw?()
    }
}

/// Every fake item a factory made, in order.
@MainActor
final class ItemLog {
    var items: [FakeStatusItem] = []
}

/// One participant on a temp folder, with its own defaults domain and a fake status item.
@MainActor
struct Rig {
    let participant: SuiteParticipant
    let defaultsName: String
    let log = ItemLog()

    init(_ app: SuiteApp, folder: SuiteFolder, folders: [SuiteFolder]? = nil, visible: Bool = true,
         clock: @escaping @Sendable () -> Double = { Date().timeIntervalSince1970 }) {
        defaultsName = "ambysto-ui-test-\(UUID().uuidString)"
        let log = log
        participant = SuiteParticipant(
            app: app, folders: folders ?? [folder], defaults: UserDefaults(suiteName: defaultsName)!,
            statusItems: SuiteStatusItemFactory { _ in
                let item = FakeStatusItem(visible: visible)
                log.items.append(item)
                return item
            }, clock: clock, observeSystem: false, onOpen: {})
        participant.publish(metrics: [GlanceMetric(id: "m", label: app.shortName, value: 1)])
    }

    func finish() {
        participant.shutdown()
        UserDefaults(suiteName: defaultsName)?.removePersistentDomain(forName: defaultsName)
    }
}

func makeTempFolder() throws -> SuiteFolder {
    let folder = SuiteFolder(url: FileManager.default.temporaryDirectory.appendingPathComponent("ambysto-ui-\(UUID().uuidString)"))
    try folder.prepare()
    return folder
}

/// Polls (refreshing each participant when asked) until `condition` holds.
@MainActor
func settle(seconds: Double = 5, refreshing participants: [SuiteParticipant] = [], _ condition: () -> Bool) async -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    while Date() < deadline {
        participants.forEach { $0.refresh() }
        if condition() { return true }
        try? await Task.sleep(for: .milliseconds(20))
    }
    return condition()
}

@MainActor
@Suite("SuiteParticipant")
struct SuiteParticipantTests {
    @Test func twoParticipantsShareOneHost() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder), b = Rig(.devbox, folder: folder)
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]

        let ready = await settle(refreshing: both) {
            a.participant.isActive && b.participant.isActive && both.filter(\.isHost).count == 1
        }
        #expect(ready)
        #expect(!a.participant.showsOwnIcon)
        #expect(!b.participant.showsOwnIcon)
        #expect(a.participant.rows.count == 2)
    }

    @Test func theOtherBecomesHostWhenTheHostQuits() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder), b = Rig(.devbox, folder: folder)
        defer { a.finish(); b.finish() }
        let both = [a.participant, b.participant]
        #expect(await settle(refreshing: both) { both.filter(\.isHost).count == 1 && both.allSatisfy(\.isActive) })

        let (host, other) = a.participant.isHost ? (a.participant, b.participant) : (b.participant, a.participant)
        host.shutdown()
        #expect(!host.isHost)
        // No refresh: the lock hand-over alone must do it.
        #expect(await settle(seconds: 2) { other.isHost })
    }

    @Test func aStoppedRecordTurnsItsRowIntoNotRunning() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder), b = Rig(.devbox, folder: folder)
        defer { a.finish(); b.finish() }
        #expect(await settle(refreshing: [a.participant, b.participant]) {
            a.participant.rows.count == 2 && b.participant.rows.count == 2
        })
        b.participant.shutdown()
        #expect(await settle(refreshing: [a.participant]) { a.participant.rows.contains(.notRunning(.devbox)) })
    }

    @Test func ownIconShowsWhileAlone() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        a.participant.refresh()
        #expect(!a.participant.isActive)
        #expect(a.participant.showsOwnIcon)
        #expect(!a.participant.isHost)
    }

    @Test func optInKeepsTheOwnIconWhileActive() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder), b = Rig(.devbox, folder: folder)
        defer { a.finish(); b.finish() }
        #expect(await settle(refreshing: [a.participant, b.participant]) { a.participant.isActive && b.participant.isActive })
        #expect(!a.participant.showsOwnIcon)
        a.participant.showOwnIconSetting = true
        #expect(a.participant.showsOwnIcon)
        #expect(!b.participant.showsOwnIcon)
        let defaults = UserDefaults(suiteName: a.defaultsName)!
        #expect(defaults.bool(forKey: "AmbystoSuite.showOwnIcon"))
    }

    @Test func echoedWritesAreIgnoredAndRemovalByTheUserSticks() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        a.participant.refresh()
        #expect(a.participant.showsOwnIcon)
        // SwiftUI writes the value it just read, several times.
        for _ in 0..<3 { a.participant.showsOwnIcon = true }
        #expect(a.participant.showsOwnIcon)
        a.participant.showOwnIconSetting = true
        a.participant.showsOwnIcon = true
        #expect(a.participant.showOwnIconSetting)

        // The user drags the icon out of the menu bar: that is the opt-in turned off, and the icon stays away.
        a.participant.showsOwnIcon = false
        #expect(!a.participant.showsOwnIcon)
        #expect(!a.participant.showOwnIconSetting)
        a.participant.refresh()
        #expect(!a.participant.showsOwnIcon)   // even though the readout is not active
        // Asking for it again in Settings brings it back.
        a.participant.showOwnIconSetting = true
        #expect(a.participant.showsOwnIcon)
    }

    @Test func aSecondShutdownIsHarmless() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        a.participant.shutdown()
        a.participant.shutdown()
        a.participant.publish(metrics: [])   // ignored after shutdown
        guard case .valid(let record)? = GlanceStore(folder: folder).read(.optimer) else {
            Issue.record("no record")
            return
        }
        #expect(record.state == .stopped)
    }

    @Test func aBrokenRecordIsNeverWritten() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        a.participant.publish(metrics: [GlanceMetric(id: "Bad Id", label: "x", value: 1)])
        guard case .valid(let record)? = GlanceStore(folder: folder).read(.optimer) else {
            Issue.record("no record")
            return
        }
        #expect(record.metrics.map(\.id) == ["m"])   // the earlier valid record is still there
    }

    @Test func anAppWhoseRecordWasRefusedDoesNotCountItself() async throws {
        let folder = try makeTempFolder()
        defer { try? FileManager.default.removeItem(at: folder.url) }
        let a = Rig(.optimer, folder: folder)
        defer { a.finish() }
        a.participant.publish(metrics: [GlanceMetric(id: "m", label: "Optimer", value: 2)], ttl: 60)   // stays fresh
        let clock = TestClock(Date().timeIntervalSince1970)
        let defaultsName = "ambysto-ui-test-\(UUID().uuidString)"
        defer { UserDefaults(suiteName: defaultsName)?.removePersistentDomain(forName: defaultsName) }
        let b = SuiteParticipant(app: .devbox, folders: [folder], defaults: UserDefaults(suiteName: defaultsName)!,
                                 statusItems: SuiteStatusItemFactory { _ in FakeStatusItem(visible: true) },
                                 clock: { clock.now }, observeSystem: false, onOpen: {})
        defer { b.shutdown() }
        #expect(b.isActive)   // running and not yet refused: it counts itself, so no icon flash
        b.publish(metrics: [GlanceMetric(id: "Bad Id", label: "x", value: 1)])
        #expect(GlanceStore(folder: folder).read(.devbox) == nil)
        b.refresh()
        clock.advance(11)   // past the 10 s hysteresis: only Optimer is fresh, B no longer counts itself
        b.refresh()
        #expect(!b.isActive)
        #expect(b.showsOwnIcon)
    }

    @Test func aRefusedTTLNeverSetsAFasterTimer() {
        #expect(SuiteParticipant.tickInterval(ttl: 0) == SuiteParticipant.tickInterval(ttl: 3))
        #expect(SuiteParticipant.tickInterval(ttl: 1000) == 5)
    }
}

@Suite("Suite text and theme")
struct SuiteTextTests {
    @Test func numbersFollowTheLocale() {
        let metric = GlanceMetric(id: "mem", label: "Memory", value: 62.5, unit: "%")
        #expect(SuiteFormat.text(metric, locale: Locale(identifier: "de_DE")) == "62,5\u{00A0}%")
        #expect(SuiteFormat.text(metric, locale: Locale(identifier: "en_US")) == "62.5\u{00A0}%")
        #expect(SuiteFormat.text(GlanceMetric(id: "s", label: "State", state: .active)) == "Active")
    }

    @Test func voiceOverLabelNamesTheLevel() {
        let metric = GlanceMetric(id: "mem", label: "Memory", value: 62, unit: "%", level: .notice)
        let label = SuiteFormat.accessibilityLabel(values: [(MetricRef(app: .optimer, metric: "mem"), metric)],
                                                   worst: .notice, locale: Locale(identifier: "en_US"))
        #expect(label == "Ambysto suite, Memory 62\u{00A0}%, notice")
    }

    @Test func colourIsNeverTheOnlySignal() {
        #expect(Theme.glyph(.ok) == nil)
        #expect(Theme.glyph(.notice) == "exclamationmark.circle")
        #expect(Theme.glyph(.alert) == "exclamationmark.triangle.fill")
    }
}

/// A clock tests move by hand.
final class TestClock: @unchecked Sendable {
    private let lock = NSLock()
    private var value: Double
    init(_ start: Double) { value = start }
    var now: Double { lock.withLock { value } }
    func advance(_ seconds: Double) { lock.withLock { value += seconds } }
}
