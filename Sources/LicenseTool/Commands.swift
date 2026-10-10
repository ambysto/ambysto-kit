import AmbystoLicense
import CryptoKit
import Foundation

/// stdout carries only results; every reminder goes to stderr (plan §4).
func note(_ text: String) {
    FileHandle.standardError.write(Data((text + "\n").utf8))
}

func emit(_ text: String) {
    FileHandle.standardOutput.write(Data((text + "\n").utf8))
}

struct Arguments {
    var positional: [String] = []
    var options: [String: String] = [:]

    init(_ args: some Sequence<String>) {
        var iterator = args.makeIterator()
        while let arg = iterator.next() {
            if arg.hasPrefix("--") {
                options[String(arg.dropFirst(2))] = iterator.next() ?? ""
            } else {
                positional.append(arg)
            }
        }
    }

    func value(_ name: String) -> String? { options[name] }
}

struct Commands {
    var store: any SigningKeyStore
    var ledger: Ledger
    /// Where results go: stdout in the tool, a capture in tests.
    var out: (String) -> Void = emit

    static let usage = """
    ambysto-license — issue Ambysto license keys (owner's Mac only)

      new-signing-key --backup /Volumes/<drive>/ambysto-signing-key.dmg [--id 1]
      import-signing-key <backup.dmg> [--id 1]
      issue --licensee <name> --products space[,optimer,…] [--seats 3] [--updates-until YYYY-MM-DD] [--id 1]
      backup-ledger <backup.dmg>
      public-key [--id 1]
      scan-for-key <repo path> [--id 1]
      test-vectors --out <file.json>
    """

    func run(_ command: String, _ args: Arguments) throws {
        let id = UInt8(args.value("id") ?? "1") ?? 1
        switch command {
        case "new-signing-key": try newSigningKey(id: id, backup: args.value("backup"))
        case "import-signing-key": try importSigningKey(id: id, image: args.positional.first)
        case "issue": try issue(id: id, args)
        case "backup-ledger": try backupLedger(image: args.positional.first)
        case "public-key": out(Hex.encode(try privateKey(id).publicKey.rawRepresentation))
        case "scan-for-key": try scan(id: id, path: args.positional.first)
        case "test-vectors":
            guard let path = args.value("out") else { throw ToolError("test-vectors needs --out <file.json>.") }
            try TestVectors.json().write(to: URL(filePath: path))
            out(path)
        default: throw ToolError(Self.usage)
        }
    }

    func privateKey(_ id: UInt8) throws -> P256.Signing.PrivateKey {
        if store is KeychainStore {
            note("macOS will ask for your login password. Choose “Allow”, not “Always Allow”.")
        }
        let raw = try store.read(id: id)
        return try P256.Signing.PrivateKey(rawRepresentation: raw)
    }

    func newSigningKey(id: UInt8, backup: String?) throws {
        guard let backup else { throw ToolError("new-signing-key needs --backup /Volumes/<drive>/ambysto-signing-key.dmg") }
        guard !store.exists(id: id) else { throw ToolError("A signing key with id \(id) already exists; nothing was changed.") }
        try BackupImage.checkBackupPath(backup)
        let key = P256.Signing.PrivateKey()
        let passphrase = try Passphrase.readNew()
        let keyFile = Data("id \(id)\n\(Hex.encode(key.rawRepresentation))\n".utf8)
        try BackupImage.create(at: backup, passphrase: passphrase, contents: [BackupImage.keyFileName: keyFile])
        try store.store(key.rawRepresentation, id: id)
        note("Backup written to \(backup). Keep that drive apart from this Mac.")
        note("Public key for TrustedKeys.production (id \(id)):")
        out(Hex.encode(key.publicKey.rawRepresentation))
    }

    func importSigningKey(id: UInt8, image: String?) throws {
        guard let image else { throw ToolError("import-signing-key needs the backup .dmg path.") }
        let passphrase = try Passphrase.read("Backup passphrase: ")
        let text = try BackupImage.withMounted(image, passphrase: passphrase, readOnly: true) { mount in
            try String(contentsOf: mount.appending(path: BackupImage.keyFileName), encoding: .utf8)
        }
        let lines = text.split(separator: "\n")
        guard lines.count >= 2, lines[0] == "id \(id)", let raw = Hex.decode(String(lines[1])),
              let key = try? P256.Signing.PrivateKey(rawRepresentation: raw) else {
            throw ToolError("The backup does not hold signing key id \(id).")
        }
        try store.store(key.rawRepresentation, id: id)
        note("Signing key \(id) imported into this Mac's Keychain.")
        out(Hex.encode(key.publicKey.rawRepresentation))
    }

