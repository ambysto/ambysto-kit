import Foundation
@_spi(Testing) @testable import AmbystoSafety

/// Test rules that mirror Ambysto Space's three allowed roots, so every refusal path of the gate is still covered.
/// The real rules live in each app.
struct HomeItemRule: AllowedRootRule {
    func refusal(realPath: String, isDirectory: Bool, context: SafetyContext) -> String? {
        guard RealPath.isInside(realPath, context.homePath) else { return "Outside the home folder" }
        let components = realPath.dropFirst(context.homePath.count).split(separator: "/")
        guard components.count >= 2 else { return "Too close to the home folder" }
        if components[0] == "Library" {
            let deletable = components.count >= 3 && (components[1] == "Caches" || components[1] == "Logs")
            guard deletable else { return "Inside Library: only Caches and Logs can be moved from here" }
        }
        return nil
    }

    func restoreRefusal(realPath: String, context: SafetyContext) -> String? {
        refusal(realPath: realPath, isDirectory: false, context: context)
    }
}

struct AppBundleRule: AllowedRootRule {
    let folders: [String]
    let isWritable: @Sendable (String) -> Bool

    private func inFolder(_ realPath: String) -> Bool {
        let parent = (realPath as NSString).deletingLastPathComponent
        let grandparent = (parent as NSString).deletingLastPathComponent
        return folders.contains(parent) || (folders.contains(grandparent) && !parent.hasSuffix(".app"))
    }

    func refusal(realPath: String, isDirectory: Bool, context: SafetyContext) -> String? {
        guard isDirectory, realPath.hasSuffix(".app") else { return "Not an application bundle" }
        guard inFolder(realPath) else { return "Not in an Applications folder" }
        guard FileManager.default.fileExists(atPath: realPath + "/Contents/Info.plist") else { return "Not a Mac app (remove it in Finder)" }
        let parent = (realPath as NSString).deletingLastPathComponent
        guard isWritable(realPath), isWritable(parent) else { return "Needs admin: remove it in Finder" }
        return nil
    }

    /// The bundle is not there yet: no Info.plist or writability check.
    func restoreRefusal(realPath: String, context: SafetyContext) -> String? {
        guard realPath.hasSuffix(".app"), inFolder(realPath) else { return "not an Applications folder" }
        return nil
    }
}

struct LeftoverRule: AllowedRootRule {
    static let folders = ["Application Support", "Caches", "Preferences", "Preferences/ByHost", "Containers", "Group Containers",
                          "Saved Application State", "Logs", "HTTPStorages", "WebKit", "Cookies", "LaunchAgents", "Application Scripts"]

    func refusal(realPath: String, isDirectory: Bool, context: SafetyContext) -> String? {
        let parent = (realPath as NSString).deletingLastPathComponent
        guard Self.folders.map({ context.homePath + "/Library/" + $0 }).contains(parent) else { return "Not in a leftover folder" }
        return nil
    }

    func restoreRefusal(realPath: String, context: SafetyContext) -> String? {
        refusal(realPath: realPath, isDirectory: false, context: context)
    }
}

/// A rule that allows everything: the gate's protection must still hold.
struct AllowEverythingRule: AllowedRootRule {
    func refusal(realPath: String, isDirectory: Bool, context: SafetyContext) -> String? { nil }
    func restoreRefusal(realPath: String, context: SafetyContext) -> String? { nil }
}
