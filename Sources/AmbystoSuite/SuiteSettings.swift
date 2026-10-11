import CoreGraphics
import Foundation

/// A metric of an app, as chosen for the menu bar item.
public struct MetricRef: Sendable, Hashable, Codable {
    public var app: SuiteApp
    public var metric: String

    public init(app: SuiteApp, metric: String) {
        self.app = app
        self.metric = metric
    }
}

/// The readout's choices (spec "The readout", `settings.json`), written only by the current host.
/// Keys this version does not know are kept, so an older host never drops a newer host's settings.
public struct SuiteSettings: Sendable, Equatable {
    public static let maxMenuBarValues = 2

    /// `nil` = the default choice (spec "Menu bar item").
    public var menuBarValues: [MetricRef]?
    public var floatingVisible = false
    public var floatingExpanded = false
    /// Top-left corner of the floating bar, screen coordinates.
    public var floatingTopLeft: CGPoint?
    /// What the system stores as the menu bar item's preferred position.
    public var statusItemPosition: Double?
    var unknown: [String: JSONValue] = [:]

    public init() {}

    public static func decode(_ data: Data) -> SuiteSettings {
        guard case .object(var object)? = try? JSONDecoder().decode(JSONValue.self, from: data) else { return SuiteSettings() }
        var settings = SuiteSettings()
        if case .array(let refs)? = object.removeValue(forKey: "menuBarValues") {
            settings.menuBarValues = refs.compactMap { ref -> MetricRef? in
                guard case .object(let fields) = ref, case .string(let app)? = fields["app"],
                      let suiteApp = SuiteApp(rawValue: app), case .string(let metric)? = fields["metric"] else { return nil }
                return MetricRef(app: suiteApp, metric: metric)
            }.prefix(maxMenuBarValues).map { $0 }
        }
        if case .bool(let value)? = object.removeValue(forKey: "floatingVisible") { settings.floatingVisible = value }
        if case .bool(let value)? = object.removeValue(forKey: "floatingExpanded") { settings.floatingExpanded = value }
        if case .array(let point)? = object.removeValue(forKey: "floatingTopLeft"), point.count == 2,
           case .number(let x) = point[0], case .number(let y) = point[1] {
            settings.floatingTopLeft = CGPoint(x: x, y: y)
        }
        if case .number(let value)? = object.removeValue(forKey: "statusItemPosition") { settings.statusItemPosition = value }
        settings.unknown = object
        return settings
    }

    public func encode() throws -> Data {
        var object = unknown
        if let menuBarValues {
            object["menuBarValues"] = .array(menuBarValues.prefix(Self.maxMenuBarValues).map {
                .object(["app": .string($0.app.rawValue), "metric": .string($0.metric)])
            })
        }
        object["floatingVisible"] = .bool(floatingVisible)
        object["floatingExpanded"] = .bool(floatingExpanded)
        if let floatingTopLeft {
            object["floatingTopLeft"] = .array([.number(floatingTopLeft.x), .number(floatingTopLeft.y)])
        }
        if let statusItemPosition { object["statusItemPosition"] = .number(statusItemPosition) }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return try encoder.encode(JSONValue.object(object))
    }

    /// Reads `settings.json` with the same safe read as glances (64 KB at most); defaults when missing or unreadable.
    public static func read(from folder: SuiteFolder) -> SuiteSettings {
        guard case .data(let data) = GlanceStore.safeRead(path: folder.settingsURL.path, limit: 64 * 1024) else {
            return SuiteSettings()
        }
        return decode(data)
    }

    public func write(to folder: SuiteFolder) throws {
        try encode().write(to: folder.settingsURL, options: .atomic)
    }
}

/// Just enough JSON to keep unknown keys intact.
enum JSONValue: Sendable, Equatable, Codable {
    case null
    case bool(Bool)
    case number(Double)
    case string(String)
    case array([JSONValue])
    case object([String: JSONValue])

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if container.decodeNil() { self = .null }
        else if let value = try? container.decode(Bool.self) { self = .bool(value) }
        else if let value = try? container.decode(Double.self) { self = .number(value) }
        else if let value = try? container.decode(String.self) { self = .string(value) }
        else if let value = try? container.decode([JSONValue].self) { self = .array(value) }
        else { self = .object(try container.decode([String: JSONValue].self)) }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        switch self {
        case .null: try container.encodeNil()
        case .bool(let value): try container.encode(value)
        case .number(let value): try container.encode(value)
        case .string(let value): try container.encode(value)
        case .array(let value): try container.encode(value)
        case .object(let value): try container.encode(value)
        }
    }
}