    func issue(id: UInt8, _ args: Arguments) throws {
        guard let licensee = args.value("licensee") else { throw ToolError("issue needs --licensee <name>.") }
        guard let list = args.value("products") else { throw ToolError("issue needs --products space[,optimer,…].") }
        var products = Set<Product>()
        for name in list.split(separator: ",").map({ $0.trimmingCharacters(in: .whitespaces) }) {
            guard let product = Product(rawValue: name) else {
                throw ToolError("Unknown product \(name). Known: \(Product.allCases.map(\.rawValue).joined(separator: ", ")).")
            }
            products.insert(product)
        }
        let seats = Int(args.value("seats") ?? "3") ?? -1
        var until: Date?
        if let text = args.value("updates-until") {
            let format = ISO8601DateFormatter()
            format.formatOptions = [.withFullDate]
            format.timeZone = TimeZone(identifier: "UTC")
            guard let date = format.date(from: text) else { throw ToolError("--updates-until must be YYYY-MM-DD.") }
            until = date
        }
        let issued = Date()
        let signer = LicenseSigner(privateKey: try privateKey(id), signingKeyID: id)
        let result: (text: String, keyID: String)
        switch signer.issue(licensee: licensee, products: products, seats: seats, updatesUntil: until, issued: issued) {
        case .failure(let error): throw ToolError(error.description)
        case .success(let value): result = value
        }
        let trusted = TrustedKeys(rawHex: [id: Hex.encode(signer.publicKeyRaw)])
        guard case .success = LicenseVerifier(trusted: trusted, revoked: []).verify(result.text) else {
            throw ToolError("The new key did not verify; nothing was recorded.")
        }
        try ledger.append(keyID: result.keyID, issued: issued, licensee: licensee, products: products.map(\.rawValue).sorted(),
                          seats: seats, updatesUntil: until, signingKeyID: id)
        let file = ledger.url.deletingLastPathComponent().appending(path: "\(result.keyID).ambystokey")
        try Data((result.text + "\n").utf8).write(to: file, options: .atomic)
        note("Recorded in the ledger. Key file: \(file.path)")
        out(result.text)
    }

    func backupLedger(image: String?) throws {
        guard let image else { throw ToolError("backup-ledger needs the backup .dmg path.") }
        let data = try Data(contentsOf: ledger.url)
        let passphrase = try Passphrase.read("Backup passphrase: ")
        try BackupImage.withMounted(image, passphrase: passphrase, readOnly: false) { mount in
            try BackupImage.writePrivate(data, to: mount.appending(path: "ledger.csv"))
        }
        note("Ledger copied into the backup.")
    }

    /// Prints only "found" or "not found": the key's hex, base64 and raw forms in the work tree and the full history.
    func scan(id: UInt8, path: String?) throws {
        guard let path else { throw ToolError("scan-for-key needs a repository path.") }
        let raw = try privateKey(id).rawRepresentation
        let needles = [Data(Hex.encode(raw).utf8), Data(Hex.encode(raw).uppercased().utf8), Data(raw.base64EncodedString().utf8), raw]
        var found = false
        if let enumerator = FileManager.default.enumerator(at: URL(filePath: path), includingPropertiesForKeys: [.isRegularFileKey]) {
            for case let url as URL in enumerator where !found {
                guard (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true,
                      let data = try? Data(contentsOf: url, options: .mappedIfSafe) else { continue }
                found = needles.contains { data.range(of: $0) != nil }
            }
        }
        if !found {
            let git = Process()
            git.executableURL = URL(filePath: "/usr/bin/git")
            git.arguments = ["-C", path, "log", "--all", "-p", "--text", "--no-color"]
            let pipe = Pipe()
            git.standardOutput = pipe
            git.standardError = FileHandle.nullDevice
            try git.run()
            let history = pipe.fileHandleForReading.readDataToEndOfFile()
            git.waitUntilExit()
            found = needles.contains { history.range(of: $0) != nil }
        }
        out(found ? "found" : "not found")
    }
}
