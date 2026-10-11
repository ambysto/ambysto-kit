// From Ambysto Optimer (macos/Sources/OptimerCore/Floating/FloatingLayout.swift, ambysto-optimer@549da79)
import CoreGraphics

/// Placement rules for the floating monitor panel. All rects are Cocoa screen coordinates (bottom-left origin).
public enum FloatingLayout {
    /// Gap from the screen corner on first launch.
    public static let cornerInset: CGFloat = 16
    /// A saved position is reused only if its top-left lies inside a screen's visible frame inset by this much.
    public static let onScreenInset: CGFloat = 40
    /// Within this distance of the right / bottom edge, resizing grows away from that edge.
    public static let edgeSnap: CGFloat = 60

    /// First placement: top-right corner of the visible frame.
    public static func initialFrame(size: CGSize, visible: CGRect) -> CGRect {
        CGRect(x: visible.maxX - cornerInset - size.width, y: visible.maxY - cornerInset - size.height,
               width: size.width, height: size.height)
    }

    /// The saved top-left, if it is still on one of the screens (displays can be unplugged).
    public static func restoredFrame(topLeft: CGPoint, size: CGSize, visibleFrames: [CGRect]) -> CGRect? {
        guard visibleFrames.contains(where: { $0.insetBy(dx: onScreenInset, dy: onScreenInset).contains(topLeft) }) else {
            return nil
        }
        return CGRect(x: topLeft.x, y: topLeft.y - size.height, width: size.width, height: size.height)
    }

    /// Resizes keeping the top-left, except near the right or bottom edge, where it grows away from that edge.
    /// The result is kept inside the visible frame.
    public static func resized(_ frame: CGRect, to size: CGSize, visible: CGRect) -> CGRect {
        var result = CGRect(x: frame.minX, y: frame.maxY - size.height, width: size.width, height: size.height)
        if frame.maxX > visible.maxX - edgeSnap { result.origin.x = frame.maxX - size.width }
        if frame.minY < visible.minY + edgeSnap { result.origin.y = frame.minY }
        return clamped(result, in: visible)
    }

    public static func clamped(_ frame: CGRect, in visible: CGRect) -> CGRect {
        var result = frame
        result.origin.x = min(max(result.minX, visible.minX), visible.maxX - result.width)
        result.origin.y = min(max(result.minY, visible.minY), visible.maxY - result.height)
        return result
    }
}
