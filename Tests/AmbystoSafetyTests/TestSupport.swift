import Darwin
import Foundation
import AmbystoProcess
@_spi(Testing) @testable import AmbystoSafety

/// A throwaway home folder (plus an Applications folder) under the temp directory. Removed on deinit.
final class TempHome: @unchecked Sendable {
    let url: URL
    let applications: URL

    init() throws {
        let base = FileManager.default.temporaryDirectory
            .appending(path: "space-tests-\(UUID().uuidString)", directoryHint: .isDirectory)
        url = base.appending(path: "home", directoryHint: .isDirectory)
        applications = base.appending(path: "Applications", directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        try FileManager.default.createDirectory(at: applications, withIntermediateDirectories: true)
    }

    deinit {
        let base = url.deletingLastPathComponent()
        _ = try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: base.path)
        try? FileManager.default.removeItem(at: base)
    }

    /// The temp folder lives under /private, which the production gate refuses; tests inject no system roots.
    func gate(additionalProtectedRoots: [String] = []) -> SafetyGate {
        SafetyGate(home: url, additionalProtectedRoots: additionalProtectedRoots, systemRoots: [], canonicalizer: .shared)
    }

    /// Mirrors Space's application-bundle rule over this temp home's Applications folders.
    func appRule(isWritable: @escaping @Sendable (String) -> Bool = { access($0, W_OK) == 0 }) -> AppBundleRule {
        AppBundleRule(folders: [applications, url.appending(path: "Applications")].map { PathCanonicalizer.shared.canonical($0.path) ?? $0.path },
                      isWritable: isWritable)
    }

    @discardableResult
    func dir(_ relative: String) throws -> URL {
        let target = url.appending(path: relative, directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
        return target
    }

    @discardableResult
    func file(_ relative: String, bytes: Int = 4096, fill: UInt8 = 0x41) throws -> URL {
        try Self.write(url.appending(path: relative), bytes: bytes, fill: fill)
    }

    @discardableResult
    static func write(_ target: URL, bytes: Int = 4096, fill: UInt8 = 0x41) throws -> URL {
        try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(repeating: fill, count: bytes).write(to: target)
        return target
    }

    func link(_ relative: String, to destination: URL) throws -> URL {
        let source = url.appending(path: relative)
        try FileManager.default.createDirectory(at: source.deletingLastPathComponent(), withIntermediateDirectories: true)
        try FileManager.default.createSymbolicLink(at: source, withDestinationURL: destination)
        return source
    }

    /// A minimal Mac app bundle.
    @discardableResult
    func app(_ name: String, in folder: URL, bundleID: String = "com.example.app") throws -> URL {
        let bundle = folder.appending(path: name, directoryHint: .isDirectory)
        try FileManager.default.createDirectory(at: bundle.appending(path: "Contents/MacOS"), withIntermediateDirectories: true)
        let plist: [String: Any] = ["CFBundleIdentifier": bundleID, "CFBundleName": (name as NSString).deletingPathExtension, "CFBundleShortVersionString": "1.0"]
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try data.write(to: bundle.appending(path: "Contents/Info.plist"))
        return bundle
    }
}

func stamp(_ url: URL) -> FileStamp? { FileStamp.of(url.path) }

/// Spawns a helper process; terminated on deinit.
final class Helper: @unchecked Sendable {
    let process = Process()

    init(_ executable: String, _ arguments: [String], cwd: URL? = nil) throws {
        process.executableURL = URL(filePath: executable)
        process.arguments = arguments
        if let cwd { process.currentDirectoryURL = cwd }
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
    }

    /// Polls until `condition` holds (or 5 s pass) instead of sleeping a fixed time.
    @discardableResult
    func waitUntil(_ condition: () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(5)
        while Date() < deadline {
            if condition() { return true }
            Thread.sleep(forTimeInterval: 0.05)
        }
        return condition()
    }

    func hasOpen(_ url: URL) -> Bool {
        let target = FileIdentity.of(url.path)
        return ProcessReader.openFiles(pid)?.contains { $0.identity == target } ?? false
    }

    func isWorking(in folder: URL) -> Bool {
        guard let cwd = ProcessReader.currentDirectory(pid) else { return false }
        return RealPath.resolve(cwd) == RealPath.resolve(folder.path)
    }

    var pid: Int32 { process.processIdentifier }

    deinit {
        process.terminate()
        process.waitUntilExit()
    }
}
