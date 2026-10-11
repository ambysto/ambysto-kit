// Proof helper (suite bar plan group 5): a fake suite participant. No app depends on it.
//
//   SuiteBarDemo --folder <path> --app <id> --metric "Label=value[unit]" [--metric ...] [--level ok|notice|alert]
//
// Publishes every 2 s, hosts when elected, and prints one JSON line to stdout per transition. Commands on stdin:
// `quit` (clean shutdown, then exit) and `freeze` (stop publishing, heartbeats included).
import AmbystoSuite
import AmbystoUI
import AppKit
import Foundation
import Observation

setvbuf(stdout, nil, _IOLBF, 0)

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("SuiteBarDemo: \(message)\n".utf8))
    FileHandle.standardError.write(Data("usage: SuiteBarDemo --folder <path> --app <id> --metric \"Label=value[unit]\" (max 4) [--level ok|notice|alert]\n".utf8))
    exit(2)
}

/// "Servers=3" or "Memory=62.5%" or "Mode=active".
func parseMetric(_ text: String, index: Int) -> GlanceMetric {
    guard let equals = text.firstIndex(of: "="), equals != text.startIndex else { fail("metric needs Label=value: \(text)") }
    let label = String(text[..<equals])
    let rest = String(text[text.index(after: equals)...])
    var id = String(label.lowercased().map { $0.isASCII && ($0.isLetter || $0.isNumber) ? $0 : "-" })
    id = String(id.prefix(20)) + "-\(index)"
    if let word = StateWord(rawValue: rest) {
        return GlanceMetric(id: id, label: label, state: word)
    }
    let digits = rest.prefix { $0.isNumber || $0 == "." || $0 == "-" }
    guard let value = Double(digits) else { fail("metric value is not a number or a state word: \(text)") }
    return GlanceMetric(id: id, label: label, value: value, unit: String(rest.dropFirst(digits.count)))
}

var folderPath: String?
var appID: String?
var level = GlanceLevel.ok
var metricTexts: [String] = []
var arguments = CommandLine.arguments.dropFirst().makeIterator()
while let argument = arguments.next() {
    func value() -> String {
        guard let next = arguments.next() else { fail("\(argument) needs a value") }
        return next
    }
    switch argument {
    case "--folder": folderPath = value()
    case "--app": appID = value()
    case "--metric": metricTexts.append(value())
    case "--level":
        guard let parsed = GlanceLevel(rawValue: value()) else { fail("--level is ok, notice or alert") }
        level = parsed
    default: fail("unknown argument \(argument)")
    }
}
guard let folderPath else { fail("--folder is required") }
guard let appID, let app = SuiteApp(rawValue: appID) else { fail("--app is one of: \(SuiteApp.allCases.map(\.rawValue).joined(separator: ", "))") }
guard metricTexts.count <= GlanceRecord.maxMetrics else { fail("at most 4 metrics") }
let metrics = metricTexts.enumerated().map { parseMetric($0.element, index: $0.offset) }

// Never the real suite folder, nor anything inside it.
let folderURL = URL(fileURLWithPath: (folderPath as NSString).expandingTildeInPath).standardizedFileURL.resolvingSymlinksInPath()
let realFolder = SuiteFolder.plain().url.resolvingSymlinksInPath().path
if folderURL.path == realFolder || folderURL.path.hasPrefix(realFolder + "/") {
    fail("refusing the real suite folder \(realFolder)")
}

@MainActor
final class Demo {
    let app: SuiteApp
    let suite: SuiteParticipant
    private var ownItem: NSStatusItem?
    private var wasHost = false
    private var wasActive = false
    private var ownShown = false
    private var rowsText = ""
    private var frozen = false

    init(app: SuiteApp, folder: SuiteFolder, level: GlanceLevel, metrics: [GlanceMetric]) {
        self.app = app
        self.level = level
        self.metrics = metrics
        suite = SuiteParticipant(app: app, folders: [folder], observeSystem: true, onOpen: { emit("open", app: app) })
    }

    private let level: GlanceLevel
    private let metrics: [GlanceMetric]

    func start() {
        publish()
        apply()
        track()
        let timer = Timer(timeInterval: 2, repeats: true) { _ in MainActor.assumeIsolated { Demo.shared?.publish() } }
        RunLoop.main.add(timer, forMode: .common)
    }

    static var shared: Demo?

    func publish() {
        guard !frozen else { return }
        suite.publish(level: level, metrics: metrics)
    }

    func freeze() {
        frozen = true
        suite.suspendPublishing()
        emit("frozen", app: app)
    }

    func quit() -> Never {
        suite.shutdown()
        emit("quit", app: app)
        exit(0)
    }

    private func track() {
        withObservationTracking {
            _ = suite.showsOwnIcon
            _ = suite.isHost
            _ = suite.isActive
            _ = suite.rows
        } onChange: {
            DispatchQueue.main.async { MainActor.assumeIsolated { Demo.shared?.apply(); Demo.shared?.track() } }
        }
    }

    private func apply() {
        if suite.isHost != wasHost {
            wasHost = suite.isHost
            emit(wasHost ? "hosting" : "not-hosting", app: app)
        }
        if suite.isActive != wasActive {
            wasActive = suite.isActive
            emit(wasActive ? "active" : "inactive", app: app)
        }
        if suite.showsOwnIcon != ownShown {
            ownShown = suite.showsOwnIcon
            if ownShown {
                let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
                item.button?.title = "\(app.rawValue) own"
                ownItem = item
            } else if let ownItem {
                NSStatusBar.system.removeStatusItem(ownItem)
                self.ownItem = nil
            }
            emit(ownShown ? "own-icon-shown" : "own-icon-hidden", app: app)
        }
        let summary = suite.rows.map { row -> String in
            if case .running = row { return "\(row.app.rawValue)=running" }
            return "\(row.app.rawValue)=notRunning"
        }.joined(separator: ",")
        if summary != rowsText {
            rowsText = summary
            emit("rows", app: app, extra: ",\"rows\":\"\(summary)\"")
        }
    }
}

/// One JSON line per transition.
func emit(_ event: String, app: SuiteApp, extra: String = "") {
    let t = String(format: "%.3f", Date().timeIntervalSince1970)
    print("{\"t\":\(t),\"pid\":\(getpid()),\"app\":\"\(app.rawValue)\",\"event\":\"\(event)\"\(extra)}")
    fflush(stdout)
}

NSApplication.shared.setActivationPolicy(.accessory)
let demo = Demo(app: app, folder: SuiteFolder(url: folderURL), level: level, metrics: metrics)
Demo.shared = demo
demo.start()

// Commands arrive on stdin; on end of input the demo simply keeps running.
let reader = Thread {
    while let line = readLine() {
        switch line.trimmingCharacters(in: .whitespaces) {
        case "quit": DispatchQueue.main.async { MainActor.assumeIsolated { Demo.shared?.quit() } }
        case "freeze": DispatchQueue.main.async { MainActor.assumeIsolated { Demo.shared?.freeze() } }
        default: break
        }
    }
}
reader.start()

NSApplication.shared.run()
