import Darwin
import Foundation

/// Runs a system tool by absolute path in its own process group, so Ctrl-C at the terminal reaches only this tool,
/// never `hdiutil detach` in the middle of its work. stdin bytes are written exactly (no terminator).
enum Spawn {
    struct Result { var status: Int32; var output: Data }

    static func run(_ arguments: [String], stdin: [UInt8]? = nil, captureOutput: Bool = false) throws -> Result {
        var attributes: posix_spawnattr_t?
        posix_spawnattr_init(&attributes)
        defer { posix_spawnattr_destroy(&attributes) }
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETPGROUP))
        posix_spawnattr_setpgroup(&attributes, 0)

        var inPipe: [Int32] = [-1, -1], outPipe: [Int32] = [-1, -1]
        guard pipe(&inPipe) == 0, pipe(&outPipe) == 0 else { throw ToolError("Could not start \(arguments[0]).") }
        var actions: posix_spawn_file_actions_t?
        posix_spawn_file_actions_init(&actions)
        defer { posix_spawn_file_actions_destroy(&actions) }
        posix_spawn_file_actions_adddup2(&actions, inPipe[0], 0)
        if captureOutput {
            posix_spawn_file_actions_adddup2(&actions, outPipe[1], 1)
        } else {
            posix_spawn_file_actions_addopen(&actions, 1, "/dev/null", O_WRONLY, 0)
        }
        posix_spawn_file_actions_addopen(&actions, 2, "/dev/null", O_WRONLY, 0)
        for fd in inPipe + outPipe { posix_spawn_file_actions_addclose(&actions, fd) }

        let argv = arguments.map { strdup($0) } + [nil]
        defer { argv.forEach { free($0) } }
        var pid: pid_t = 0
        let spawned = posix_spawn(&pid, arguments[0], &actions, &attributes, argv, environ)
        close(inPipe[0]); close(outPipe[1])
        guard spawned == 0 else {
            close(inPipe[1]); close(outPipe[0])
            throw ToolError("Could not start \(arguments[0]).")
        }
        if let stdin { _ = stdin.withUnsafeBytes { write(inPipe[1], $0.baseAddress, $0.count) } }
        close(inPipe[1])
        var output = Data()
        if captureOutput {
            var buffer = [UInt8](repeating: 0, count: 65_536)
            while true {
                let n = read(outPipe[0], &buffer, buffer.count)
                if n <= 0 { break }
                output.append(contentsOf: buffer[0 ..< n])
            }
        }
        close(outPipe[0])
        var status: Int32 = 0
        while waitpid(pid, &status, 0) == -1 && errno == EINTR {}
        let exitCode = (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F)
        return Result(status: exitCode, output: output)
    }
}
