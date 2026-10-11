import AmbystoSuite
import AppKit
import SwiftUI

/// Text for metrics and levels. Numbers are formatted in the user's locale; the publisher sends raw numbers.
public enum SuiteFormat {
    public static func text(_ metric: GlanceMetric, locale: Locale = .current) -> String {
        switch metric.value {
        case .state(let word):
            return word.rawValue.capitalized
        case .number(let value):
            let number = value.formatted(.number.precision(.fractionLength(0...2)).locale(locale))
            return metric.unit.isEmpty ? number : number + "\u{00A0}" + metric.unit
        }
    }

    public static func levelWord(_ level: GlanceLevel) -> String {
        switch level {
        case .ok: "ok"
        case .notice: "notice"
        case .alert: "alert"
        }
    }

    /// For VoiceOver on the menu bar item, e.g. "Ambysto suite, Memory 62 %, notice".
    public static func accessibilityLabel(values: [(MetricRef, GlanceMetric)], worst: GlanceLevel, locale: Locale = .current) -> String {
        var parts = ["Ambysto suite"]
        parts += values.map { "\($0.1.label) \(text($0.1, locale: locale))" }
        parts.append(levelWord(worst))
        return parts.joined(separator: ", ")
    }
}

/// A symbol for a level that is not ok, with the level spelled out for VoiceOver.
struct LevelGlyph: View {
    let level: GlanceLevel

    var body: some View {
        if let name = Theme.glyph(level) {
            Image(systemName: name)
                .foregroundStyle(Theme.color(level))
                .accessibilityLabel(SuiteFormat.levelWord(level))
        }
    }
}

/// The rows of the readout in catalog order: running apps with their metrics, the others as "not running".
struct SuiteRowsView: View {
    var suite: SuiteParticipant
    var showsOpen = true

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(suite.rows, id: \.app) { row in
                switch row {
                case .running(let record):
                    VStack(alignment: .leading, spacing: 4) {
                        HStack {
                            Text(record.app.shortName).font(.headline)
                            LevelGlyph(level: record.level)
                            Spacer(minLength: 8)
                            if showsOpen {
                                Button("Open") { SuiteParticipant.requestOpen(record.app) }
                                    .controlSize(.small)
                                    .accessibilityLabel("Open \(record.app.displayName)")
                            }
                        }
                        ForEach(record.metrics, id: \.id) { metric in
                            HStack {
                                Text(metric.label).foregroundStyle(.secondary)
                                Spacer(minLength: 8)
                                LevelGlyph(level: metric.level)
                                Text(SuiteFormat.text(metric)).monospacedDigit()
                            }
                            .font(.callout)
                            .accessibilityElement(children: .combine)
                        }
                    }
                case .notRunning(let app):
                    HStack {
                        Text(app.shortName).font(.headline)
                        Spacer(minLength: 8)
                        Text("not running").foregroundStyle(.secondary)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
    }
}

/// The popover of the suite's menu bar item.
public struct SuitePopover: View {
    var suite: SuiteParticipant
    @State private var installed: [(SuiteApp, URL)] = []

    public init(suite: SuiteParticipant) {
        self.suite = suite
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            SuiteRowsView(suite: suite)
            Divider()
            menuBarPickers
            Toggle("Show floating bar", isOn: settingsBinding(\.floatingVisible))
                .accessibilityLabel("Show floating bar, shortcut \(GlobalHotKey.displayName)")
            Divider()
            appsSection
        }
        .padding(16)
        .frame(width: 300)
        .onAppear { installed = Self.installedApps() }
    }

    private var menuBarPickers: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Show in menu bar").font(.subheadline).foregroundStyle(.secondary)
            ForEach(0..<SuiteSettings.maxMenuBarValues, id: \.self) { slot in
                Picker("Value \(slot + 1)", selection: slotBinding(slot)) {
                    Text("None").tag(MetricRef?.none)
                    ForEach(choices, id: \.0) { ref, title in
                        Text(title).tag(Optional(ref))
                    }
                }
                .accessibilityLabel("Menu bar value \(slot + 1)")
            }
        }
    }

    /// Every fresh metric, for the pickers.
    private var choices: [(MetricRef, String)] {
        suite.rows.flatMap { row -> [(MetricRef, String)] in
            guard case .running(let record) = row else { return [] }
            return record.metrics.map { (MetricRef(app: record.app, metric: $0.id), "\(record.app.shortName): \($0.label)") }
        }
    }

    private func slotBinding(_ slot: Int) -> Binding<MetricRef?> {
        Binding(
            get: {
                let current = suite.settings.menuBarValues ?? suite.menuBarValues.map(\.0)
                return slot < current.count ? current[slot] : nil
            },
            set: { newValue in
                var refs = suite.settings.menuBarValues ?? suite.menuBarValues.map(\.0)
                while refs.count <= slot { refs.append(MetricRef(app: .optimer, metric: "")) }
                refs[slot] = newValue ?? MetricRef(app: .optimer, metric: "")
                var settings = suite.settings
                settings.menuBarValues = refs.filter { !$0.metric.isEmpty }
                suite.settings = settings
            })
    }

    private var appsSection: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Ambysto apps").font(.subheadline).foregroundStyle(.secondary)
            ForEach(installed, id: \.0) { app, url in
                Button(app.displayName) {
                    NSWorkspace.shared.openApplication(at: url, configuration: NSWorkspace.OpenConfiguration())
                }
                .buttonStyle(.link)
                .accessibilityLabel("Open \(app.displayName)")
            }
        }
    }

    /// Catalog apps that are installed (an app that is not installed is not listed).
    private static func installedApps() -> [(SuiteApp, URL)] {
        SuiteApp.allCases.compactMap { app in
            NSWorkspace.shared.urlForApplication(withBundleIdentifier: app.bundleIdentifier).map { (app, $0) }
        }
    }

    private func settingsBinding(_ path: WritableKeyPath<SuiteSettings, Bool>) -> Binding<Bool> {
        Binding(
            get: { suite.settings[keyPath: path] },
            set: { newValue in
                var settings = suite.settings
                settings[keyPath: path] = newValue
                suite.settings = settings
            })
    }
}

