import Foundation

/// The suite catalog (spec `suite-bar.md`, "The suite catalog"). Case order is the display order.
/// The ids are wire and system identifiers and never follow a rename: `space` is shown as Lean, `guard` as Gate.
public enum SuiteApp: String, CaseIterable, Sendable, Codable, Comparable {
    case optimer
    case steady
    case space
    case devbox
    case power
    case tune
    case `guard`

    public var bundleIdentifier: String { "com.ambysto.\(rawValue)" }

    public var displayName: String {
        switch self {
        case .optimer: "Ambysto Optimer"
        case .steady: "Ambysto Steady"
        case .space: "Ambysto Lean"
        case .devbox: "Ambysto DevBox"
        case .power: "Ambysto Power"
        case .tune: "Ambysto Tune"
        case .guard: "Ambysto Gate"
        }
    }

    /// The name without the brand, for compact rows.
    public var shortName: String { String(displayName.dropFirst("Ambysto ".count)) }

    /// Counts towards "active" and appears in the readout (contract 1). Lean and Tune run only while open.
    public var participates: Bool {
        switch self {
        case .space, .tune: false
        default: true
        }
    }

    /// May hold the host lock and show the readout. Steady is a window app; Lean and Tune do not participate.
    public var canHost: Bool {
        switch self {
        case .optimer, .devbox, .power, .guard: true
        case .steady, .space, .tune: false
        }
    }

    /// The glance file name, `<app id>.json`.
    public var glanceFileName: String { "\(rawValue).json" }

    public static func < (lhs: SuiteApp, rhs: SuiteApp) -> Bool {
        allCases.firstIndex(of: lhs)! < allCases.firstIndex(of: rhs)!
    }
}
