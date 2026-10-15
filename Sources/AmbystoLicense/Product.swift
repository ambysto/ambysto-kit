/// The Ambysto product registry. Bit positions are part of the license format (docs/spec/license.md) and never change.
/// A new product is added by a minor Kit release; verifiers ignore bits they do not know.
public enum Product: String, CaseIterable, Sendable, Hashable {
    case optimer, steady, space, devbox, power, tune
    case gate

    public var bit: Int {
        switch self {
        case .optimer: 0
        case .steady: 1
        case .space: 2
        case .devbox: 3
        case .power: 4
        case .tune: 5
        case .gate: 6
        }
    }

    /// The name users see (APS-17). Ids and bits are fixed from the first release: `space` is shown as Lean;
    /// `gate` was `guard` until 2026-10-15, before any release (APS-20).
    public var displayName: String {
        switch self {
        case .optimer: "Ambysto Optimer"
        case .steady: "Ambysto Steady"
        case .space: "Ambysto Lean"
        case .devbox: "Ambysto DevBox"
        case .power: "Ambysto Power"
        case .tune: "Ambysto Tune"
        case .gate: "Ambysto Gate"
        }
    }

    static func products(in mask: UInt32) -> Set<Product> {
        Set(allCases.filter { mask & (1 << UInt32($0.bit)) != 0 })
    }

    static func mask(_ products: some Sequence<Product>) -> UInt32 {
        products.reduce(0) { $0 | (1 << UInt32($1.bit)) }
    }
}
