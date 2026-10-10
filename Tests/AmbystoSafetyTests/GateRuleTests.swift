import AmbystoProcess
import Foundation
import Testing
@_spi(Testing) @testable import AmbystoSafety

@Suite("Gate and rules")
struct GateRuleTests {
    @Test(arguments: [".ssh/id", "Pictures/x", "Library/Keychains/login", "Library/Mail/V10", "Library/CloudStorage/Dropbox/a",
                      "Library/Containers/com.apple.Notes/Data", "Documents/Old.photoslibrary/db"])
    func aRuleThatAllowsEverythingCannotReachProtectedData(relative: String) throws {
        let home = try TempHome()
        let target = try home.file(relative + "/item")
        let decision = home.gate().check(target, rule: AllowEverythingRule(), expected: stamp(target))
        #expect(!decision.isAllowed, "\(relative)")
        #expect(home.gate().restoreDestinationRefusal(target.path + "-restored", rule: AllowEverythingRule()) != nil, "\(relative)")
    }

    @Test func additionalProtectedRootsProtectAnAppsOwnFolder() throws {
        let home = try TempHome()
        let journal = try home.file("Library/Application Support/Ambysto Space/journal.jsonl")
        #expect(home.gate().check(journal, rule: AllowEverythingRule(), expected: nil).isAllowed)   // not suite-wide
        let gate = home.gate(additionalProtectedRoots: ["Library/Application Support/Ambysto Space"])
        guard case .refused(let reason) = gate.check(journal, rule: AllowEverythingRule(), expected: nil) else {
            Issue.record("the app's own folder was not protected"); return
        }
        #expect(reason.hasPrefix("Protected"))
    }

    @Test func thePublicInitialiserKeepsTheSystemRoots() throws {
        let temp = try TempHome()
        let file = try temp.file("Documents/a/b.bin")
        #expect(SafetyGate(home: temp.url).check(file, rule: AllowEverythingRule(), expected: stamp(file))
                == .refused("Protected: system location /private"))
    }

    // Restore destinations (Space covered these only through Put back).
    @Test func restoreDestinations() throws {
        let home = try TempHome()
        let gate = home.gate()
        try home.dir("Documents/a")
        try home.dir(".ssh")
        try home.dir("Library/Developer")
        #expect(gate.restoreDestinationRefusal(home.url.path + "/Documents/a/file.bin", rule: HomeItemRule()) == nil)
        #expect(gate.restoreDestinationRefusal(home.url.path + "/.ssh/authorized_keys", rule: HomeItemRule())?
            .hasPrefix("Cannot restore to a protected location") == true)
        #expect(gate.restoreDestinationRefusal(home.url.path + "/Library/Developer/x", rule: HomeItemRule())?
            .hasPrefix("Cannot restore there") == true)
        #expect(gate.restoreDestinationRefusal(home.url.path + "/Documents/missing/file.bin", rule: HomeItemRule())
            == "The original folder no longer exists or moved")
        #expect(gate.restoreDestinationRefusal(home.url.path, rule: AllowEverythingRule()) != nil)
    }

    @Test func anAppBundleCanBePutBackThroughItsRestoreRule() throws {
        let home = try TempHome()
        let rule = home.appRule()
        let destination = home.applications.appending(path: "Tool.app").path   // does not exist yet
        #expect(home.gate().restoreDestinationRefusal(destination, rule: rule) == nil)
        #expect(home.gate().restoreDestinationRefusal(home.url.path + "/Documents/Tool.app", rule: rule) != nil)
    }

    @Test func refusedAtSelectionAndAtDeleteTime() throws {
        let home = try TempHome()
        let movie = try home.file("Documents/m/movie.mov")
        let shown = try #require(stamp(movie))                       // selection
        #expect(home.gate().check(movie, rule: HomeItemRule(), expected: shown).isAllowed)
        try FileManager.default.removeItem(at: movie)                // replaced before the delete
        try home.file("Documents/m/movie.mov")
        #expect(home.gate().check(movie, rule: HomeItemRule(), expected: shown) == .refused("Changed since the scan — scan again"))
    }
}
