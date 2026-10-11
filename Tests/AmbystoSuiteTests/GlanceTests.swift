import Foundation
import Testing
@testable import AmbystoSuite

/// The spec's vectors (`docs/spec/suite-bar-vectors.json`), so macOS and Windows test the same contract.
struct Vectors: Decodable {
    struct Record: Decodable {
        let name: String
        let file: String
        let outcome: String
        let text: String
        let rule: String?
    }
    struct Freshness: Decodable {
        let name: String
        let at: Double
        let ttl: Int
        let now: Double
        let fresh: Bool
    }
    let records: [Record]
    let freshness: [Freshness]

    static let shared: Vectors = {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("docs/spec/suite-bar-vectors.json")
        return try! JSONDecoder().decode(Vectors.self, from: Data(contentsOf: url))
    }()
}

@Suite("Glance vectors")
struct GlanceVectorTests {
    @Test(arguments: Vectors.shared.records.map(\.name))
    func vector(_ name: String) {
        let vector = Vectors.shared.records.first { $0.name == name }!
        let result = GlanceCodec.decode(Data(vector.text.utf8), fileName: vector.file)
        switch (vector.outcome, result) {
        case ("valid", .valid(let record)):
            // A valid record survives an encode and a decode unchanged.
            let again = GlanceCodec.decode(try! GlanceCodec.encode(record), fileName: vector.file)
            #expect(again == .valid(record))
        case ("skipped", .skipped):
            break
        case ("invalid", .invalid(let reason)):
            #expect(reason == vector.rule, "\(name)")
        default:
            Issue.record("\(name): expected \(vector.outcome), got \(result)")
        }
    }

    @Test(arguments: Vectors.shared.freshness.map(\.name))
    func freshness(_ name: String) {
        let vector = Vectors.shared.freshness.first { $0.name == name }!
        let record = GlanceRecord(app: .optimer, at: vector.at, ttl: vector.ttl)
        #expect(record.isFresh(now: vector.now) == vector.fresh)
    }

    @Test func coversEveryInvalidRuleOnce() {
        #expect(Vectors.shared.records.filter { $0.outcome == "invalid" }.count >= 25)
    }
}

@Suite("Glance codec")
struct GlanceCodecTests {
    @Test func stoppedIsNeverFresh() {
        let record = GlanceRecord(app: .power, state: .stopped, at: 100, ttl: 10)
        #expect(!record.isFresh(now: 100))
    }

    @Test func publisherRefusesInvalidRecords() {
        let record = GlanceRecord(app: .devbox, at: 1, metrics: [GlanceMetric(id: "srv", label: "/Users/me", value: 3)])
        #expect(throws: GlanceValidationError.self) { try GlanceCodec.encode(record) }
    }

    @Test func numbersWithTwoDecimalsAreAccepted() throws {
        for value in [0.07, 12.25, 99.99, -3.5, 1_000_000] {
            let record = GlanceRecord(app: .optimer, at: 1, metrics: [GlanceMetric(id: "v", label: "V", value: value)])
            _ = try GlanceCodec.encode(record)
        }
    }

    @Test func booleanIsNotANumber() {
        let text = #"{"contract":1,"app":"optimer","hosting":false,"state":"running","at":true,"ttl":10,"level":"ok","metrics":[]}"#
        #expect(GlanceCodec.decode(Data(text.utf8), fileName: "optimer.json") == .invalid(reason: "at: required number"))
    }

    @Test func sameContentIgnoresTheTime() {
        let a = GlanceRecord(app: .optimer, at: 1, metrics: [GlanceMetric(id: "m", label: "M", value: 1)])
        var b = a
        b.at = 99
        #expect(a.sameContent(as: b))
        b.metrics[0].value = .number(2)
        #expect(!a.sameContent(as: b))
    }

    @Test func catalogOrderAndRoles() {
        #expect(SuiteApp.allCases.map(\.rawValue) == ["optimer", "steady", "space", "devbox", "power", "tune", "guard"])
        #expect(SuiteApp.allCases.filter(\.canHost) == [.optimer, .devbox, .power, .guard])
        #expect(SuiteApp.allCases.filter { !$0.participates } == [.space, .tune])
        #expect(SuiteApp.guard.bundleIdentifier == "com.ambysto.guard")
    }
}
