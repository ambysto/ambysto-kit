import Darwin

/// Thin, non-throwing readers over libproc and sysctl. Every reader returns `nil` when the process is gone or
/// not readable (root's and other users' processes), never an error. Merged from Ambysto Optimer and Space.
/// docs/spec/process.md.
public enum ProcessReader {
    public struct BSDInfo: Sendable, Equatable {
        public var ppid: pid_t
        public var uid: uid_t
        /// Microseconds since 1970.
        public var startTime: UInt64
        public var name: String
        /// `SZOMB` (5) for a process that has exited but not been collected yet.
        public var status: UInt32
        public var isZombie: Bool { status == UInt32(SZOMB) }

        public init(ppid: pid_t, uid: uid_t, startTime: UInt64, name: String, status: UInt32) {
            self.ppid = ppid
            self.uid = uid
            self.startTime = startTime
            self.name = name
            self.status = status
        }
    }

    public struct Usage: Sendable, Equatable {
        public var footprint: UInt64
        public var cpuTicks: UInt64
        public var diskRead: UInt64
        public var diskWritten: UInt64

        public init(footprint: UInt64, cpuTicks: UInt64, diskRead: UInt64, diskWritten: UInt64) {
            self.footprint = footprint
            self.cpuTicks = cpuTicks
            self.diskRead = diskRead
            self.diskWritten = diskWritten
        }
    }

    /// A file a process has open or mapped: its path as the kernel reports it, and its identity.
    public struct VnodeRef: Sendable, Equatable {
        public var path: String
        public var identity: FileIdentity

        public init(path: String, identity: FileIdentity) {
            self.path = path
            self.identity = identity
        }
    }

    public static func allPIDs() -> [pid_t] {
        let estimate = proc_listallpids(nil, 0)
        guard estimate > 0 else { return [] }
        var buffer = [pid_t](repeating: 0, count: Int(estimate) + 64)
        let count = buffer.withUnsafeMutableBytes { raw in proc_listallpids(raw.baseAddress, Int32(raw.count)) }
        guard count > 0 else { return [] }
        return buffer.prefix(Int(count)).filter { $0 > 0 }
    }

    public static func bsdInfo(_ pid: pid_t) -> BSDInfo? {
        var info = proc_bsdinfo()
        let size = Int32(MemoryLayout<proc_bsdinfo>.size)
        guard proc_pidinfo(pid, PROC_PIDTBSDINFO, 0, &info, size) == size else { return nil }
        let name = string(fromTuple: info.pbi_name)
        return BSDInfo(ppid: pid_t(info.pbi_ppid), uid: info.pbi_uid,
                       startTime: info.pbi_start_tvsec &* 1_000_000 &+ info.pbi_start_tvusec,
                       name: name.isEmpty ? string(fromTuple: info.pbi_comm) : name, status: info.pbi_status)
    }

