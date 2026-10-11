import AmbystoSuite
import AppKit
import Foundation
import Observation
import os

/// The one object an app creates to join the suite (spec "The host", "Each app's own icon and floating bar").
/// It publishes the app's glance record, reads everyone's, runs the shared state rules, competes for the host lock
/// and, while it is the host-elect and the readout is active, shows the suite's menu bar item.
@MainActor
@Observable
public final class SuiteParticipant {
    public let app: SuiteApp

    /// At least two participants are running: the app hides its own floating bar and releases its own shortcut.
    public private(set) var isActive = false
    /// This app shows the suite's menu bar item.
    public private(set) var isHost = false
    /// One row per participant in catalog order.
    public private(set) var rows: [SuiteRow] = []
    public private(set) var worstLevel: GlanceLevel = .ok
    /// The values shown next to the mark in the menu bar.
    public private(set) var menuBarValues: [(MetricRef, GlanceMetric)] = []

    private var settingsStorage = SuiteSettings()
    private var showOwnIconStorage: Bool
    private var removedByUser = false
    private var ownIconByRules = true

    @ObservationIgnored private let folders: [SuiteFolder]
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let statusItems: SuiteStatusItemFactory
    @ObservationIgnored private let clock: @Sendable () -> Double
    @ObservationIgnored private let observesSystem: Bool
    @ObservationIgnored private let onOpen: @MainActor () -> Void
    @ObservationIgnored private var machine = SuiteStateMachine()
    @ObservationIgnored private var lastWriteFailed = false
    @ObservationIgnored private var schedule = PublishSchedule()
    @ObservationIgnored private var last: (level: GlanceLevel, metrics: [GlanceMetric], ttl: Int)?
    @ObservationIgnored private(set) var lock: HostLock?
    @ObservationIgnored private var host: SuiteHost?
    @ObservationIgnored private var watchers: [DispatchSourceFileSystemObject] = []
    @ObservationIgnored private(set) var timer: Timer?
    @ObservationIgnored private var timerTTL = SuiteParticipant.defaultTTL
    @ObservationIgnored private var tokens: [(NotificationCenter, NSObjectProtocol)] = []
    @ObservationIgnored private var openObserver: SuiteOpenObserver?
    @ObservationIgnored private var persistTask: Task<Void, Never>?
    @ObservationIgnored private var hostingRecheck: Task<Void, Never>?
    @ObservationIgnored private var hostingRechecksLeft = 0
    @ObservationIgnored private var loggedProblems = Set<String>()
    @ObservationIgnored private var didShutdown = false
    @ObservationIgnored private var publishingSuspended = false

    nonisolated static let showOwnIconKey = "AmbystoSuite.showOwnIcon"
    nonisolated static let openNotification = Notification.Name("com.ambysto.suite.open")
    private static let defaultTTL = 10
    private static let log = Logger(subsystem: "com.ambysto.suite", category: "participant")

    /// The periodic tick for a record with this `ttl`: every 5 s at most, and often enough that a heartbeat is never
    /// later than `ttl / 2` (spec "Freshness") for any allowed ttl (3...60), given the timer's tolerance.
    nonisolated static func tickInterval(ttl: Int) -> TimeInterval {
        let ttl = min(max(ttl, GlanceRecord.ttlRange.lowerBound), GlanceRecord.ttlRange.upperBound)   // a refused ttl never sets a faster timer
        return min(5, Double(ttl) / 2 - 0.5)
    }
    nonisolated static func tickTolerance(ttl: Int) -> TimeInterval { tickInterval(ttl: ttl) * 0.1 }

    /// Reads the suite folders synchronously, so `showsOwnIcon` is right before the app builds its first scene.
    public init(app: SuiteApp,
                folders: [SuiteFolder] = SuiteFolder.reachable(),
                defaults: UserDefaults = .standard,
                statusItems: SuiteStatusItemFactory = .system,
                clock: @escaping @Sendable () -> Double = { Date().timeIntervalSince1970 },
                observeSystem: Bool = true,
                onOpen: @escaping @MainActor () -> Void) {
        self.app = app
        self.folders = folders
        self.defaults = defaults
        self.statusItems = statusItems
        self.clock = clock
        self.observesSystem = observeSystem
        self.onOpen = onOpen
        self.showOwnIconStorage = defaults.bool(forKey: Self.showOwnIconKey)

        for folder in folders {
            do { try folder.prepare() } catch { Self.log.error("suite folder not usable: \(error.localizedDescription, privacy: .public)") }
        }
        evaluate()

        startLocks()
        startWatching()
        startTimer(ttl: Self.defaultTTL)
        startOpenObserver()
        if observeSystem { startSystemObservers() }
    }

