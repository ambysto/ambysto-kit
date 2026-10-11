// From Ambysto Optimer (macos/Sources/AmbystoOptimer/Views/Theme.swift, ambysto-optimer@549da79)
import AmbystoSuite
import AppKit
import SwiftUI

/// Ambysto palette: a violet primary and an axolotl-coral secondary (the name nods to *Ambystoma*), rounded cards,
/// and the level colours of the readout. Colour is never the only signal: `glyph(_:)` adds a symbol for non-ok levels.
public enum Theme {
    public static let accent = Color(red: 0.42, green: 0.36, blue: 0.91)        // Ambysto violet #6B5CE8
    public static let secondary = Color(red: 0.93, green: 0.44, blue: 0.56)     // axolotl coral #ED708F
    public static let ink = Color(red: 0.11, green: 0.10, blue: 0.20)            // dark tile / floating bar #1C1A33
    public static let page = Color(nsColor: .windowBackgroundColor)
    public static let card = Color(nsColor: .controlBackgroundColor)
    public static let hairline = Color.primary.opacity(0.08)

    public static let cardRadius: CGFloat = 14
    public static let cardPadding: CGFloat = 20

    // Optimer's rating colours: great (ok), fair (notice), poor (alert).
    private static let okRGB = (0.13, 0.66, 0.42)
    private static let noticeRGB = (0.93, 0.62, 0.10)
    private static let alertRGB = (0.86, 0.24, 0.22)

    private static func rgb(_ level: GlanceLevel) -> (Double, Double, Double) {
        switch level {
        case .ok: okRGB
        case .notice: noticeRGB
        case .alert: alertRGB
        }
    }

    public static func color(_ level: GlanceLevel) -> Color {
        let (red, green, blue) = rgb(level)
        return Color(red: red, green: green, blue: blue)
    }

    public static func nsColor(_ level: GlanceLevel) -> NSColor {
        let (red, green, blue) = rgb(level)
        return NSColor(red: red, green: green, blue: blue, alpha: 1)
    }

    /// An SF Symbol name for a level that is not ok, so colour is never the only signal; `nil` for ok.
    public static func glyph(_ level: GlanceLevel) -> String? {
        switch level {
        case .ok: nil
        case .notice: "exclamationmark.circle"
        case .alert: "exclamationmark.triangle.fill"
        }
    }
}
