import CoreFoundation
import Foundation

/// An app's or a metric's state (spec: `ok`, `notice`, `alert`). Ordered from best to worst.
public enum GlanceLevel: String, CaseIterable, Sendable, Codable, Comparable {
    case ok, notice, alert

    public static func < (lhs: GlanceLevel, rhs: GlanceLevel) -> Bool {
        allCases.firstIndex(of: lhs)! < allCases.firstIndex(of: rhs)!
    }
}

public enum GlanceState: String, Sendable, Codable {
    case running, stopped
}

/// The closed list of words a metric may show instead of a number. Nothing else fits, so no name can.
public enum StateWord: String, CaseIterable, Sendable, Codable {
    case on, off, active, idle, paused, blocked, allowed, ok, warning, error, none
}

public enum MetricValue: Sendable, Equatable {
    case number(Double)
    case state(StateWord)
}

public struct GlanceMetric: Sendable, Equatable {
    public var id: String
    /// A fixed string from the app's code, in the app's language. Never a name of anything on the machine.
    public var label: String
    public var value: MetricValue
    public var unit: String
    public var level: GlanceLevel

    public init(id: String, label: String, value: MetricValue, unit: String = "", level: GlanceLevel = .ok) {
        self.id = id
        self.label = label
        self.value = value
        self.unit = unit
        self.level = level
    }

    public init(id: String, label: String, value: Double, unit: String = "", level: GlanceLevel = .ok) {
        self.init(id: id, label: label, value: .number(value), unit: unit, level: level)
    }

    public init(id: String, label: String, state: StateWord, level: GlanceLevel = .ok) {
        self.init(id: id, label: label, value: .state(state), unit: "", level: level)
    }
}

/// One app's glance record (spec "The glance record").
public struct GlanceRecord: Sendable, Equatable {
    public static let contract = 1

    public var contract: Int
    public var app: SuiteApp
    public var hosting: Bool
    public var state: GlanceState
    /// Seconds since 1970, when written.
    public var at: Double
    public var ttl: Int
    public var level: GlanceLevel
    public var metrics: [GlanceMetric]

    public init(app: SuiteApp, hosting: Bool = false, state: GlanceState = .running, at: Double, ttl: Int = 10,
                level: GlanceLevel = .ok, metrics: [GlanceMetric] = [], contract: Int = GlanceRecord.contract) {
        self.contract = contract
        self.app = app
        self.hosting = hosting
        self.state = state
        self.at = at
        self.ttl = ttl
        self.level = level
        self.metrics = metrics
    }

    /// Spec "Freshness": a running record written at most 2 s in the future and at most `ttl` seconds ago.
    public func isFresh(now: Double) -> Bool {
        state == .running && at - GlanceRecord.futureTolerance <= now && now <= at + Double(ttl)
    }

    /// The same content apart from the time it was written: a publisher writes only when this changes (or for a heartbeat).
    public func sameContent(as other: GlanceRecord) -> Bool {
        var copy = other
        copy.at = at
        return copy == self
    }

    public static let futureTolerance: Double = 2
    public static let ttlRange = 3...60
    public static let maxMetrics = 4
    public static let maxBytes = 2048
}

public enum GlanceReadResult: Sendable, Equatable {
    case valid(GlanceRecord)
    /// A higher contract major: not shown, not an error.
    case skipped(contract: Int)
    case invalid(reason: String)
}

public struct GlanceValidationError: Error, Sendable, Equatable, CustomStringConvertible {
    public let reason: String
    public var description: String { reason }
}

/// Encoding, decoding and the validator (spec "The glance record"; vectors in `suite-bar-vectors.json`).
public enum GlanceCodec {
    static let labelMax = 16
    static let unitMax = 6
    static let metricIDMax = 24
    static let bidiFormatting: Set<UInt32> = [0x061C, 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
                                              0x2066, 0x2067, 0x2068, 0x2069]

    /// The reason a record breaks the contract, or `nil` when it is valid.
    public static func problem(in record: GlanceRecord) -> String? {
        guard record.contract >= 1 else { return "contract: integer >= 1" }
        guard GlanceRecord.ttlRange.contains(record.ttl) else { return "ttl: 3-60" }
        guard record.at.isFinite else { return "at: required number" }
        guard record.metrics.count <= GlanceRecord.maxMetrics else { return "metrics: 0-4" }
        var ids = Set<String>()
        for metric in record.metrics {
            if let problem = problem(in: metric) { return problem }
            guard ids.insert(metric.id).inserted else { return "metric ids unique" }
        }
        return nil
    }

    static func problem(in metric: GlanceMetric) -> String? {
        guard (1...metricIDMax).contains(metric.id.unicodeScalars.count) else { return "metric id length" }
        guard metric.id.unicodeScalars.allSatisfy({ ("a"..."z").contains($0) || ("0"..."9").contains($0) || $0 == "-" })
        else { return "metric id charset" }
        if let problem = textProblem(metric.label, field: "label", range: 1...labelMax) { return problem }
        if let problem = textProblem(metric.unit, field: "unit", range: 0...unitMax) { return problem }
        if case .number(let value) = metric.value {
            guard value.isFinite else { return "value: finite number" }
            let hundredths = value * 100
            guard abs(hundredths - hundredths.rounded()) < 1e-6 else { return "value: at most 2 decimals" }
        }
        return nil
    }