    // MARK: Publishing

    /// Remembers the metrics and writes the record when it changed or the heartbeat is due. A record that breaks
    /// the contract is logged once per reason and never written. Keeps publishing on its own until `shutdown()`.
    public func publish(level: GlanceLevel = .ok, metrics: [GlanceMetric], ttl: Int = 10) {
        let first = last == nil
        last = (level, metrics, ttl)
        if ttl != timerTTL { startTimer(ttl: ttl) }
        // The first publish counts this app as running before its file lands, so a second app never flashes its icon.
        if first { evaluate() }
        writeIfDue(margin: 0)
    }

    /// For proofs: stops writing, heartbeat included, so the record goes stale like a hung app's. Not reversible.
    public func suspendPublishing() {
        publishingSuspended = true
    }

    private func writeIfDue(margin: Double) {
        guard !didShutdown, !publishingSuspended, let last else { return }
        let now = clock()
        let record = GlanceRecord(app: app, hosting: isHost, at: now, ttl: last.ttl, level: last.level, metrics: last.metrics)
        if let problem = GlanceCodec.problem(in: record) {
            logOnce("invalid:\(problem)", "glance record not written: \(problem)")
            lastWriteFailed = true
            return
        }
        // `margin` looks ahead one tick (with its tolerance), so a heartbeat is never later than the record's own `ttl / 2`.
        guard schedule.isDue(record, now: now + margin) else { return }
        let wrote = write(record)
        lastWriteFailed = !wrote
        if wrote { schedule.didWrite(record) }
    }

    @discardableResult
    private func write(_ record: GlanceRecord) -> Bool {
        var wrote = false
        for folder in folders {
            do {
                try GlanceStore(folder: folder).write(record)
                wrote = true
            } catch {
                logOnce("write:\(folder.url.path)", "glance record not written: \(error.localizedDescription)")
            }
        }
        return wrote
    }

    private func logOnce(_ key: String, _ message: String) {
        guard loggedProblems.insert(key).inserted else { return }
        Self.log.error("\(message, privacy: .public)")
    }

    // MARK: Own icon

    /// For `MenuBarExtra(isInserted:)`. Reading: the spec's rules for the app's own icon, unless the user removed it.
    /// Writing: SwiftUI echoes the same value several times, so only a write that differs is an action, and the only
    /// one that matters is `false` while the icon is shown: the user dragged it out of the menu bar.
    public var showsOwnIcon: Bool {
        get { (ownIconByRules || showOwnIconStorage) && !removedByUser }
        set {
            guard newValue != showsOwnIcon else { return }
            if !newValue {
                showOwnIconSetting = false
                removedByUser = true
            }
        }
    }

    /// "Show my own menu bar icon" (persisted). Turning it on again also brings back an icon the user dragged out.
    public var showOwnIconSetting: Bool {
        get { showOwnIconStorage }
        set {
            showOwnIconStorage = newValue
            if newValue { removedByUser = false }
            defaults.set(newValue, forKey: Self.showOwnIconKey)
        }
    }

    // MARK: Settings

    /// The readout's choices. Written only by the host; a write is saved to every suite folder.
    public var settings: SuiteSettings {
        get { settingsStorage }
        set {
            guard isHost, newValue != settingsStorage else { return }
            settingsStorage = newValue
            host?.update()
            persistSoon()
        }
    }

    func floatingBarMoved(to topLeft: CGPoint) {
        guard isHost else { return }
        settingsStorage.floatingTopLeft = topLeft
        persistSoon()
    }

