// From Ambysto Optimer (macos/Sources/AmbystoOptimer/Floating/FloatingMonitorController.swift, ambysto-optimer@549da79)
import AppKit
import SwiftUI

/// An always-on-top floating panel for any SwiftUI content: creation, placement, the animated bar <-> expanded
/// resize and a position sink. Optimer's controller, generalised; it keeps no settings of its own.
@MainActor
public final class FloatingPanel: NSObject, NSWindowDelegate {
    public let barSize: CGSize
    public let expandedSize: CGSize

    private let content: AnyView
    private let onMove: @MainActor (CGPoint) -> Void
    private var topLeft: CGPoint?
    private var idleAlpha: CGFloat
    private var hovering = false
    private var panel: NSPanel?
    private var isExpanded = false

    /// `topLeft`: a saved top-left corner in screen coordinates; used only while it is still on a screen.
    /// `onMove` receives the new top-left each time the user moves the panel.
    public init<Content: View>(content: Content, barSize: CGSize, expandedSize: CGSize, topLeft: CGPoint?,
                               onMove: @escaping @MainActor (CGPoint) -> Void, idleAlpha: CGFloat = 1) {
        self.content = AnyView(content)
        self.barSize = barSize
        self.expandedSize = expandedSize
        self.topLeft = topLeft
        self.onMove = onMove
        self.idleAlpha = idleAlpha
    }

    public var isVisible: Bool { panel != nil }

    /// Shows the panel without taking focus from the current app. Safe to call repeatedly.
    public func show() {
        let panel = panel ?? makePanel()
        panel.alphaValue = hovering ? 1 : idleAlpha
        panel.orderFrontRegardless()
    }

    /// Hiding drops the SwiftUI content (ending its sampling surface) and closes the window,
    /// so toggling never leaves stale windows behind.
    public func hide() {
        guard let panel else { return }
        panel.delegate = nil
        panel.contentView = nil
        panel.close()
        self.panel = nil
    }

    /// Switches between the bar and the expanded size, animated while visible.
    public func setExpanded(_ expanded: Bool) {
        guard isExpanded != expanded else { return }
        isExpanded = expanded
        guard let panel else { return }
        let size = expanded ? expandedSize : barSize
        let visible = (panel.screen ?? NSScreen.main)?.visibleFrame ?? panel.frame
        panel.setFrame(FloatingLayout.resized(panel.frame, to: size, visible: visible), display: true, animate: true)
    }

    /// Fully opaque while the pointer is over the panel, `idleAlpha` otherwise.
    public func setHovering(_ value: Bool) {
        hovering = value
        panel?.alphaValue = value ? 1 : idleAlpha
    }

    private func makePanel() -> NSPanel {
        let size = isExpanded ? expandedSize : barSize
        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
            backing: .buffered, defer: false
        )
        panel.isFloatingPanel = true
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.sharingType = .readOnly   // visible in screenshots and screen sharing
        panel.isReleasedWhenClosed = false   // ARC owns it; close() must not also release
        panel.contentView = NSHostingView(rootView: content)
        panel.setFrame(initialFrame(size: size), display: false)
        panel.delegate = self   // after the first placement, so placing it is not reported as a move
        self.panel = panel
        return panel
    }

    private func initialFrame(size: CGSize) -> CGRect {
        let visibleFrames = NSScreen.screens.map(\.visibleFrame)
        if let topLeft, let frame = FloatingLayout.restoredFrame(topLeft: topLeft, size: size, visibleFrames: visibleFrames) {
            return frame
        }
        let visible = NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 875)
        return FloatingLayout.initialFrame(size: size, visible: visible)
    }

    public func windowDidMove(_ notification: Notification) {
        guard let frame = panel?.frame else { return }
        let corner = CGPoint(x: frame.minX, y: frame.maxY)
        topLeft = corner
        onMove(corner)
    }
}
