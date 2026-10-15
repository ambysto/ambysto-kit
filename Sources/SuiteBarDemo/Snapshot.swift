// `SuiteBarDemo --snapshot <dir>` (APS-16 proof): renders the suite views to PNG files, and touches nothing real.
// The participant lives in its own temp folder with fake status items (no menu bar item, no hot key), a scratch
// defaults domain, no system observers and an injected clock; the only deletion is that temp folder, at the end.
import AmbystoSuite
@_spi(Snapshot) import AmbystoUI
import AppKit
import Foundation
import os
import SwiftUI

/// A status item that is nowhere: the snapshot never puts anything in the menu bar.
@MainActor
final class SnapshotStatusItem: SuiteStatusItem {
    var isVisible: Bool { true }
    func update(values: [(MetricRef, GlanceMetric)], worst: GlanceLevel) {}
    func withdraw() {}
}

final class SnapshotClock: Sendable {
    private let value: OSAllocatedUnfairLock<Double>
    init(_ start: Double) { value = OSAllocatedUnfairLock(initialState: start) }
    func read() -> Double { value.withLock { $0 } }
    func advance(_ seconds: Double) { value.withLock { $0 += seconds } }
}

/// Lets the main run loop turn (file events, timers) until `condition` holds or `seconds` pass.
@MainActor
func pump(seconds: Double = 8, until condition: () -> Bool) -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    while Date() < deadline {
        if condition() { return true }
        RunLoop.main.run(until: Date().addingTimeInterval(0.05))
    }
    return condition()
}

struct SnapshotError: LocalizedError {
    let errorDescription: String?
}

/// Draws a view through an `NSHostingView` (an `ImageRenderer` draws nothing inside a `ScrollView`) and writes a PNG.
@MainActor
func renderPNG(_ view: some View, size: CGSize, appearance: NSAppearance.Name, to url: URL) throws {
    let hosting = NSHostingView(rootView: view)
    hosting.appearance = NSAppearance(named: appearance)
    hosting.frame = CGRect(origin: .zero, size: size)
    if size.height == 0 {   // fit the content (rows have no fixed height)
        hosting.frame.size.height = hosting.fittingSize.height
    }
    hosting.layoutSubtreeIfNeeded()
    guard let bitmap = hosting.bitmapImageRepForCachingDisplay(in: hosting.bounds) else {
        throw SnapshotError(errorDescription: "no bitmap for \(url.lastPathComponent)")
    }
    hosting.cacheDisplay(in: hosting.bounds, to: bitmap)
    guard let png = bitmap.representation(using: .png, properties: [:]) else {
        throw SnapshotError(errorDescription: "no PNG for \(url.lastPathComponent)")
    }
    try png.write(to: url, options: .atomic)
    print(url.path)
}

