import Foundation
import Testing
@testable import AmbystoSuite

private func record(_ app: SuiteApp, at: Double, hosting: Bool = false, state: GlanceState = .running,
                    level: GlanceLevel = .ok, metrics: [GlanceMetric] = []) -> GlanceRecord {
    GlanceRecord(app: app, hosting: hosting, state: state, at: at, ttl: 10, level: level, metrics: metrics)
}

@Suite("Suite state")
struct SuiteStateTests {
    @Test(arguments: [(0, false), (1, false), (2, true), (3, true)])
    func activeWithFreshParticipants(count: Int, active: Bool) {
        let apps: [SuiteApp] = [.optimer, .devbox, .power]
        var machine = SuiteStateMachine()
        machine.update(records: Dictionary(uniqueKeysWithValues: apps.prefix(count).map { ($0, record($0, at: 100)) }), now: 100)
        #expect(machine.isActive == active)
    }

    @Test func staleStoppedAndNonParticipantsDoNotCount() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 80),
                                 .power: record(.power, at: 100, state: .stopped),
                                 .space: record(.space, at: 100), .tune: record(.tune, at: 100)], now: 100)
        #expect(!machine.isActive)
    }

    @Test func turnsOnAtOnceAndOffOnlyAfterTenSeconds() {
        var machine = SuiteStateMachine()
        let both: [SuiteApp: GlanceRecord] = [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)]
        machine.update(records: both, now: 100)
        #expect(machine.isActive)
        let one: [SuiteApp: GlanceRecord] = [.optimer: record(.optimer, at: 111)]
        machine.update(records: one, now: 111)   // devbox went stale at 110
        #expect(machine.isActive)
        machine.update(records: one, now: 120.9)
        #expect(machine.isActive)
        machine.update(records: one, now: 121)
        #expect(!machine.isActive)
    }

    @Test func aBriefDipDoesNotTurnItOff() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 100)
        machine.update(records: [.optimer: record(.optimer, at: 111)], now: 111)
        machine.update(records: [.optimer: record(.optimer, at: 115), .devbox: record(.devbox, at: 115)], now: 115)
        machine.update(records: [.optimer: record(.optimer, at: 124)], now: 124)
        #expect(machine.isActive)   // below since 124 only (the dip at 111 ended at 115)
    }

    @Test func nothingTurnsOffForTenSecondsAfterWake() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 100)
        machine.didWake(now: 500)   // every record is stale after sleep
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 509)
        #expect(machine.isActive)
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 519.9)
        #expect(machine.isActive)
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 520)
        #expect(machine.isActive)   // the grace is over; the 10 s count starts now
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100)], now: 530)
        #expect(!machine.isActive)
    }

    @Test func ownIconRule() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100)], now: 100)
        #expect(machine.showsOwnIcon(optIn: false))   // not active
        machine.update(records: [.optimer: record(.optimer, at: 100, hosting: true), .devbox: record(.devbox, at: 100)], now: 100)
        #expect(!machine.showsOwnIcon(optIn: false))
        #expect(machine.showsOwnIcon(optIn: true))
    }

    @Test func ownIconsReturnWhenNoHostAppearsForFiveSeconds() {
        var machine = SuiteStateMachine()
        let noHost: (Double) -> [SuiteApp: GlanceRecord] = { [.optimer: record(.optimer, at: $0), .devbox: record(.devbox, at: $0)] }
        machine.update(records: noHost(100), now: 100)
        #expect(!machine.showsOwnIcon(optIn: false))
        machine.update(records: noHost(105), now: 105)
        #expect(!machine.showsOwnIcon(optIn: false))
        machine.update(records: noHost(105.1), now: 105.1)
        #expect(machine.showsOwnIcon(optIn: false))
        machine.update(records: [.optimer: record(.optimer, at: 106, hosting: true), .devbox: record(.devbox, at: 106)], now: 106)
        #expect(!machine.showsOwnIcon(optIn: false))
    }

    @Test func rowsKeepGoneAppsAsNotRunningForSixtySeconds() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100), .devbox: record(.devbox, at: 100),
                                 .power: record(.power, at: 100)], now: 100)
        #expect(machine.rows.map(\.app) == [.optimer, .devbox, .power])
        let later: (Double) -> [SuiteApp: GlanceRecord] = {
            [.optimer: record(.optimer, at: $0), .devbox: record(.devbox, at: $0), .power: record(.power, at: 100, state: .stopped)]
        }
        machine.update(records: later(101), now: 101)
        #expect(machine.rows == [.running(record(.optimer, at: 101)), .running(record(.devbox, at: 101)), .notRunning(.power)])
        machine.update(records: later(160), now: 160)
        #expect(machine.rows.last == .notRunning(.power))
        machine.update(records: later(160.5), now: 160.5)
        #expect(machine.rows.map(\.app) == [.optimer, .devbox])
    }

    @Test func menuBarValuesDefaultToOptimerThenCatalogOrder() {
        let mem = GlanceMetric(id: "mem", label: "Memory", value: 62, unit: "%")
        let srv = GlanceMetric(id: "servers", label: "Servers", value: 3)
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100, metrics: [mem]),
                                 .devbox: record(.devbox, at: 100, metrics: [srv])], now: 100)
        #expect(machine.menuBarValues(chosen: nil).map(\.1) == [mem])
        #expect(machine.menuBarValues(chosen: [MetricRef(app: .devbox, metric: "servers"), MetricRef(app: .power, metric: "x")])
            .map(\.1) == [srv])
        machine.update(records: [.devbox: record(.devbox, at: 101, metrics: [srv]), .power: record(.power, at: 101)], now: 101)
        #expect(machine.menuBarValues(chosen: nil).map(\.1) == [srv])
    }

    @Test func worstLevelAmongFreshRecords() {
        var machine = SuiteStateMachine()
        machine.update(records: [.optimer: record(.optimer, at: 100, level: .notice), .devbox: record(.devbox, at: 100),
                                 .power: record(.power, at: 50, level: .alert)], now: 100)
        #expect(machine.worstLevel == .notice)
    }
}

@Suite("Suite settings")
struct SuiteSettingsTests {
    @Test func roundTripKeepsUnknownKeys() throws {
        let text = #"{"floatingVisible":true,"menuBarValues":[{"app":"power","metric":"battery"}],"fromANewerHost":{"a":[1,"b"]},"statusItemPosition":312}"#
        let settings = SuiteSettings.decode(Data(text.utf8))
        #expect(settings.floatingVisible)
        #expect(settings.menuBarValues == [MetricRef(app: .power, metric: "battery")])
        #expect(settings.statusItemPosition == 312)
        let again = SuiteSettings.decode(try settings.encode())
        #expect(again == settings)
        #expect(String(decoding: try settings.encode(), as: UTF8.self).contains("fromANewerHost"))
    }

    @Test func garbageGivesDefaults() {
        #expect(SuiteSettings.decode(Data("nope".utf8)) == SuiteSettings())
    }

    @Test func atMostTwoMenuBarValues() {
        let text = #"{"menuBarValues":[{"app":"power","metric":"a"},{"app":"power","metric":"b"},{"app":"power","metric":"c"}]}"#
        #expect(SuiteSettings.decode(Data(text.utf8)).menuBarValues?.count == 2)
    }
}
