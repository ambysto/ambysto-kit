// From Ambysto Space (ambysto/ambysto-space@1ab11ea, macos/Sources/SpaceCore/Safety), merged into Ambysto Kit 0.2 (APS-11).
import Foundation

public enum FullDiskAccess {
    /// Settings pane for Privacy & Security → Full Disk Access.
    public static let settingsURL = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles")!

    /// Probes the privacy database first (readable only with Full Disk Access), then Optimer's Safari probe.
    /// `nil` when neither probe exists.
    public static func isGranted(home: URL = FileManager.default.homeDirectoryForCurrentUser) -> Bool? {
        let tcc = home.appending(path: "Library/Application Support/com.apple.TCC/TCC.db").path
        if FileManager.default.fileExists(atPath: tcc) {
            guard let handle = FileHandle(forReadingAtPath: tcc) else { return false }
            try? handle.close()
            return true
        }
        let probe = home.appending(path: "Library/Safari", directoryHint: .isDirectory)
        guard FileManager.default.fileExists(atPath: probe.path) else { return nil }
        return (try? FileManager.default.contentsOfDirectory(atPath: probe.path)) != nil
    }
}
