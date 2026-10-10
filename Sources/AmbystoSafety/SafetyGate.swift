// From Ambysto Space's SafetyPolicy (ambysto/ambysto-space@1ab11ea), generalised for Ambysto Kit 0.2 (APS-11):
// the protection checks live here; which folders an app may change comes from the app's AllowedRootRule.
import AmbystoProcess
import Darwin
import Foundation

public enum SafetyDecision: Sendable, Equatable {
    case allowed(realPath: String, stamp: FileStamp)
    case refused(String)

    public var isAllowed: Bool {
        if case .allowed = self { return true }
        return false
    }
}

/// What an allowed-root rule may use.
public struct SafetyContext: Sendable {
    /// Canonical home path (symlinks resolved, firmlinks collapsed).
    public let homePath: String
    let canonicalizer: PathCanonicalizer

    /// Canonical, user-facing form of an existing path; `nil` if it does not exist.
    public func canonical(_ path: String) -> String? { canonicalizer.canonical(path) }
    public func visiblePath(_ path: String) -> String { canonicalizer.visiblePath(path) }
}

/// Which folders an app may change. Runs only after every protection check passed, so a rule can never widen
/// protection. docs/spec/safety.md → "Allowed roots".
public protocol AllowedRootRule: Sendable {
    /// Deleting. `nil` = the canonical real path is inside this rule's roots; otherwise a plain refusal reason.
    func refusal(realPath: String, isDirectory: Bool, context: SafetyContext) -> String?
    /// Putting back: the destination does not exist yet. No default, so a rule never reuses its delete check by accident.
    func restoreRefusal(realPath: String, context: SafetyContext) -> String?
}

/// The gate every move to the Trash and every Put back passes, immediately before it happens.
/// docs/spec/safety.md → "The gate".
public struct SafetyGate: Sendable {
    public let homePath: String
    public let neverTouchRoots: [String]
    public let context: SafetyContext
    let systemRoots: [String]
    let neverTouchIdentities: [FileIdentity: String]
    /// Identities of every ancestor of a never-touch root → that root (deleting a parent of protected data).
    let protectedAncestorIdentities: [FileIdentity: String]
    let volumeDevice: UInt64?
    let canonicalizer: PathCanonicalizer

    /// - Parameter additionalProtectedRoots: home-relative folders the app adds to the suite-wide list
    ///   (for example its own data folder). Protection can only be added, never removed.
    public init(home: URL, additionalProtectedRoots: [String] = []) {
        self.init(home: home, additionalProtectedRoots: additionalProtectedRoots,
                  systemRoots: NeverTouch.defaultSystemRoots, canonicalizer: .shared)
    }

    /// Tests only: the temp folder lives under `/private`, which the system roots refuse.
    @_spi(Testing) public init(home: URL, additionalProtectedRoots: [String] = [], systemRoots: [String],
                               canonicalizer: PathCanonicalizer) {
        let homePath = canonicalizer.canonical(home.path) ?? home.standardizedFileURL.path
        self.homePath = homePath
        self.canonicalizer = canonicalizer
        self.systemRoots = systemRoots
        context = SafetyContext(homePath: homePath, canonicalizer: canonicalizer)
        let roots = NeverTouch.roots(home: home, additional: additionalProtectedRoots).map { canonicalizer.visiblePath($0) }
        neverTouchRoots = roots
        var identities: [FileIdentity: String] = [:]
        var ancestors: [FileIdentity: String] = [:]
        for root in roots {
            guard let identity = FileIdentity.of(root) else { continue }
            identities[identity] = root
            var parent = (root as NSString).deletingLastPathComponent
            while RealPath.isInside(parent, homePath), parent != homePath {
                if let id = FileIdentity.of(parent), ancestors[id] == nil { ancestors[id] = root }
                parent = (parent as NSString).deletingLastPathComponent
            }
        }
        neverTouchIdentities = identities
        protectedAncestorIdentities = ancestors
        volumeDevice = FileIdentity.of(homePath)?.device
    }

    /// - Parameter expected: the stamp recorded when the item was shown; `nil` only for callers without one.
    public func check(_ url: URL, rule: some AllowedRootRule, expected: FileStamp?) -> SafetyDecision {
        let path = url.path
        var info = stat()
        guard lstat(path, &info) == 0 else { return .refused("Already gone") }
        if (info.st_mode & S_IFMT) == S_IFLNK {
            return .refused("Is a symbolic link; links are never followed for deletion")
        }
        guard let real = canonicalizer.canonical(path) else { return .refused("Could not resolve the real path") }
        let stamp = FileStamp(info)
        if let expected, !expected.isUnchanged(comparedTo: stamp) {
            return .refused("Changed since the scan — scan again")
        }
        if let volumeDevice, stamp.identity.device != volumeDevice {
            return .refused("On another volume")
        }
        if real == "/" || real == homePath {
            return .refused("Refusing to delete the root or home folder")
        }
        if let reason = protectionReason(real: real, identity: stamp.identity) {
            return .refused(reason)
        }
        if let reason = rule.refusal(realPath: real, isDirectory: stamp.isDirectory, context: context) {
            return .refused(reason)
        }
        return .allowed(realPath: real, stamp: stamp)
    }

    /// A Put back destination, which does not exist yet. `nil` = allowed.
    public func restoreDestinationRefusal(_ path: String, rule: some AllowedRootRule) -> String? {
        let parentPath = (path as NSString).deletingLastPathComponent
        guard let parent = canonicalizer.canonical(parentPath) else { return "The original folder no longer exists or moved" }
        let real = parent + "/" + (path as NSString).lastPathComponent
        if real == "/" || real == homePath { return "Cannot restore to the root or home folder" }
        if let reason = protectionReason(real: real, identity: nil) { return "Cannot restore to a protected location (\(reason))" }
        if let reason = rule.restoreRefusal(realPath: real, context: context) { return "Cannot restore there: \(reason)" }
        return nil
    }

    /// Never-touch rules on the canonical path **and** on identities of the item and every ancestor.
    /// `identity` is `nil` for a path that does not exist yet.
    func protectionReason(real: String, identity: FileIdentity?) -> String? {
        for root in systemRoots where RealPath.isInside(real, root) {
            return "Protected: system location \(root)"
        }
        for root in neverTouchRoots {
            if RealPath.isInside(real, root) { return "Protected: \(display(root))" }
            if RealPath.isInside(root, real) { return "Contains protected data: \(display(root))" }
        }
        if let identity, let root = neverTouchIdentities[identity] { return "Protected: \(display(root))" }
        if let identity, let root = protectedAncestorIdentities[identity] { return "Contains protected data: \(display(root))" }
        var current = real
        while true {
            if let id = FileIdentity.of(current), let root = neverTouchIdentities[id] { return "Protected: \(display(root))" }
            let parent = (current as NSString).deletingLastPathComponent
            if parent == current || parent.isEmpty { break }
            current = parent
        }
        if NeverTouch.hasProtectedComponent(real) { return "Protected library or backup" }
        if NeverTouch.isAppleOwned(real, homePath: homePath) { return "Protected: Apple app data" }
        if NeverTouch.isRestricted(real) { return "Protected by macOS" }
        return nil
    }

    private func display(_ path: String) -> String {
        path.hasPrefix(homePath) ? "~" + path.dropFirst(homePath.count) : path
    }
}