    /// Dragging produces many moves: save the last one shortly after it stops.
    private func persistSoon() {
        persistTask?.cancel()
        persistTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(400))
            guard !Task.isCancelled else { return }
            self?.persistSettings()
        }
    }

    private func persistSettings() {
        persistTask?.cancel()
        persistTask = nil
        for folder in folders {
            do { try settingsStorage.write(to: folder) } catch {
                logOnce("settings:\(folder.url.path)", "suite settings not saved: \(error.localizedDescription)")
            }
        }
    }

    // MARK: Reading and rules

    /// Reads the folders, runs the rules, brings the host in line, republishes when due. `tick` is the 5 s timer:
    /// only it looks ahead for the heartbeat (a write caused by a file event must not cause another write).
    func refresh(tick: Bool = false) {
        guard !didShutdown else { return }
        evaluate()
        syncStatusItemPosition()
        writeIfDue(margin: tick ? Self.tickInterval(ttl: timerTTL) * 1.1 : 0)
    }

    private func evaluate() {
        let now = clock()
        machine.update(records: recordsWithSelf(now: now), now: now)
        assignIfChanged(\.isActive, machine.isActive)
        assignIfChanged(\.rows, machine.rows)
        assignIfChanged(\.worstLevel, machine.worstLevel)
        assignIfChanged(\.ownIconByRules, machine.showsOwnIcon(optIn: false))
        let values = machine.menuBarValues(chosen: settingsStorage.menuBarValues)
        if !Self.sameValues(values, menuBarValues) { menuBarValues = values }
        reconcileHost()
    }

    private func assignIfChanged<Value: Equatable>(_ path: ReferenceWritableKeyPath<SuiteParticipant, Value>, _ value: Value) {
        if self[keyPath: path] != value { self[keyPath: path] = value }
    }

    private static func sameValues(_ lhs: [(MetricRef, GlanceMetric)], _ rhs: [(MetricRef, GlanceMetric)]) -> Bool {
        lhs.count == rhs.count && zip(lhs, rhs).allSatisfy { $0.0 == $1.0 && $0.1 == $1.1 }
    }

    /// Every folder's valid records, newest `at` per app.
    private func readRecords() -> [SuiteApp: GlanceRecord] {
        var merged: [SuiteApp: GlanceRecord] = [:]
        for folder in folders {
            for case .valid(let record) in GlanceStore(folder: folder).readAll().values {
                if let existing = merged[record.app], existing.at >= record.at { continue }
                merged[record.app] = record
            }
        }
        return merged
    }

    /// This app is running, so it counts itself as fresh (`hosting: false`, at `now`) until its own file says so:
    /// without it a second app starts with an empty folder and shows its icon for a moment before its first write.
    private func recordsWithSelf(now: Double) -> [SuiteApp: GlanceRecord] {
        var records = readRecords()
        // Only while nothing says otherwise: a record that was refused or could not be written does not count, so
        // this app never believes in an active readout that the other apps cannot see.
        guard app.participates, !publishingSuspended, !didShutdown, !lastWriteFailed else { return records }
        if let own = records[app], own.isFresh(now: now) { return records }
        records[app] = GlanceRecord(app: app, hosting: false, at: now, ttl: last?.ttl ?? Self.defaultTTL, level: .ok, metrics: [])
        return records
    }

    // MARK: Host

    /// Holds the lock in every suite folder this app publishes to (one `HostLock` takes them in sorted order).
    private var isHostElect: Bool {
        app.canHost && lock?.isHeld == true
    }

    private func reconcileHost() {
        guard !didShutdown else { return }
        let wanted = isHostElect && machine.isActive
        if wanted, host == nil {
            if let stored = folders.first.map(SuiteSettings.read(from:)) { settingsStorage = stored }
            hostingRechecksLeft = 12
            host = SuiteHost(participant: self, factory: statusItems, registersHotKey: observesSystem)
        } else if !wanted, let current = host {
            current.tearDown()
            host = nil
        } else {
            host?.update()
        }
        updateHostingFlag()
    }

    /// `hosting` follows the item's real visibility; a change is published at once. A new status item is not on
    /// screen yet when it is created, so for its first 3 s the check is repeated every 0.25 s instead of waiting
    /// for the next tick (otherwise the other apps would show their icons for the gap).
    private func updateHostingFlag() {
        let hosting = host?.isVisible ?? false
        if host != nil, !hosting, hostingRecheck == nil, hostingRechecksLeft > 0 {
            hostingRechecksLeft -= 1
            hostingRecheck = Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(250))
                guard !Task.isCancelled, let self else { return }
                hostingRecheck = nil
                updateHostingFlag()
            }
        }
        guard hosting != isHost else { return }
        isHost = hosting
        writeIfDue(margin: 0)
    }

    private func syncStatusItemPosition() {
        guard isHost, let position = statusItems.preferredPosition(), position != settingsStorage.statusItemPosition else { return }
        settingsStorage.statusItemPosition = position
        persistSettings()
    }

    private func startLocks() {
        guard app.canHost, !folders.isEmpty else { return }
        let lock = HostLock(urls: folders.map(\.lockURL))
        self.lock = lock
        lock.start { [weak self] in
            Task { @MainActor in self?.refresh() }
        }
    }

    // MARK: Change detection

    private func startWatching() {
        for folder in folders {
            let fd = open(folder.glancesURL.path, O_EVTONLY | O_CLOEXEC)
            guard fd >= 0 else { continue }
            let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: .write, queue: .main)
            source.setEventHandler { [weak self] in
                MainActor.assumeIsolated { self?.refresh() }
            }
            source.setCancelHandler { close(fd) }
            source.resume()
            watchers.append(source)
        }
    }

    /// (Re)arms the periodic tick for `ttl`; `publish` calls it again when the ttl changes.
    private func startTimer(ttl: Int) {
        timer?.invalidate()
        timerTTL = ttl
        let timer = Timer(timeInterval: Self.tickInterval(ttl: ttl), repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh(tick: true) }
        }
        timer.tolerance = Self.tickTolerance(ttl: ttl)
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    // MARK: System events

    private func startSystemObservers() {
        let center = NotificationCenter.default
        tokens.append((center, center.addObserver(forName: NSApplication.willTerminateNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.shutdown() }
        }))
        let workspace = NSWorkspace.shared.notificationCenter
        tokens.append((workspace, workspace.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.didWake() }
        }))
    }

    private func didWake() {
        guard !didShutdown else { return }
        machine.didWake(now: clock())
        schedule.forceNext()
        refresh()
    }

    // MARK: Open

    /// Asks `app` to open its main window (spec "Open").
    public static func requestOpen(_ app: SuiteApp) {
        DistributedNotificationCenter.default().postNotificationName(
            openNotification, object: app.rawValue, userInfo: nil, deliverImmediately: true)
    }

    private func startOpenObserver() {
        let observer = SuiteOpenObserver(onOpen: onOpen)
        DistributedNotificationCenter.default().addObserver(
            observer, selector: #selector(SuiteOpenObserver.opened(_:)), name: Self.openNotification,
            object: app.rawValue, suspensionBehavior: .deliverImmediately)
        openObserver = observer
    }

    // MARK: Quit

    /// The clean-quit order (spec "The host"): remove the item, publish `stopped`, release the locks. Idempotent.
    public func shutdown() {
        guard !didShutdown else { return }
        didShutdown = true
        timer?.invalidate()
        timer = nil
        watchers.forEach { $0.cancel() }
        watchers = []
        for (center, token) in tokens { center.removeObserver(token) }
        tokens = []
        if let openObserver { DistributedNotificationCenter.default().removeObserver(openObserver) }
        openObserver = nil

        hostingRecheck?.cancel()
        hostingRecheck = nil
        host?.tearDown()
        host = nil
        isHost = false
        if persistTask != nil { persistSettings() }   // a pending settings write belongs to this host
        let stopped = GlanceRecord(app: app, hosting: false, state: .stopped, at: clock(), ttl: last?.ttl ?? 10)
        write(stopped)
        lock?.release()
    }
}

/// Receives the distributed `open` notification (a selector, because only that form can ask for immediate delivery).
@MainActor
final class SuiteOpenObserver: NSObject {
    private let onOpen: @MainActor () -> Void

    init(onOpen: @escaping @MainActor () -> Void) {
        self.onOpen = onOpen
    }

    @objc func opened(_ notification: Notification) {
        onOpen()
    }
}
