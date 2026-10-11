import AmbystoSuite
import AppKit
import SwiftUI

/// The host's menu bar item. A protocol so unit tests never need a menu bar.
@MainActor
public protocol SuiteStatusItem: AnyObject {
    /// The item is actually shown (the user can hide it in the menu bar settings); the host publishes `hosting` from this.
    var isVisible: Bool { get }
    func update(values: [(MetricRef, GlanceMetric)], worst: GlanceLevel)
    /// Takes the item out of the menu bar. Called before `stopped` is published, so two items never show.
    func withdraw()
}

/// Creates the host's status item. `.system` is a real `NSStatusItem`; tests pass a fake.
public struct SuiteStatusItemFactory: Sendable {
    let make: @MainActor (_ host: SuiteHost) -> SuiteStatusItem
    /// The position the system keeps for the item, to copy into `settings.json`; `nil` for fakes.
    let preferredPosition: @MainActor () -> Double?

    public init(preferredPosition: @escaping @MainActor () -> Double? = { nil },
                make: @escaping @MainActor (_ host: SuiteHost) -> SuiteStatusItem) {
        self.make = make
        self.preferredPosition = preferredPosition
    }

    public static let system = SuiteStatusItemFactory(
        preferredPosition: { SuiteStatusItemPosition.read() },
        make: { SystemSuiteStatusItem(host: $0) })
}

/// macOS keeps a status item's position in the app's own defaults under an undocumented key. The host copies it
/// between `settings.json` and its defaults so the item stays put when another app becomes the host.
public enum SuiteStatusItemPosition {
    public static let autosaveName = "AmbystoSuite"
    public static let defaultsKey = "NSStatusItem Preferred Position AmbystoSuite"

    public static func read(defaults: UserDefaults = .standard) -> Double? {
        defaults.object(forKey: defaultsKey) as? Double
    }

    public static func write(_ position: Double, defaults: UserDefaults = .standard) {
        defaults.set(position, forKey: defaultsKey)
    }
}

/// The real thing: a variable-length `NSStatusItem` showing the mark and up to two values; a click toggles a popover.
@MainActor
final class SystemSuiteStatusItem: NSObject, SuiteStatusItem {
    private let item: NSStatusItem
    private let popover = NSPopover()

    init(host: SuiteHost) {
        if let position = host.participant.settings.statusItemPosition {
            SuiteStatusItemPosition.write(position)   // before the item exists, so macOS places it there
        }
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        item.autosaveName = SuiteStatusItemPosition.autosaveName
        super.init()
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(rootView: SuitePopover(suite: host.participant))
        if let button = item.button {
            button.target = self
            button.action = #selector(togglePopover(_:))
            button.imagePosition = .imageLeading
            button.image = SuiteMark.image(level: .ok)
            button.setAccessibilityLabel("Ambysto suite")
        }
    }

    var isVisible: Bool { item.isVisible }

    func update(values: [(MetricRef, GlanceMetric)], worst: GlanceLevel) {
        guard let button = item.button else { return }
        button.image = SuiteMark.image(level: worst)
        button.contentTintColor = worst == .ok ? nil : Theme.nsColor(worst)
        let titles = values.map { SuiteFormat.text($0.1) }
        let font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
        button.attributedTitle = NSAttributedString(
            string: titles.isEmpty ? "" : " " + titles.joined(separator: "  "), attributes: [.font: font])
        button.setAccessibilityLabel(SuiteFormat.accessibilityLabel(values: values, worst: worst))
    }

    func withdraw() {
        popover.close()
        NSStatusBar.system.removeStatusItem(item)
    }

    @objc private func togglePopover(_ sender: Any?) {
        guard let button = item.button else { return }
        if popover.isShown {
            popover.close()
        } else {
            popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        }
    }
}

/// The Ambysto mark, drawn in code: seven short arcs forming a ring, a template image so the menu bar tints it.
/// For `notice` and `alert` a symbol is drawn next to it, so colour is never the only signal.
enum SuiteMark {
    static let size: CGFloat = 16

    static func image(level: GlanceLevel) -> NSImage {
        let glyphName = Theme.glyph(level)
        let width = glyphName == nil ? size : size + 14
        let image = NSImage(size: NSSize(width: width, height: size), flipped: false) { _ in
            drawRing()
            if let glyphName,
               let symbol = NSImage(systemSymbolName: glyphName, accessibilityDescription: nil)?
                   .withSymbolConfiguration(.init(pointSize: 11, weight: .semibold)) {
                symbol.draw(in: NSRect(x: size + 2, y: 2, width: 11, height: 12))
            }
            return true
        }
        image.isTemplate = true
        return image
    }

    private static func drawRing() {
        let center = NSPoint(x: size / 2, y: size / 2)
        let radius: CGFloat = 6.2
        let arcs = 7
        let step = 360.0 / Double(arcs)
        let sweep = step * 0.62
        NSColor.black.setStroke()
        for index in 0..<arcs {
            let start = 90 + Double(index) * step
            let path = NSBezierPath()
            path.appendArc(withCenter: center, radius: radius, startAngle: start, endAngle: start + sweep)
            path.lineWidth = 1.9
            path.lineCapStyle = .round
            path.stroke()
        }
    }
}
