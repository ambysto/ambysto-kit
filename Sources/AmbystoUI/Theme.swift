// From Ambysto Optimer (macos/Sources/AmbystoOptimer/Views/Theme.swift, ambysto-optimer@549da79); Terminal tokens: APS-16
import AmbystoSuite
import AppKit
import SwiftUI

/// The Terminal frame of the suite (APS-16): near-black ground, off-white ink, hairlines instead of shadows, radius 6
/// to 8, SF Mono for readouts. Each app has one tone (`tone(_:)`), identity only, never a state. Only the suite
/// bar's floating bar is always dark (`Terminal`); `accent` and `secondary` follow the system appearance.
/// Colour is never the only signal: `glyph(_:)` adds a symbol for non-ok levels.
public enum Theme {
    /// The Terminal neutrals. Fixed: they never adapt to the appearance (the floating bar is always dark).
    public enum Terminal {
        static let groundHex: UInt32 = 0x0B0B0C
        static let panelHex: UInt32 = 0x101011
        static let inkHex: UInt32 = 0xE8E8E6
        static let muteHex: UInt32 = 0x8A8A8C
        static let ruleHex: UInt32 = 0x222224
        static let dimHex: UInt32 = 0x3A3A3C

        public static let ground = Color(hex: groundHex)   // floating bar ground #0B0B0C
        public static let panel = Color(hex: panelHex)     // one step above the ground #101011
        public static let ink = Color(hex: inkHex)         // text on the ground #E8E8E6
        public static let mute = Color(hex: muteHex)       // secondary text on the ground #8A8A8C
        public static let rule = Color(hex: ruleHex)       // hairlines on the ground #222224
        public static let dim = Color(hex: dimHex)         // unlit cells, leaders #3A3A3C
    }

    // Light and dark values of the two adaptive colours (ink and mute of the appearance in force).
    static let accentLightHex: UInt32 = 0x0B0B0C
    static let accentDarkHex: UInt32 = 0xE8E8E6
    static let secondaryLightHex: UInt32 = 0x5F5F62
    static let secondaryDarkHex: UInt32 = 0x8A8A8C

    static let accentNSColor = adaptive(light: accentLightHex, dark: accentDarkHex)
    static let secondaryNSColor = adaptive(light: secondaryLightHex, dark: secondaryDarkHex)

    private static func adaptive(light: UInt32, dark: UInt32) -> NSColor {
        NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? NSColor(hex: dark) : NSColor(hex: light)
        }
    }

    /// Ink: near-black in light, off-white in dark. No longer a colour to put white text on.
    public static let accent = Color(nsColor: accentNSColor)
    /// Muted ink: #5F5F62 in light, #8A8A8C in dark.
    public static let secondary = Color(nsColor: secondaryNSColor)
    public static let ink = Terminal.ground                                      // dark tile / floating bar #0B0B0C
    public static let page = Color(nsColor: .windowBackgroundColor)
    public static let card = Color(nsColor: .controlBackgroundColor)
    public static let hairline = Color.primary.opacity(0.08)

    public static let controlRadius: CGFloat = 6
    public static let panelRadius: CGFloat = 8
    public static let cardRadius: CGFloat = panelRadius
    public static let cardPadding: CGFloat = 20
    public static let hairlineWidth: CGFloat = 1

    /// SF Mono for readouts: the text style's font in its monospaced design. Built from the style's font rather than
    /// the system-font factory, so the suite bar grep list (which includes the libc call's name) stays at zero hits.
    public static func mono(_ style: Font.TextStyle, weight: Font.Weight = .regular) -> Font {
        let base: Font = switch style {
        case .largeTitle: .largeTitle
        case .title: .title
        case .title2: .title2
        case .title3: .title3
        case .headline: .headline
        case .subheadline: .subheadline
        case .body: .body
        case .callout: .callout
        case .footnote: .footnote
        case .caption: .caption
        case .caption2: .caption2
        @unknown default: .body
        }
        return base.monospaced().weight(weight)
    }

    /// An app's tone: identity only (a name, a bar, one border). Never a fill, never a state. Tuned for the dark
    /// Terminal ground; on a light ground it falls to about 2:1, so text there is not set in a tone.
    public static func tone(_ app: SuiteApp) -> Color {
        Color(hex: toneHex(app))
    }

    /// The seven tones as RGB hex. No `default`: a new catalog case fails to compile until it has a tone.
    static func toneHex(_ app: SuiteApp) -> UInt32 {
        switch app {
        case .optimer: 0xB0ADFF
        case .steady: 0x62C4F5
        case .space: 0xF99A8F       // shown as Lean
        case .devbox: 0x4ECFBD
        case .power: 0x9FC675
        case .tune: 0xDFAE5B
        case .gate: 0xE69AD4
        }
    }

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

extension Color {
    init(hex: UInt32) {
        self.init(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255, opacity: 1)
    }
}

extension NSColor {
    convenience init(hex: UInt32) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
}
