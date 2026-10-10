import Darwin
import Foundation

/// Every issued license, one CSV line each, kept outside the repo so a key can be revoked later (intent §3).
struct Ledger {
    static var defaultURL: URL {
        FileManager.default.homeDirectoryForCurrentUser.appending(path: "Library/Application Support/Ambysto License/ledger.csv")
    }

    static let header = "key_id,issued_utc,licensee,products,seats,updates_until,signing_key_id\n"

    let url: URL

    /// Appends and fsyncs before the caller prints anything.
    func append(keyID: String, issued: Date, licensee: String, products: [String], seats: Int, updatesUntil: Date?, signingKeyID: UInt8) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true,
                                                attributes: [.posixPermissions: 0o700])
        let isNew = !FileManager.default.fileExists(atPath: url.path)
        let fd = open(url.path, O_WRONLY | O_CREAT | O_APPEND, 0o600)
        guard fd >= 0 else { throw ToolError("Could not open the ledger at \(url.path).") }
        defer { close(fd) }
        let day = ISO8601DateFormatter()
        day.formatOptions = [.withFullDate]
        day.timeZone = TimeZone(identifier: "UTC")
        let fields = [keyID, day.string(from: issued), licensee, products.joined(separator: " "), String(seats),
                      updatesUntil.map(day.string(from:)) ?? "for life", String(signingKeyID)]
        let line = (isNew ? Self.header : "") + fields.map(Self.csvField).joined(separator: ",") + "\n"
        let bytes = Array(line.utf8)
        guard write(fd, bytes, bytes.count) == bytes.count, fsync(fd) == 0 else { throw ToolError("Could not write the ledger.") }
    }

    /// Quoted, with spreadsheet formula characters neutralised.
    static func csvField(_ value: String) -> String {
        var text = value
        if let first = text.first, "=+-@".contains(first) { text = "'" + text }
        return "\"" + text.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }
}
