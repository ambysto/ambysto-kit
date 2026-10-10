import Darwin
import Foundation

@main
struct LicenseTool {
    static func main() {
        var arguments = Array(CommandLine.arguments.dropFirst())
        guard let command = arguments.first else {
            note(Commands.usage)
            exit(2)
        }
        arguments.removeFirst()
        let args = Arguments(arguments)
        var store: any SigningKeyStore = KeychainStore()
        var ledger = Ledger(url: Ledger.defaultURL)
        #if DEBUG
        // Development only: the published test key in a plain file, and a separate ledger.
        if let file = args.value("store-file") { store = FileStore(url: URL(filePath: file)) }
        if let path = args.value("ledger") { ledger = Ledger(url: URL(filePath: path)) }
        #endif
        do {
            try Commands(store: store, ledger: ledger).run(command, args)
        } catch {
            note("\(error)")
            exit(1)
        }
    }
}
