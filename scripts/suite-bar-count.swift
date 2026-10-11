// Counts the menu bar items of processes (suite bar proof). On macOS 26 every item's window belongs to Control
// Center, so CGWindowList cannot say which app owns an item; the Accessibility attribute AXExtrasMenuBar of an
// app does (its children are the app's items). Needs Accessibility permission for the terminal that runs it.
//
//   swift scripts/suite-bar-count.swift <pid>...              one line per pid: "<pid> <count>"
//   suite-bar-count --watch <ms> <pid>...                     (compiled) a line per sample: "<epoch> <total> <count>..."
//
// Prints 0 for a pid without the attribute (error -25212, "no value") or one that is gone.
// Exits 2 when this process is not trusted for Accessibility.
import ApplicationServices
import Foundation

setvbuf(stdout, nil, _IOLBF, 0)

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data("suite-bar-count: \(message)\n".utf8))
    exit(2)
}

func itemCount(pid: pid_t) -> Int {
    let app = AXUIElementCreateApplication(pid)
    var bar: CFTypeRef?
    let error = AXUIElementCopyAttributeValue(app, "AXExtrasMenuBar" as CFString, &bar)
    if error == .apiDisabled { fail("Accessibility is not allowed for this terminal (System Settings > Privacy & Security > Accessibility)") }
    guard error == .success, let bar else { return 0 }   // -25212 = no value: the app has no items
    var children: CFTypeRef?
    guard AXUIElementCopyAttributeValue(bar as! AXUIElement, kAXChildrenAttribute as CFString, &children) == .success,
          let list = children as? [AXUIElement] else { return 0 }
    return list.count
}

var arguments = Array(CommandLine.arguments.dropFirst())
guard AXIsProcessTrusted() else {
    fail("Accessibility is not allowed for this terminal (System Settings > Privacy & Security > Accessibility)")
}

if arguments.first == "--watch" {
    guard arguments.count >= 3, let milliseconds = Double(arguments[1]), milliseconds > 0 else {
        fail("usage: --watch <ms> <pid>...")
    }
    let pids = arguments.dropFirst(2).compactMap { pid_t($0) }
    while true {
        let counts = pids.map { itemCount(pid: $0) }
        let epoch = String(format: "%.3f", Date().timeIntervalSince1970)
        print("\(epoch) \(counts.reduce(0, +)) \(counts.map(String.init).joined(separator: " "))")
        Thread.sleep(forTimeInterval: milliseconds / 1000)
    }
}

let pids = arguments.compactMap { pid_t($0) }
guard !pids.isEmpty, pids.count == arguments.count else { fail("usage: suite-bar-count <pid>...") }
for pid in pids { print("\(pid) \(itemCount(pid: pid))") }