/// The merged floating bar: one line per app when compact, the popover's rows when expanded.
public struct SuiteFloatingBar: View {
    var suite: SuiteParticipant

    public init(suite: SuiteParticipant) {
        self.suite = suite
    }

    public var body: some View {
        let expanded = suite.settings.floatingExpanded
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Image(systemName: "circle.hexagongrid").accessibilityHidden(true)
                Text("Ambysto").font(.caption.weight(.semibold))
                Spacer()
                Button {
                    var settings = suite.settings
                    settings.floatingExpanded.toggle()
                    suite.settings = settings
                } label: {
                    Image(systemName: expanded ? "chevron.up" : "chevron.down")
                }
                .buttonStyle(.plain)
                .accessibilityLabel(expanded ? "Collapse floating bar" : "Expand floating bar")
            }
            if expanded {
                ScrollView { SuiteRowsView(suite: suite) }
            } else {
                ForEach(suite.rows, id: \.app) { row in compactLine(row) }
            }
        }
        .foregroundStyle(.white)
        .padding(10)
        .frame(maxWidth: .infinity, maxHeight: expanded ? .infinity : nil, alignment: .topLeading)
        .background(Theme.ink, in: RoundedRectangle(cornerRadius: Theme.cardRadius))
        .environment(\.colorScheme, .dark)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
    }

    @ViewBuilder
    private func compactLine(_ row: SuiteRow) -> some View {
        switch row {
        case .running(let record):
            HStack(spacing: 6) {
                Text(record.app.shortName).font(.caption.weight(.medium))
                LevelGlyph(level: record.level)
                Spacer(minLength: 6)
                Text(record.metrics.prefix(2).map { SuiteFormat.text($0) }.joined(separator: "  "))
                    .font(.caption).monospacedDigit()
            }
            .accessibilityElement(children: .combine)
        case .notRunning(let app):
            HStack {
                Text(app.shortName).font(.caption.weight(.medium))
                Spacer()
                Text("not running").font(.caption).foregroundStyle(.secondary)
            }
            .accessibilityElement(children: .combine)
        }
    }
}

/// For an app's Settings: the opt-in to keep its own menu bar icon next to the suite item.
public struct SuiteSettingsSection: View {
    var suite: SuiteParticipant

    public init(suite: SuiteParticipant) {
        self.suite = suite
    }

    public var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Toggle("Show my own menu bar icon", isOn: Binding(
                get: { suite.showOwnIconSetting },
                set: { suite.showOwnIconSetting = $0 }))
                .accessibilityLabel("Show my own menu bar icon")
            Text("When two or more Ambysto apps are running they share one menu bar item with a readout of the whole suite.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }
}