    public static func usage(_ pid: pid_t) -> Usage? {
        var info = rusage_info_v4()
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) { proc_pid_rusage(pid, RUSAGE_INFO_V4, $0) }
        }
        guard result == 0 else { return nil }
        return Usage(footprint: info.ri_phys_footprint, cpuTicks: info.ri_user_time &+ info.ri_system_time,
                     diskRead: info.ri_diskio_bytesread, diskWritten: info.ri_diskio_byteswritten)
    }

    public static func executablePath(_ pid: pid_t) -> String? {
        var buffer = [CChar](repeating: 0, count: Int(MAXPATHLEN) * 4)
        let length = proc_pidpath(pid, &buffer, UInt32(buffer.count))
        guard length > 0 else { return nil }
        return String(decoding: buffer.prefix(Int(length)).map { UInt8(bitPattern: $0) }, as: UTF8.self)
    }

    public static func currentDirectory(_ pid: pid_t) -> String? {
        var info = proc_vnodepathinfo()
        let size = Int32(MemoryLayout<proc_vnodepathinfo>.size)
        guard proc_pidinfo(pid, PROC_PIDVNODEPATHINFO, 0, &info, size) == size else { return nil }
        let path = string(fromTuple: info.pvi_cdir.vip_path)
        return path.isEmpty ? nil : path
    }

    /// The size of the buffer `arguments(_:buffer:)` needs (`kern.argmax`).
    public static let argumentsBufferSize: Int = {
        var argMax: Int32 = 0
        var size = MemoryLayout<Int32>.size
        var mib: [Int32] = [CTL_KERN, KERN_ARGMAX]
        return sysctl(&mib, 2, &argMax, &size, nil, 0) == 0 && argMax > 0 ? Int(argMax) : 1 << 20
    }()

    /// Command-line arguments via `KERN_PROCARGS2` (argv only). Allocates a buffer; callers reading many
    /// processes pass their own reusable buffer to `arguments(_:buffer:)`.
    public static func arguments(_ pid: pid_t) -> [String]? {
        var buffer = [UInt8](repeating: 0, count: argumentsBufferSize)
        return arguments(pid, buffer: &buffer)
    }

    public static func arguments(_ pid: pid_t, buffer: inout [UInt8]) -> [String]? {
        if buffer.count < argumentsBufferSize { buffer = [UInt8](repeating: 0, count: argumentsBufferSize) }
        var size = buffer.count
        var mib: [Int32] = [CTL_KERN, KERN_PROCARGS2, pid]
        let ok = buffer.withUnsafeMutableBytes { raw in sysctl(&mib, 3, raw.baseAddress, &size, nil, 0) }
        guard ok == 0, size > MemoryLayout<Int32>.size else { return nil }
        return parseProcArgs(buffer[0 ..< size])
    }

    /// Layout: `int32 argc`, exec path, NUL padding, then `argc` NUL-terminated strings.
    static func parseProcArgs(_ bytes: ArraySlice<UInt8>) -> [String]? {
        let bytes = Array(bytes)
        guard bytes.count >= 4 else { return nil }
        let argc = bytes.withUnsafeBytes { $0.loadUnaligned(as: Int32.self) }
        guard argc > 0 else { return [] }
        var index = 4
        while index < bytes.count, bytes[index] != 0 { index += 1 }
        while index < bytes.count, bytes[index] == 0 { index += 1 }
        var args: [String] = []
        while index < bytes.count, args.count < Int(argc) {
            let start = index
            while index < bytes.count, bytes[index] != 0 { index += 1 }
            args.append(String(decoding: bytes[start ..< index], as: UTF8.self))
            index += 1
        }
        return args
    }

    /// File descriptors of the process (all kinds). `nil` when unreadable.
    static func fileDescriptors(_ pid: pid_t) -> [proc_fdinfo]? {
        let size = proc_pidinfo(pid, PROC_PIDLISTFDS, 0, nil, 0)
        guard size > 0 else { return nil }
        var list = [proc_fdinfo](repeating: proc_fdinfo(), count: Int(size) / MemoryLayout<proc_fdinfo>.stride + 8)
        let got = list.withUnsafeMutableBytes { raw in proc_pidinfo(pid, PROC_PIDLISTFDS, 0, raw.baseAddress, Int32(raw.count)) }
        guard got > 0 else { return nil }
        return Array(list.prefix(Int(got) / MemoryLayout<proc_fdinfo>.stride))
    }

    /// Files the process has open. `nil` when the process cannot be read.
    public static func openFiles(_ pid: pid_t) -> [VnodeRef]? {
        guard let fds = fileDescriptors(pid) else { return nil }
        var refs: [VnodeRef] = []
        for fd in fds where fd.proc_fdtype == UInt32(PROX_FDTYPE_VNODE) {
            var info = vnode_fdinfowithpath()
            let infoSize = Int32(MemoryLayout<vnode_fdinfowithpath>.size)
            guard proc_pidfdinfo(pid, fd.proc_fd, PROC_PIDFDVNODEPATHINFO, &info, infoSize) == infoSize else { continue }
            let stat = info.pvip.vip_vi.vi_stat
            refs.append(VnodeRef(path: string(fromTuple: info.pvip.vip_path),
                                 identity: FileIdentity(device: UInt64(stat.vst_dev), inode: stat.vst_ino)))
        }
        return refs
    }

    /// Files mapped into the process's memory (libraries, databases). `nil` when unreadable.
    /// Walks every region; about 0.8 s for all processes, so callers snapshot it.
    public static func mappedFiles(_ pid: pid_t) -> [VnodeRef]? {
        var address: UInt64 = 0
        var info = proc_regionwithpathinfo()
        let size = Int32(MemoryLayout<proc_regionwithpathinfo>.size)
        var refs: [VnodeRef] = []
        var seen = Set<FileIdentity>()
        var regions = 0
        while regions < 200_000 {
            let got = proc_pidinfo(pid, PROC_PIDREGIONPATHINFO, address, &info, size)
            guard got == size else { break }
            regions += 1
            let path = string(fromTuple: info.prp_vip.vip_path)
            if !path.isEmpty {
                let stat = info.prp_vip.vip_vi.vi_stat
                let identity = FileIdentity(device: UInt64(stat.vst_dev), inode: stat.vst_ino)
                if seen.insert(identity).inserted { refs.append(VnodeRef(path: path, identity: identity)) }
            }
            let next = info.prp_prinfo.pri_address &+ info.prp_prinfo.pri_size
            guard next > address else { break }
            address = next
        }
        return regions == 0 ? nil : refs   // every live, readable process has regions
    }

    static func string<T>(fromTuple tuple: T) -> String {
        withUnsafeBytes(of: tuple) { raw in String(decoding: raw.prefix { $0 != 0 }, as: UTF8.self) }
    }
}
