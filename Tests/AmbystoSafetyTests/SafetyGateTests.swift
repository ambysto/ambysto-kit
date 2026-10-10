import Darwin
import Foundation
import Testing
import AmbystoProcess
@_spi(Testing) @testable import AmbystoSafety

@Suite("SafetyGate")
struct SafetyGateTests {
    // MARK: Home items (ported from Optimer and narrowed for Space)

    @Test func allowsAFileDeepInHome() throws {
        let home = try TempHome()
        let movie = try home.file("Documents/Old/movie.mov")
        #expect(home.gate().check(movie, rule: HomeItemRule(), expected: stamp(movie)).isAllowed)
    }

    @Test func refusesRootHomeAndShallowPaths() throws {
        let home = try TempHome()
        let gate = home.gate()
        let shallow = try home.dir("Projects")
        #expect(gate.check(home.url, rule: HomeItemRule(), expected: nil) == .refused("Refusing to delete the root or home folder"))
        #expect(gate.check(shallow, rule: HomeItemRule(), expected: nil) == .refused("Too close to the home folder"))
        let system = gate.check(URL(filePath: "/usr/bin"), rule: HomeItemRule(), expected: nil)
        #expect(!system.isAllowed, "\(system)")
    }

    @Test func libraryOnlyAllowsInsideCachesAndLogs() throws {
        let home = try TempHome()
        let gate = home.gate()
        let cache = try home.dir("Library/Caches/com.example.tool")
        let log = try home.file("Library/Logs/tool.log")
        let support = try home.dir("Library/Application Support/Tool")
        let cachesItself = home.url.appending(path: "Library/Caches")
        #expect(gate.check(cache, rule: HomeItemRule(), expected: stamp(cache)).isAllowed)
        #expect(gate.check(log, rule: HomeItemRule(), expected: stamp(log)).isAllowed)
        #expect(gate.check(support, rule: HomeItemRule(), expected: nil) == .refused("Inside Library: only Caches and Logs can be moved from here"))
        #expect(gate.check(cachesItself, rule: HomeItemRule(), expected: nil) == .refused("Inside Library: only Caches and Logs can be moved from here"))
    }

    @Test func refusesATargetThatIsASymlink() throws {
        let home = try TempHome()
        let pictures = try home.dir("Documents/real")
        let link = try home.link("Documents/alias", to: pictures)
        #expect(home.gate().check(link, rule: HomeItemRule(), expected: nil) == .refused("Is a symbolic link; links are never followed for deletion"))
    }

    @Test func refusesASymlinkedParentThatEscapesHome() throws {
        let home = try TempHome()
        let outside = try TempHome()
        try outside.file("stuff/big.bin")
        _ = try home.link("Documents/outside", to: outside.url)
        let target = home.url.appending(path: "Documents/outside/stuff/big.bin")
        #expect(home.gate().check(target, rule: HomeItemRule(), expected: nil) == .refused("Outside the home folder"))
    }

    @Test func refusesASymlinkedParentThatLeadsIntoProtectedData() throws {
        let home = try TempHome()
        let protected = try home.dir("Pictures/Holiday")
        try home.file("Pictures/Holiday/a.jpg")
        _ = try home.link("Documents/shortcut", to: protected)
        let target = home.url.appending(path: "Documents/shortcut/a.jpg")
        let decision = home.gate().check(target, rule: HomeItemRule(), expected: nil)
        guard case .refused(let reason) = decision else { Issue.record("expected refusal"); return }
        #expect(reason.hasPrefix("Protected"))
    }

    // MARK: Never touch

    @Test(arguments: [
        ".claude/projects/session", ".ssh/id", ".gnupg/key", ".Trash/old", "Pictures/x",
        "Library/Keychains/login", "Library/Mail/V10", "Library/Messages/chat.db",
        "Library/Mobile Documents/com~apple~CloudDocs", "Library/CloudStorage/Dropbox",
        "Library/Calendars/x", "Library/Accounts/x",
        "Library/Application Support/MobileSync/Backup/device",
        "Library/Application Support/com.apple.TCC/TCC.db",
        "Library/Application Support/AddressBook/Sources",
        "Library/Safari/History", "Library/Containers/com.apple.Safari/Data",
        "Library/Application Support/Google/Chrome/Default", "Library/Application Support/Arc/User Data",
        "Library/Application Support/Firefox/Profiles", "Library/Application Support/BraveSoftware/Brave",
        "Library/Application Support/Microsoft Edge/Default",
    ])
    func refusesEveryNeverTouchEntryUnderEveryRule(relative: String) throws {
        let home = try TempHome()
        let target = try home.file(relative + "/item")
        for rule in [any AllowedRootRule](arrayLiteral: HomeItemRule(), LeftoverRule()) {
            let decision = home.gate().check(target, rule: rule, expected: stamp(target))
            guard case .refused(let reason) = decision else { Issue.record("\(relative) allowed as \(rule)"); continue }
            #expect(reason.hasPrefix("Protected"), "\(relative): \(reason)")
        }
    }

