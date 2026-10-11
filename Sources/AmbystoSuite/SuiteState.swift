import Foundation

/// A row of the readout (spec "The readout").
public enum SuiteRow: Sendable, Equatable {
    case running(GlanceRecord)
    /// Stale or stopped: shown without numbers for 60 s, then gone.
    case notRunning(SuiteApp)

    public var app: SuiteApp {
        switch self {
        case .running(let record): record.app
        case .notRunning(let app): app
        }
    }
}

/// The pure rules of the suite (spec "Active", "Each app's own icon"), fed with the valid records and the time.
/// Every participant runs the same machine on the same folder, so they agree within the hysteresis margin.
public struct SuiteStateMachine: Sendable {
    /// Below two fresh participants for this long, without a break, before the readout turns off.
    public static let offDelay: Double = 10
    /// After a wake from sleep, nothing turns off for this long.
    public static let wakeGrace: Double = 10
    /// Active with no fresh `hosting: true` record for this long: every app shows its own icon again.
    public static let hostGrace: Double = 5
    /// A row that stopped being fresh stays as "not running" for this long.
    public static let notRunningShown: Double = 60

    public private(set) var isActive = false
    private var belowSince: Double?
    private var wakeUntil = -Double.infinity
    private var activeSince: Double?
    private var hostSeenAt: Double?
    private var latest: [SuiteApp: GlanceRecord] = [:]
    private var lastFreshAt: [SuiteApp: Double] = [:]
    private var now: Double = 0

    public init() {}

    public mutating func update(records: [SuiteApp: GlanceRecord], now: Double) {
        self.now = now
        let fresh = records.filter { $0.key.participates && $0.value.isFresh(now: now) }
        latest = records.filter { $0.key.participates }   // a record that vanished or turned invalid shows no old numbers
        for app in fresh.keys { lastFreshAt[app] = now }

        if fresh.count >= 2 {
            if !isActive {
                isActive = true
                activeSince = now
                hostSeenAt = nil
            }
            belowSince = nil
        } else if isActive {
            if now < wakeUntil {
                belowSince = nil
            } else {
                let since = belowSince ?? now
                belowSince = since
                if now - since >= Self.offDelay {
                    isActive = false
                    activeSince = nil
                    hostSeenAt = nil
                    belowSince = nil
                }
            }
        }
        if isActive, fresh.values.contains(where: \.hosting) { hostSeenAt = now }
    }

    public mutating func didWake(now: Double) {
        wakeUntil = now + Self.wakeGrace
        belowSince = nil
    }

    /// No fresh record has said `hosting: true` for `hostGrace` seconds while active.
    public var hostMissing: Bool {
        guard isActive, let reference = hostSeenAt ?? activeSince else { return false }
        return now - reference > Self.hostGrace
    }

    /// Spec "Each app's own icon": not active, or the user's opt-in, or no host for `hostGrace` seconds.
    public func showsOwnIcon(optIn: Bool) -> Bool {
        !isActive || optIn || hostMissing
    }

    /// Participants in catalog order: fresh ones with their record, recently gone ones as "not running".
    public var rows: [SuiteRow] {
        SuiteApp.allCases.compactMap { app -> SuiteRow? in
            guard app.participates else { return nil }
            if let record = latest[app], record.isFresh(now: now) { return .running(record) }
            guard let freshAt = lastFreshAt[app], now - freshAt <= Self.notRunningShown else { return nil }
            return .notRunning(app)
        }
    }

    /// The worst level among fresh records.
    public var worstLevel: GlanceLevel {
        rows.compactMap { row -> GlanceLevel? in
            if case .running(let record) = row { return record.level }
            return nil
        }.max() ?? .ok
    }

    /// The values the menu bar item shows: the user's choice where those metrics are fresh, else the default
    /// (Optimer's first metric when Optimer runs, else the first metric of the first fresh participant).
    public func menuBarValues(chosen: [MetricRef]?) -> [(MetricRef, GlanceMetric)] {
        let fresh: [SuiteApp: GlanceRecord] = Dictionary(uniqueKeysWithValues: rows.compactMap { row in
            if case .running(let record) = row { return (record.app, record) }
            return nil
        })
        if let chosen, !chosen.isEmpty {
            return chosen.prefix(SuiteSettings.maxMenuBarValues).compactMap { ref in
                fresh[ref.app]?.metrics.first(where: { $0.id == ref.metric }).map { (ref, $0) }
            }
        }
        for app in SuiteApp.allCases {   // Optimer is first in the catalog
            if let metric = fresh[app]?.metrics.first { return [(MetricRef(app: app, metric: metric.id), metric)] }
        }
        return []
    }
}
