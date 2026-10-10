import Foundation

/// A verified license. docs/spec/license.md.
public struct License: Sendable, Equatable {
    public let licensee: String
    public let products: Set<Product>
    /// 16 lowercase hex characters: the identity of the license (revocation, ledger, support).
    public let keyID: String
    public let issued: Date
    /// `nil` = updates for life.
    public let updatesUntil: Date?
    public let seats: Int
    public let signingKeyID: Int

    let updatesUntilDay: UInt16

    init(_ payload: LicensePayload) {
        licensee = String(decoding: payload.licensee, as: UTF8.self)
        products = Product.products(in: payload.productMask)
        keyID = payload.keyID.map { String(format: "%02x", $0) }.joined()
        issued = LicensePayload.date(ofDay: payload.issuedDay)
        updatesUntilDay = payload.updatesUntilDay
        updatesUntil = payload.updatesUntilDay == 0 ? nil : LicensePayload.date(ofDay: payload.updatesUntilDay)
        seats = Int(payload.seats)
        signingKeyID = Int(payload.signingKeyID)
    }

    public func covers(_ product: Product) -> Bool { products.contains(product) }

    /// Whether a build made on `date` (UTC day) is within the license's updates. Inclusive.
    public func allowsUpdates(builtOn date: Date) -> Bool {
        guard updatesUntilDay != 0 else { return true }
        let interval = date.timeIntervalSince(LicensePayload.epoch)
        guard interval.isFinite else { return false }
        return LicensePayload.day(of: date) <= Int(updatesUntilDay)
    }
}