    @Test(arguments: ["Library/Containers/com.apple.Notes", "Library/Group Containers/group.com.apple.notes", "Library/Application Support/com.apple.sharedfilelist"])
    func refusesAppleAppData(relative: String) throws {
        let home = try TempHome()
        let target = try home.dir(relative)
        #expect(home.gate().check(target, rule: LeftoverRule(), expected: stamp(target)) == .refused("Protected: Apple app data"))
    }

    @Test func refusesAParentOfProtectedData() throws {
        let home = try TempHome()
        try home.dir("Library/Application Support/Google/Chrome/Default")
        let parent = home.url.appending(path: "Library/Application Support/Google")
        let decision = home.gate().check(parent, rule: LeftoverRule(), expected: nil)
        guard case .refused(let reason) = decision else { Issue.record("expected refusal"); return }
        #expect(reason.hasPrefix("Contains protected data"))
    }

    @Test func refusesProtectedLibraryComponentsAnywhere() throws {
        let home = try TempHome()
        let library = try home.file("Documents/Old.photoslibrary/database/x")
        #expect(home.gate().check(library, rule: HomeItemRule(), expected: nil) == .refused("Protected library or backup"))
    }

    /// Firmlinks give one inode two spellings that `realpath` does not collapse. The identity check catches
    /// a spelling the string rules do not recognize.
    @Test func refusesAProtectedItemReachedByAnotherSpelling() throws {
        let home = try TempHome()
        let ssh = try home.dir(".ssh")
        let gate = home.gate()
        let identity = try #require(FileIdentity.of(ssh.path))
        #expect(gate.protectionReason(real: "/Some/Other/Spelling/.ssh", identity: identity) != nil)
    }

    /// Read-only check against this Mac: the Data-volume spelling of a protected folder deep enough to pass the
    /// depth rule (`~/Library/Keychains`, present on every Mac) is refused as protected.
    @Test func refusesTheDataVolumeSpellingOfARealProtectedFolder() throws {
        let realHome = FileManager.default.homeDirectoryForCurrentUser
        let keychains = try #require(PathCanonicalizer.shared.canonical(realHome.appending(path: "Library/Keychains").path))
        let spelled = URL(filePath: PathCanonicalizer.dataVolumePrefix + keychains)
        for rule in [any AllowedRootRule](arrayLiteral: HomeItemRule(), LeftoverRule()) {
            let decision = SafetyGate(home: realHome).check(spelled, rule: rule, expected: nil)
            guard case .refused(let reason) = decision else { Issue.record("Keychains allowed via the Data volume"); return }
            #expect(reason.hasPrefix("Protected"), "\(reason)")
        }
    }

    @Test func productionDefaultsRefuseSystemLocations() throws {
        let temp = try TempHome()
        let file = try temp.file("Documents/a/b.bin")
        let decision = SafetyGate(home: temp.url).check(file, rule: HomeItemRule(), expected: stamp(file))
        #expect(decision == .refused("Protected: system location /private"))
    }

    // MARK: Identity since the scan

    @Test func refusesAFileChangedSinceTheScan() throws {
        let home = try TempHome()
        let file = try home.file("Documents/a/report.pdf", bytes: 100)
        let scanned = try #require(stamp(file))
        try TempHome.write(file, bytes: 200)
        #expect(home.gate().check(file, rule: HomeItemRule(), expected: scanned) == .refused("Changed since the scan — scan again"))
    }

    @Test func refusesAFileReplacedSinceTheScan() throws {
        let home = try TempHome()
        let file = try home.file("Documents/a/report.pdf", bytes: 100)
        let scanned = try #require(stamp(file))
        try FileManager.default.removeItem(at: file)
        try home.file("Documents/a/report.pdf", bytes: 100)
        #expect(home.gate().check(file, rule: HomeItemRule(), expected: scanned) == .refused("Changed since the scan — scan again"))
    }

    @Test func refusesMissingPaths() throws {
        let home = try TempHome()
        #expect(home.gate().check(home.url.appending(path: "Documents/gone"), rule: HomeItemRule(), expected: nil) == .refused("Already gone"))
    }

    // MARK: Application bundles

