import CryptoKit
import Foundation
import Testing
@testable import AmbystoLicense
@testable import LicenseTool

private let vectorsURL = URL(filePath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    .appending(path: "docs/spec/license-test-vectors.json")

private func testSigner() -> LicenseSigner {
    LicenseSigner(privateKey: try! P256.Signing.PrivateKey(rawRepresentation: Hex.decode(TestKey.privateHex)!), signingKeyID: 0)
}

private func issue(_ name: String = "Tester", _ products: Set<Product> = [.space], until: Date? = nil) -> String {
    switch testSigner().issue(licensee: name, products: products, updatesUntil: until) {
    case .success(let value): return value.text
    case .failure(let error): Issue.record("issue failed: \(error)"); return ""
    }
}

private let testVerifier = LicenseVerifier(trusted: .test, revoked: [])

@Suite("Spec vectors")
struct VectorTests {
    @Test func everyVectorGivesItsDocumentedResult() throws {
        let vectors = try JSONDecoder().decode([TestVectors.Vector].self, from: Data(contentsOf: vectorsURL))
        #expect(vectors.count >= 20)
        for vector in vectors {
            let trusted = TrustedKeys(rawHex: Dictionary(uniqueKeysWithValues: vector.trustedKeys.map { (UInt8($0.key)!, $0.value) }))
            let result = LicenseVerifier(trusted: trusted, revoked: Set(vector.revoked)).verify(vector.text)
            switch result {
            case .success(let license):
                #expect(vector.expect.valid, "\(vector.name): verified but should fail with \(vector.expect.error ?? "")")
                #expect(license.licensee == vector.expect.licensee, "\(vector.name)")
                #expect(license.products.map(\.rawValue).sorted() == vector.expect.products, "\(vector.name)")
                #expect(license.keyID == vector.expect.keyID, "\(vector.name)")
                #expect(license.seats == vector.expect.seats, "\(vector.name)")
                #expect(Int(license.updatesUntilDay) == vector.expect.updatesUntilDay, "\(vector.name)")
            case .failure(let error):
                #expect(!vector.expect.valid, "\(vector.name): refused with \(error) but should verify")
                #expect(error.rawValue == vector.expect.error, "\(vector.name): \(error)")
            }
        }
    }

    @Test func theCommittedVectorsMatchTheGeneratorsCases() throws {
        let committed = try JSONDecoder().decode([TestVectors.Vector].self, from: Data(contentsOf: vectorsURL))
        let fresh = TestVectors.generate()
        #expect(committed.map(\.name) == fresh.map(\.name))
        #expect(committed.map(\.expect) == fresh.map(\.expect))
    }
}

@Suite("Text form")
struct TextTests {
    @Test func roundTripsThroughCaseSeparatorsAndLineBreaks() {
        let text = issue()
        #expect(text.hasPrefix("AMB-"))
        let messy = "  " + text.lowercased().replacingOccurrences(of: "-", with: " \n\t") + "\r\n"
        #expect((try? testVerifier.verify(messy).get())?.licensee == "Tester")
        #expect((try? testVerifier.verify(String(text.dropFirst(4))).get()) != nil)   // without the AMB- prefix
    }

    @Test func nonASCIIAndForbiddenSymbolsAreMalformed() {
        let text = issue()
        for bad in [text + "é", text.replacingOccurrences(of: "0", with: "０"), text + "#", text + "U", text.replacingOccurrences(of: "-", with: "_")] {
            #expect(testVerifier.verify(bad) == .failure(.malformed), "\(bad.suffix(8))")
        }
    }

    @Test func invalidLengthsAreMalformed() {
        let text = issue().replacingOccurrences(of: "-", with: "").dropFirst(3)
        for cut in 1 ... 8 {
            let shorter = String(text.dropLast(cut))
            #expect(testVerifier.verify(shorter).isFailure, "cut \(cut)")
        }
    }
}

@Suite("Licenses")
struct LicenseBehaviourTests {
    @Test func aSuiteKeyCoversExactlyItsProducts() throws {
        let license = try testVerifier.verify(issue("Suite", [.optimer, .steady, .space])).get()
        #expect(license.covers(.space) && license.covers(.optimer) && license.covers(.steady))
        #expect(!license.covers(.devbox) && !license.covers(.guard))
    }

    @Test func updatesUntilIsInclusiveAndForLifeAllowsAnyDate() throws {
        let day = Date(timeIntervalSince1970: 1_767_225_600 + 400 * 86_400)
        let limited = try testVerifier.verify(issue(until: day)).get()
        #expect(limited.allowsUpdates(builtOn: day.addingTimeInterval(86_399)))
        #expect(!limited.allowsUpdates(builtOn: day.addingTimeInterval(86_400)))
        let forLife = try testVerifier.verify(issue()).get()
        #expect(forLife.updatesUntil == nil)
        #expect(forLife.allowsUpdates(builtOn: Date(timeIntervalSince1970: 6_000_000_000)))
    }

    @Test func productionDoesNotTrustTheTestKey() {
        #expect(!TrustedKeys.production.ids.contains(0))
        #expect(LicenseVerifier().verify(issue()) == .failure(.unknownSigningKey))
    }

    @Test func aRevokedKeyIsRefused() throws {
        let text = issue()
        let id = try testVerifier.verify(text).get().keyID
        #expect(LicenseVerifier(trusted: .test, revoked: [id]).verify(text) == .failure(.revoked))
    }

    @Test func issuerRejectsBadNamesInsteadOfTruncating() {
        let signer = testSigner()
        #expect(signer.issue(licensee: String(repeating: "á", count: 40), products: [.space]).failure == .licenseeTooLong)
        #expect(signer.issue(licensee: "Evil\u{202E}name", products: [.space]).failure == .licenseeHasControlCharacters)
        #expect(signer.issue(licensee: "Tab\tname", products: [.space]).failure == .licenseeHasControlCharacters)
        #expect(signer.issue(licensee: "", products: [.space]).failure == .licenseeEmpty)
        #expect(signer.issue(licensee: "A", products: []).failure == .noProducts)
        #expect(signer.issue(licensee: "A", products: [.space], updatesUntil: Date(timeIntervalSince1970: 1_767_225_600)).failure == .updatesBeforeIssued)
    }

    @Test func decomposedNamesAreNormalisedToNFC() throws {
        let decomposed = "Nguye\u{0302}\u{0303}n"   // ê + tilde, decomposed
        let license = try testVerifier.verify(issue(decomposed)).get()
        #expect(license.licensee == decomposed.precomposedStringWithCanonicalMapping)
        #expect(Array(license.licensee.utf8) == Array(decomposed.precomposedStringWithCanonicalMapping.utf8))
    }

    @Test func verificationIsFast() {
        let text = issue()
        let start = Date()
        for _ in 0 ..< 50 { _ = testVerifier.verify(text) }
        #expect(Date().timeIntervalSince(start) / 50 < 0.010)
    }

    @Test func aKeyFileIsReadAndAnOversizedFileIsRefused() throws {
        let folder = FileManager.default.temporaryDirectory.appending(path: "kit-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let good = folder.appending(path: "a.ambystokey")
        try Data((issue() + "\n").utf8).write(to: good)
        #expect((try? testVerifier.verify(fileAt: good).get()) != nil)
        let big = folder.appending(path: "b.ambystokey")
        try Data(repeating: 0x41, count: 5_000).write(to: big)
        #expect(testVerifier.verify(fileAt: big) == .failure(.malformed))
    }
}

@Suite("Fuzz")
struct FuzzTests {
    @Test func randomTextNeverCrashesOrVerifies() {
        let symbols = Array("0123456789ABCDEFGHJKMNPQRSTVWXYZabcxyz-OIL U#é\n")
        for _ in 0 ..< 20_000 {
            let text = "AMB-" + String((0 ..< Int.random(in: 0 ... 200)).map { _ in symbols.randomElement()! })
            #expect(testVerifier.verify(text).isFailure)
        }
    }

    @Test func everySingleByteMutationOfTheDecodedKeyFails() {
        let text = issue()
        let bytes = Base32.decodeLicenseText(text)!
        for _ in 0 ..< 20_000 {
            var mutated = bytes
            let i = Int.random(in: 0 ..< mutated.count)
            mutated[i] ^= UInt8.random(in: 1 ... 255)
            #expect(testVerifier.verify(Base32.licenseText(mutated)).isFailure)
        }
    }
}

@Suite("Tool")
struct ToolTests {
    @Test func issuingRecordsTheLedgerBeforePrintingAndPrintsOnlyTheLicense() throws {
        let folder = FileManager.default.temporaryDirectory.appending(path: "kit-tool-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let keyFile = folder.appending(path: "test.key")
        try Hex.decode(TestKey.privateHex)!.write(to: keyFile)
        let ledgerURL = folder.appending(path: "ledger.csv")
        let printed = Box()
        var commands = Commands(store: FileStore(url: keyFile), ledger: Ledger(url: ledgerURL))
        commands.out = { line in
            printed.lines.append(line)
            printed.ledgerExistedFirst = FileManager.default.fileExists(atPath: ledgerURL.path)
        }
        try commands.run("issue", Arguments(["--licensee", "=cmd|' /C calc'!A0", "--products", "space,optimer", "--id", "0"]))
        #expect(printed.lines.count == 1)
        #expect(printed.ledgerExistedFirst)
        let license = try testVerifier.verify(printed.lines[0]).get()
        #expect(license.products == [.space, .optimer])
        let ledger = try String(contentsOf: ledgerURL, encoding: .utf8)
        #expect(ledger.contains(license.keyID))
        #expect(ledger.contains("\"'=cmd|' /C calc'!A0\""))   // formula neutralised and quoted
        #expect(!printed.lines[0].contains(TestKey.privateHex))
        #expect(FileManager.default.fileExists(atPath: folder.appending(path: "\(license.keyID).ambystokey").path))
    }

    @Test func theFileStoreRefusesProductionKeyIDs() throws {
        let store = FileStore(url: FileManager.default.temporaryDirectory.appending(path: "nope-\(UUID().uuidString)"))
        #expect(throws: ToolError.self) { try store.store(Data(repeating: 1, count: 32), id: 1) }
        #expect(throws: ToolError.self) { try store.read(id: 1) }
    }

    @Test func theBackupMustGoToAnExternalDriveOutsideAGitTree() {
        #expect(throws: ToolError.self) { try BackupImage.checkBackupPath(NSHomeDirectory() + "/key.dmg") }
        #expect(throws: ToolError.self) { try BackupImage.checkBackupPath("/Volumes/Macintosh HD/key.dmg") }
        #expect(throws: ToolError.self) { try BackupImage.checkBackupPath("/tmp/key.dmg") }
    }

    @Test func csvFieldsAreQuotedAndFormulasNeutralised() {
        #expect(Ledger.csvField("Nguyễn \"A\"") == "\"Nguyễn \"\"A\"\"\"")
        #expect(Ledger.csvField("+1") == "\"'+1\"")
        #expect(Ledger.csvField("@x") == "\"'@x\"")
    }
}

private final class Box: @unchecked Sendable {
    var lines: [String] = []
    var ledgerExistedFirst = false
}

extension Result {
    var isFailure: Bool { if case .failure = self { true } else { false } }
}

extension Result where Failure == LicenseSigner.InputError {
    var failure: LicenseSigner.InputError? { if case .failure(let error) = self { error } else { nil } }
}
