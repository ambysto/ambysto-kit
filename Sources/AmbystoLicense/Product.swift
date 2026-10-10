/// The Ambysto product registry. Bit positions are part of the license format (docs/spec/license.md) and never change.
/// A new product is added by a minor Kit release; verifiers ignore bits they do not know.
public enum Product: String, CaseIterable, Sendable, Hashable {
    case optimer, steady, space, devbox, power, tune
    case `guard`

    public var bit: Int {
        switch self {
        case .optimer: 0
        case .steady: 1
        case .space: 2
        case .devbox: 3
        case .power: 4
        case .tune: 5
        case .guard: 6
        }
    }

    public var displayName: String { "Ambysto " + rawValue.prefix(1).uppercased() + rawValue.dropFirst() }

    static func products(in mask: UInt32) -> Set<Product> {
        Set(allCases.filter { mask & (1 << UInt32($0.bit)) != 0 })
    }

    static func mask(_ products: some Sequence<Product>) -> UInt32 {
        products.reduce(0) { $0 | (1 << UInt32($1.bit)) }
    }
}