    static func textProblem(_ text: String, field: String, range: ClosedRange<Int>) -> String? {
        let scalars = text.unicodeScalars
        guard range.contains(scalars.count) else { return "\(field): \(range.lowerBound)-\(range.upperBound) scalars" }
        for scalar in scalars {
            if scalar == "/" || scalar == "\\" { return "\(field): no / or \\" }
            if scalar.properties.generalCategory == .control { return "\(field): no control characters" }
            if bidiFormatting.contains(scalar.value) { return "\(field): no bidi formatting" }
        }
        return nil
    }

    /// Validates, then encodes. Throws instead of writing a record that breaks the contract.
    public static func encode(_ record: GlanceRecord) throws -> Data {
        if let problem = problem(in: record) { throw GlanceValidationError(reason: problem) }
        let object: [String: Any] = [
            "contract": record.contract,
            "app": record.app.rawValue,
            "hosting": record.hosting,
            "state": record.state.rawValue,
            "at": record.at,
            "ttl": record.ttl,
            "level": record.level.rawValue,
            "metrics": record.metrics.map { metric -> [String: Any] in
                let value: Any = switch metric.value {
                case .number(let number): number
                case .state(let word): word.rawValue
                }
                return ["id": metric.id, "label": metric.label, "value": value, "unit": metric.unit,
                        "level": metric.level.rawValue]
            },
        ]
        let data = try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys, .withoutEscapingSlashes])
        guard data.count <= GlanceRecord.maxBytes else { throw GlanceValidationError(reason: "size: at most 2,048 bytes") }
        return data
    }

    /// Decodes the content of `<fileName>` (for example `optimer.json`). Unknown fields are ignored.
    public static func decode(_ data: Data, fileName: String) -> GlanceReadResult {
        guard data.count <= GlanceRecord.maxBytes else { return .invalid(reason: "size: at most 2,048 bytes") }
        guard let any = try? JSONSerialization.jsonObject(with: data) else { return .invalid(reason: "JSON") }
        guard let object = any as? [String: Any] else { return .invalid(reason: "an object") }
        guard let contract = integer(object["contract"]), contract >= 1 else { return .invalid(reason: "contract: integer >= 1") }
        if contract > GlanceRecord.contract { return .skipped(contract: contract) }

        guard let appID = object["app"] as? String, let app = SuiteApp(rawValue: appID) else {
            return .invalid(reason: "app: catalog id")
        }
        guard fileName == app.glanceFileName else { return .invalid(reason: "app equals file name") }
        guard let hosting = boolean(object["hosting"]) else { return .invalid(reason: "hosting: boolean") }
        guard let stateText = object["state"] as? String, let state = GlanceState(rawValue: stateText) else {
            return .invalid(reason: "state")
        }
        guard let at = number(object["at"]) else { return .invalid(reason: "at: required number") }
        guard let ttlNumber = number(object["ttl"]) else { return .invalid(reason: "ttl: 3-60") }
        guard ttlNumber == ttlNumber.rounded() else { return .invalid(reason: "ttl: integer") }
        guard let levelText = object["level"] as? String, let level = GlanceLevel(rawValue: levelText) else {
            return .invalid(reason: "level")
        }
        guard let rawMetrics = object["metrics"] as? [Any] else { return .invalid(reason: "metrics: 0-4") }
        guard rawMetrics.count <= GlanceRecord.maxMetrics else { return .invalid(reason: "metrics: 0-4") }

        var metrics: [GlanceMetric] = []
        for raw in rawMetrics {
            switch decodeMetric(raw) {
            case .success(let metric): metrics.append(metric)
            case .failure(let error): return .invalid(reason: error.reason)
            }
        }
        guard ttlNumber >= Double(Int.min), ttlNumber <= Double(Int.max) else { return .invalid(reason: "ttl: 3-60") }
        let record = GlanceRecord(app: app, hosting: hosting, state: state, at: at, ttl: Int(ttlNumber), level: level,
                                  metrics: metrics, contract: contract)
        if let problem = problem(in: record) { return .invalid(reason: problem) }
        return .valid(record)
    }

    static func decodeMetric(_ raw: Any) -> Result<GlanceMetric, GlanceValidationError> {
        func fail(_ reason: String) -> Result<GlanceMetric, GlanceValidationError> { .failure(.init(reason: reason)) }
        guard let object = raw as? [String: Any] else { return fail("metric: an object") }
        guard let id = object["id"] as? String else { return fail("metric id length") }
        guard let label = object["label"] as? String else { return fail("label: 1-16 scalars") }
        guard let unit = object["unit"] as? String else { return fail("unit: 0-6 scalars") }
        guard let levelText = object["level"] as? String, let level = GlanceLevel(rawValue: levelText) else {
            return fail("level")
        }
        let value: MetricValue
        if let word = object["value"] as? String {
            guard let state = StateWord(rawValue: word) else { return fail("value: closed state-word list") }
            value = .state(state)
        } else if let number = number(object["value"]) {
            value = .number(number)
        } else {
            return fail("value: finite number")
        }
        return .success(GlanceMetric(id: id, label: label, value: value, unit: unit, level: level))
    }

    /// A JSON number that is not a boolean (Foundation bridges both to `NSNumber`).
    static func number(_ any: Any?) -> Double? {
        guard let number = any as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID() else { return nil }
        let value = number.doubleValue
        return value.isFinite ? value : nil
    }

    static func integer(_ any: Any?) -> Int? {
        guard let value = number(any), value == value.rounded(), abs(value) < 1e15 else { return nil }
        return Int(value)
    }

    static func boolean(_ any: Any?) -> Bool? {
        guard let number = any as? NSNumber, CFGetTypeID(number) == CFBooleanGetTypeID() else { return nil }
        return number.boolValue
    }
}
