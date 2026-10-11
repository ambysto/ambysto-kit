// From Ambysto Optimer (macos/Tests/OptimerCoreTests/FloatingAndDockTests.swift, ambysto-optimer@549da79)
import CoreGraphics
import Testing
@testable import AmbystoUI

@Suite("FloatingLayout")
struct FloatingLayoutTests {
    let visible = CGRect(x: 0, y: 0, width: 1440, height: 875)   // below a 25 pt menu bar on a 900 pt screen
    let bar = CGSize(width: 330, height: 36)
    let table = CGSize(width: 340, height: 470)

    @Test func firstPlacementIsTheTopRightCorner() {
        let frame = FloatingLayout.initialFrame(size: bar, visible: visible)
        #expect(frame == CGRect(x: 1440 - 16 - 330, y: 875 - 16 - 36, width: 330, height: 36))
    }

    @Test func savedPositionIsReusedOnlyWhileOnAScreen() {
        let onScreen = FloatingLayout.restoredFrame(topLeft: CGPoint(x: 200, y: 600), size: bar, visibleFrames: [visible])
        #expect(onScreen == CGRect(x: 200, y: 564, width: 330, height: 36))
        // A position on a display that is no longer connected.
        #expect(FloatingLayout.restoredFrame(topLeft: CGPoint(x: 2000, y: 600), size: bar, visibleFrames: [visible]) == nil)
        // Too close to the edge (inside the 40 pt inset).
        #expect(FloatingLayout.restoredFrame(topLeft: CGPoint(x: 10, y: 600), size: bar, visibleFrames: [visible]) == nil)
    }

    @Test func expandingKeepsTheTopLeftAwayFromEdges() {
        let frame = CGRect(x: 300, y: 600, width: 330, height: 36)
        let expanded = FloatingLayout.resized(frame, to: table, visible: visible)
        #expect(expanded.minX == 300)
        #expect(expanded.maxY == frame.maxY)
    }

    @Test func expandingNearTheRightEdgeGrowsLeft() {
        let frame = FloatingLayout.initialFrame(size: bar, visible: visible)
        let expanded = FloatingLayout.resized(frame, to: table, visible: visible)
        #expect(expanded.maxX == frame.maxX)
        #expect(expanded.maxY == frame.maxY)
    }

    @Test func expandingNearTheBottomGrowsUpAndStaysOnScreen() {
        let frame = CGRect(x: 300, y: 20, width: 330, height: 36)
        let expanded = FloatingLayout.resized(frame, to: table, visible: visible)
        #expect(expanded.minY == 20)
        #expect(visible.contains(expanded))
    }
}
