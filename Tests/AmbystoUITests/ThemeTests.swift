import AmbystoSuite
import AppKit
import SwiftUI
import Testing
@testable import AmbystoUI

// APS-16: the Terminal tokens are pinned here, and the contrast claims are computed, not copied from the brand file.

/// WCAG relative luminance of an sRGB hex colour.
private func luminance(_ hex: UInt32) -> Double {
    func channel(_ shift: UInt32) -> Double {
        let value = Double((hex >> shift) & 0xFF) / 255
        return value <= 0.03928 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4)
    }
    return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
}

private func contrast(_ a: UInt32, _ b: UInt32) -> Double {
    let (high, low) = (max(luminance(a), luminance(b)), min(luminance(a), luminance(b)))
    return (high + 0.05) / (low + 0.05)
}

/// The sRGB bytes a colour resolves to under an appearance.
private func resolved(_ color: NSColor, _ name: NSAppearance.Name) -> UInt32 {
    var result: UInt32 = 0
    NSAppearance(named: name)!.performAsCurrentDrawingAppearance {
        let srgb = color.usingColorSpace(.sRGB)!
        let red = UInt32((srgb.redComponent * 255).rounded())
        let green = UInt32((srgb.greenComponent * 255).rounded())
        let blue = UInt32((srgb.blueComponent * 255).rounded())
        result = red << 16 | green << 8 | blue
    }
    return result
}

@Suite("Theme")
struct ThemeTests {
    @Test func terminalNeutralsArePinned() {
        #expect(Theme.Terminal.groundHex == 0x0B0B0C)
        #expect(Theme.Terminal.panelHex == 0x101011)
        #expect(Theme.Terminal.inkHex == 0xE8E8E6)
        #expect(Theme.Terminal.muteHex == 0x8A8A8C)
        #expect(Theme.Terminal.ruleHex == 0x222224)
        #expect(Theme.Terminal.dimHex == 0x3A3A3C)
    }

    @Test func tonesArePinnedAndDistinct() {
        let expected: [SuiteApp: UInt32] = [
            .optimer: 0xB0ADFF, .steady: 0x62C4F5, .space: 0xF99A8F, .devbox: 0x4ECFBD,
            .power: 0x9FC675, .tune: 0xDFAE5B, .guard: 0xE69AD4,
        ]
        #expect(Set(SuiteApp.allCases) == Set(expected.keys))
        for app in SuiteApp.allCases { #expect(Theme.toneHex(app) == expected[app], "\(app)") }
        #expect(Set(SuiteApp.allCases.map { Theme.toneHex($0) }).count == SuiteApp.allCases.count)
    }

    @Test func colorsResolveToTheirHex() {
        #expect(resolved(NSColor(Theme.Terminal.ground), .darkAqua) == 0x0B0B0C)
        #expect(resolved(NSColor(Theme.tone(.optimer)), .darkAqua) == 0xB0ADFF)
        #expect(resolved(NSColor(Theme.ink), .aqua) == 0x0B0B0C)
    }

    @Test func everyToneReadsOnTheGround() {
        for app in SuiteApp.allCases {
            #expect(contrast(Theme.toneHex(app), Theme.Terminal.groundHex) >= 7, "\(app)")
        }
    }

    @Test func neutralsAndAdaptiveTextReadOnTheirGrounds() {
        #expect(contrast(Theme.Terminal.muteHex, Theme.Terminal.groundHex) >= 4.5)
        #expect(contrast(Theme.Terminal.muteHex, Theme.Terminal.panelHex) >= 4.5)
        #expect(contrast(Theme.Terminal.inkHex, Theme.Terminal.groundHex) >= 12)
        #expect(contrast(Theme.secondaryLightHex, 0xFFFFFF) >= 4.5)
        #expect(contrast(Theme.secondaryLightHex, 0xECECEC) >= 4.5)   // the light window background
        #expect(contrast(Theme.secondaryDarkHex, Theme.Terminal.groundHex) >= 4.5)
    }

    @Test func adaptiveColorsFollowTheAppearance() {
        #expect(resolved(Theme.accentNSColor, .aqua) == 0x0B0B0C)
        #expect(resolved(Theme.accentNSColor, .darkAqua) == 0xE8E8E6)
        #expect(resolved(Theme.secondaryNSColor, .aqua) == 0x5F5F62)
        #expect(resolved(Theme.secondaryNSColor, .darkAqua) == 0x8A8A8C)
        // The public SwiftUI colours, not only their AppKit sources.
        #expect(resolved(NSColor(Theme.accent), .aqua) == 0x0B0B0C)
        #expect(resolved(NSColor(Theme.accent), .darkAqua) == 0xE8E8E6)
        #expect(resolved(NSColor(Theme.secondary), .aqua) == 0x5F5F62)
        #expect(resolved(NSColor(Theme.secondary), .darkAqua) == 0x8A8A8C)
    }

    @Test func radiiAreTheTerminalOnes() {
        #expect(Theme.cardRadius == Theme.panelRadius)
        #expect(Theme.panelRadius == 8)
        #expect(Theme.controlRadius == 6)
        #expect(Theme.hairlineWidth == 1)
    }
}