@MainActor
func runSnapshot(directory: String) -> Never {
    let outURL = URL(fileURLWithPath: (directory as NSString).expandingTildeInPath).standardizedFileURL
    if isRealSuiteFolderOrInside(outURL) { fail("refusing the real suite folder \(SuiteFolder.plain().url.path)") }
    // A new or empty folder only, so a run never overwrites files the user keeps there.
    if let existing = try? FileManager.default.contentsOfDirectory(atPath: outURL.path), !existing.isEmpty {
        fail("\(outURL.path) is not empty; give a new or empty folder")
    }

    let tempURL = URL(fileURLWithPath: NSTemporaryDirectory()).appendingPathComponent("suitebardemo-snapshot-\(UUID().uuidString)", isDirectory: true)
    if isRealSuiteFolderOrInside(tempURL) { fail("the temp folder is inside the real suite folder: \(tempURL.path)") }
    let folder = SuiteFolder(url: tempURL)
    let defaultsName = "ambysto-suitebardemo-snapshot-\(UUID().uuidString)"
    guard let defaults = UserDefaults(suiteName: defaultsName) else { fail("no scratch defaults") }

    _ = NSApplication.shared
    NSApplication.shared.setActivationPolicy(.accessory)
    do {
        try FileManager.default.createDirectory(at: outURL, withIntermediateDirectories: true)
        try folder.prepare()
    } catch {
        try? FileManager.default.removeItem(at: tempURL)   // only the temp folder this run created
        fail("cannot prepare the folders: \(error.localizedDescription)")
    }

    let clock = SnapshotClock(Date().timeIntervalSince1970)
    let suite = SuiteParticipant(
        app: .optimer, folders: [folder], defaults: defaults,
        statusItems: SuiteStatusItemFactory { _ in SnapshotStatusItem() },
        clock: { clock.read() }, observeSystem: false, onOpen: {})

    let optimer = [GlanceMetric(id: "mem", label: "Memory", value: 71, unit: "%", level: .notice),
                   GlanceMetric(id: "cpu", label: "CPU", value: 18, unit: "%")]
    let devbox = [GlanceMetric(id: "servers", label: "Servers", value: 3)]
    let steady = [GlanceMetric(id: "mode", label: "Mode", state: .active)]
    let store = GlanceStore(folder: folder)
    func publishAll(steadyToo: Bool) {
        suite.publish(level: .notice, metrics: optimer)
        try? store.write(GlanceRecord(app: .devbox, at: clock.read(), metrics: devbox))
        if steadyToo { try? store.write(GlanceRecord(app: .steady, at: clock.read(), metrics: steady)) }
    }

    var failed = false
    /// Leaves the way a clean run does: participant shut down, scratch defaults gone, and only the temp folder removed.
    func finish(_ code: Int32, _ message: String? = nil) -> Never {
        if let message { FileHandle.standardError.write(Data("SuiteBarDemo: \(message)\n".utf8)) }
        suite.shutdown()
        defaults.removePersistentDomain(forName: defaultsName)
        try? FileManager.default.removeItem(at: tempURL)   // the one deletion: the temp folder this run created
        exit(code)
    }
    func render(_ name: String, size: CGSize, appearance: NSAppearance.Name, _ view: some View) {
        do { try renderPNG(view, size: size, appearance: appearance, to: outURL.appendingPathComponent(name)) } catch {
            FileHandle.standardError.write(Data("SuiteBarDemo: \(name): \(error.localizedDescription)\n".utf8))
            failed = true
        }
    }
    func rowsView(_ scheme: ColorScheme) -> some View {
        SuiteRowsView(suite: suite).padding(16).frame(width: 300, alignment: .topLeading)
            .background(Theme.page).environment(\.colorScheme, scheme)
    }
    func barView(_ name: String) -> some View {
        SuiteFloatingBar(suite: suite).frame(width: 300, height: name == "floating-compact.png" ? 132 : 460)
            .padding(12).background(Color(white: 0.5))
    }

    publishAll(steadyToo: true)
    let allRunning = pump {
        suite.isHost && suite.rows.count == 3 && suite.rows.allSatisfy { if case .running(let record) = $0 { return !record.metrics.isEmpty }; return false }
    }
    if !allRunning { finish(1, "the three samples did not become running rows in time") }

    render("floating-compact.png", size: CGSize(width: 324, height: 156), appearance: .darkAqua, barView("floating-compact.png"))
    var settings = suite.settings
    settings.floatingExpanded = true
    suite.settings = settings
    render("floating-expanded.png", size: CGSize(width: 324, height: 484), appearance: .darkAqua, barView("floating-expanded.png"))
    render("rows-light.png", size: CGSize(width: 300, height: 0), appearance: .aqua, rowsView(.light))
    render("rows-dark.png", size: CGSize(width: 300, height: 0), appearance: .darkAqua, rowsView(.dark))

    // Steady's record expires while Optimer and DevBox are written fresh: Steady becomes a "not running" row.
    clock.advance(30)
    publishAll(steadyToo: false)
    let gone = pump {
        suite.rows.count == 3 && suite.rows.contains { $0 == .notRunning(.steady) }
    }
    if !gone { finish(1, "Steady did not become a not-running row in time") }
    render("rows-not-running.png", size: CGSize(width: 300, height: 0), appearance: .aqua, rowsView(.light))

    finish(failed ? 1 : 0)
}
