import AmbystoSuite
import AppKit
import SwiftUI

/// What the host-elect shows while the readout is active: the menu bar item with its popover, the merged floating
/// bar and the suite hotkey. Created and torn down by `SuiteParticipant`; an app never makes one.
@MainActor
public final class SuiteHost {
    public unowned let participant: SuiteParticipant
    private(set) var statusItem: SuiteStatusItem?
    private var panel: FloatingPanel?
    private var hotKey: GlobalHotKey?
    private var shown: (values: [(MetricRef, GlanceMetric)], worst: GlanceLevel)?

    static let barSize = CGSize(width: 300, height: 132)
    static let expandedSize = CGSize(width: 340, height: 460)

    init(participant: SuiteParticipant, factory: SuiteStatusItemFactory, registersHotKey: Bool) {
        self.participant = participant
        statusItem = factory.make(self)
        if registersHotKey {
            let key = GlobalHotKey { [weak self] in self?.toggleFloatingBar() }
            if !key.isRegistered {
                participant.logOnce("shortcut", "the suite shortcut \(GlobalHotKey.displayName) could not be registered: this app still holds the same keys (a duplicate in one process)")
            }
            hotKey = key
        }
        update()
    }

    var isVisible: Bool { statusItem?.isVisible ?? false }

    /// Brings the item and the floating bar in line with the participant's state. Safe to call repeatedly.
    func update() {
        // The item is touched only when what it shows changed: this runs on every file event and tick.
        let values = participant.menuBarValues, worst = participant.worstLevel
        if shown == nil || shown?.worst != worst || !Self.same(shown?.values ?? [], values) {
            statusItem?.update(values: values, worst: worst)
            shown = (values, worst)
        }
        let settings = participant.settings
        if settings.floatingVisible {
            let panel = panel ?? makePanel(topLeft: settings.floatingTopLeft)
            panel.setExpanded(settings.floatingExpanded)
            if !panel.isVisible { panel.show() }   // no per-tick re-ordering of windows
        } else {
            panel?.hide()
        }
    }

    /// The clean-quit part of the host: the item first, so two items never show.
    func tearDown() {
        statusItem?.withdraw()
        statusItem = nil
        panel?.hide()
        panel = nil
        hotKey?.invalidate()
        hotKey = nil
    }

    private static func same(_ lhs: [(MetricRef, GlanceMetric)], _ rhs: [(MetricRef, GlanceMetric)]) -> Bool {
        lhs.count == rhs.count && zip(lhs, rhs).allSatisfy { $0.0 == $1.0 && $0.1 == $1.1 }
    }

    private func makePanel(topLeft: CGPoint?) -> FloatingPanel {
        let participant = participant
        let created = FloatingPanel(
            content: SuiteFloatingBar(suite: participant).onHover { [weak self] inside in self?.panel?.setHovering(inside) },
            barSize: Self.barSize, expandedSize: Self.expandedSize, topLeft: topLeft,
            onMove: { participant.floatingBarMoved(to: $0) }, idleAlpha: 1)
        panel = created
        return created
    }

    private func toggleFloatingBar() {
        var settings = participant.settings
        settings.floatingVisible.toggle()
        participant.settings = settings
    }
}
