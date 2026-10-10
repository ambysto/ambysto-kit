import Darwin

/// Reads a passphrase from the terminal without echo (`readpassphrase`). Bytes only: never a String in logs.
enum Passphrase {
    static let minimumLength = 12

    static func read(_ prompt: String) throws -> [UInt8] {
        var buffer = [CChar](repeating: 0, count: 1_024)
        guard readpassphrase(prompt, &buffer, buffer.count, RPP_REQUIRE_TTY) != nil else {
            throw ToolError("Could not read a passphrase (run this in a terminal).")
        }
        defer { for i in buffer.indices { buffer[i] = 0 } }
        return buffer.prefix { $0 != 0 }.map { UInt8(bitPattern: $0) }
    }

    static func readNew() throws -> [UInt8] {
        let first = try read("Backup passphrase (at least \(minimumLength) characters): ")
        guard first.count >= minimumLength else { throw ToolError("The passphrase is shorter than \(minimumLength) characters.") }
        var second = try read("Repeat the passphrase: ")
        defer { second.resetBytes() }
        guard first == second else { throw ToolError("The passphrases differ; nothing was created.") }
        return first
    }
}

extension Array where Element == UInt8 {
    /// Overwrites the bytes before the array is released (Swift cannot wipe copies made elsewhere).
    mutating func resetBytes() { for i in indices { self[i] = 0 } }
}