    @Test func allowsAWritableAppInAnApplicationsFolderOrOneSubfolderBelow() throws {
        let home = try TempHome()
        let direct = try home.app("Tool.app", in: home.applications)
        let nested = try home.app("Other.app", in: home.applications.appending(path: "Utilities"))
        #expect(home.gate().check(direct, rule: home.appRule(), expected: stamp(direct)).isAllowed)
        #expect(home.gate().check(nested, rule: home.appRule(), expected: stamp(nested)).isAllowed)
    }

    @Test func refusesAppsTwoFoldersDeepOutsideTheFoldersOrWithoutInfoPlist() throws {
        let home = try TempHome()
        let deep = try home.app("Deep.app", in: home.applications.appending(path: "A/B"))
        let elsewhere = try home.app("Loose.app", in: home.url.appending(path: "Downloads"))
        let iOSStyle = home.applications.appending(path: "Phone.app")
        try FileManager.default.createDirectory(at: iOSStyle.appending(path: "Wrapper"), withIntermediateDirectories: true)
        let gate = home.gate()
        #expect(gate.check(deep, rule: home.appRule(), expected: nil) == .refused("Not in an Applications folder"))
        #expect(gate.check(elsewhere, rule: home.appRule(), expected: nil) == .refused("Not in an Applications folder"))
        #expect(gate.check(iOSStyle, rule: home.appRule(), expected: nil) == .refused("Not a Mac app (remove it in Finder)"))
    }

    @Test func refusesAnAppTheUserCannotWrite() throws {
        let home = try TempHome()
        let app = try home.app("RootOwned.app", in: home.applications)
        let blocked = app.path
        let rule = home.appRule(isWritable: { $0 != PathCanonicalizer.shared.canonical(blocked) })
        #expect(home.gate().check(app, rule: rule, expected: nil) == .refused("Needs admin: remove it in Finder"))
    }

    // MARK: Leftovers

    @Test func leftoversMustBeDirectChildrenOfLeftoverFolders() throws {
        let home = try TempHome()
        let direct = try home.dir("Library/Caches/com.example.tool")
        let nested = try home.dir("Library/Caches/com.example.tool/sub")
        let other = try home.dir("Library/Developer/com.example.tool")
        let gate = home.gate()
        #expect(gate.check(direct, rule: LeftoverRule(), expected: stamp(direct)).isAllowed)
        #expect(gate.check(nested, rule: LeftoverRule(), expected: nil) == .refused("Not in a leftover folder"))
        #expect(gate.check(other, rule: LeftoverRule(), expected: nil) == .refused("Not in a leftover folder"))
    }
}

@Suite("PathCanonicalizer")
struct PathCanonicalizerTests {
    let table = "/Applications\tApplications\n/Users\tUsers\n/usr/local\tusr/local\n/System/Library/Caches\tSystem/Library/Caches\n"

    @Test func mapsDataVolumePathsToTheirFirmlinkedForm() {
        let c = PathCanonicalizer(firmlinksTable: table)
        #expect(c.visiblePath("/System/Volumes/Data/Users/x/file") == "/Users/x/file")
        #expect(c.visiblePath("/System/Volumes/Data/Users") == "/Users")
        #expect(c.visiblePath("/System/Volumes/Data/usr/local/bin") == "/usr/local/bin")
    }

    @Test func leavesOtherPathsAlone() {
        let c = PathCanonicalizer(firmlinksTable: table)
        #expect(c.visiblePath("/System/Volumes/Data/Usersfoo/x") == "/System/Volumes/Data/Usersfoo/x")
        #expect(c.visiblePath("/System/Volumes/Data/private/var") == "/System/Volumes/Data/private/var")
        #expect(c.visiblePath("/Users/x") == "/Users/x")
    }

    @Test func realTableCollapsesTheHomeFolder() {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        #expect(PathCanonicalizer.shared.canonical(PathCanonicalizer.dataVolumePrefix + home) == PathCanonicalizer.shared.canonical(home))
    }
}

@Suite("FileStamp")
struct FileStampTests {
    @Test func handlesFilesDatedIn1970AndBefore2001() throws {
        let home = try TempHome()
        let old = try home.file("Documents/old.txt")
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSince1970: 0)], ofItemAtPath: old.path)
        let older = try home.file("Documents/older.txt")
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSince1970: -86_400)], ofItemAtPath: older.path)
        #expect(stamp(old)?.modifiedNanos == 0)
        #expect((stamp(older)?.modifiedNanos ?? 0) < 0)
    }

    @Test func foldersIgnoreSizeButNotModificationTime() throws {
        let home = try TempHome()
        let folder = try home.dir("Documents/f")
        let before = try #require(stamp(folder))
        var resized = before
        resized.size += 64
        #expect(before.isUnchanged(comparedTo: resized))
        var touched = before
        touched.modifiedNanos += 1
        #expect(!before.isUnchanged(comparedTo: touched))
    }
}
