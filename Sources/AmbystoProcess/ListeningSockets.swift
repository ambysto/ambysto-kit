import Darwin

/// A TCP socket in the LISTEN state held by a readable process. docs/spec/process.md → "Listening sockets".
public struct ListeningSocket: Sendable, Hashable {
    public enum AddressClass: String, Sendable, Hashable {
        /// Wildcard (`0.0.0.0`, `::`): reachable from every network.
        case any
        /// Loopback (`127.0.0.0/8`, `::1`, or IPv4-mapped loopback): this Mac only.
        case loopback
        /// A specific interface address.
        case specific
    }

    public var pid: pid_t
    public var fd: Int32
    /// The kernel's handle of the socket: equal for processes sharing one socket (a master and its workers).
    public var socketID: UInt64
    /// Host byte order.
    public var port: UInt16
    /// `AF_INET` or `AF_INET6`.
    public var family: Int32
    public var addressClass: AddressClass
    /// Printable local address (`127.0.0.1`, `::1`, `0.0.0.0`, `::`, `192.168.1.5`, …).
    public var address: String
}

public enum ListeningSockets {
    /// One pass over all readable processes.
    public static func read() -> [ListeningSocket] {
        ProcessReader.allPIDs().flatMap(read(pid:))
    }

    public static func read(pid: pid_t) -> [ListeningSocket] {
        guard let fds = ProcessReader.fileDescriptors(pid) else { return [] }
        var result: [ListeningSocket] = []
        for fd in fds where fd.proc_fdtype == UInt32(PROX_FDTYPE_SOCKET) {
            var info = socket_fdinfo()
            let size = Int32(MemoryLayout<socket_fdinfo>.size)
            guard proc_pidfdinfo(pid, fd.proc_fd, PROC_PIDFDSOCKETINFO, &info, size) == size else { continue }
            guard info.psi.soi_kind == Int32(SOCKINFO_TCP) else { continue }
            let tcp = info.psi.soi_proto.pri_tcp
            guard tcp.tcpsi_state == TSI_S_LISTEN else { continue }
            let ini = tcp.tcpsi_ini
            let port = UInt16(bigEndian: UInt16(truncatingIfNeeded: ini.insi_lport))
            let (address, addressClass) = describe(ini, family: info.psi.soi_family)
            result.append(ListeningSocket(pid: pid, fd: fd.proc_fd, socketID: info.psi.soi_so, port: port,
                                          family: info.psi.soi_family, addressClass: addressClass, address: address))
        }
        return result
    }

    /// IPv4 addresses (including IPv4-mapped ones on IPv6 sockets, told apart by the version flags) live in
    /// `ina_46`; IPv6 addresses in `ina_6`.
    static func describe(_ ini: in_sockinfo, family: Int32) -> (String, ListeningSocket.AddressClass) {
        let isIPv4 = family == AF_INET || (ini.insi_vflag & UInt8(INI_IPV4) != 0 && ini.insi_vflag & UInt8(INI_IPV6) == 0)
        if isIPv4 {
            var address = ini.insi_laddr.ina_46.i46a_addr4
            let host = UInt32(bigEndian: address.s_addr)
            let text = ntop(AF_INET, &address, Int(INET_ADDRSTRLEN))
            let kind: ListeningSocket.AddressClass = host == 0 ? .any : (host >> 24 == 127 ? .loopback : .specific)
            return (text, kind)
        }
        var address = ini.insi_laddr.ina_6
        let bytes = withUnsafeBytes(of: address) { Array($0) }
        let text = ntop(AF_INET6, &address, Int(INET6_ADDRSTRLEN))
        if bytes.allSatisfy({ $0 == 0 }) { return (text, .any) }
        if bytes.prefix(15).allSatisfy({ $0 == 0 }) && bytes[15] == 1 { return (text, .loopback) }
        // ::ffff:a.b.c.d
        if bytes.prefix(10).allSatisfy({ $0 == 0 }) && bytes[10] == 0xFF && bytes[11] == 0xFF {
            return (text, bytes[12] == 127 ? .loopback : (bytes[12 ... 15].allSatisfy { $0 == 0 } ? .any : .specific))
        }
        return (text, .specific)
    }

    private static func ntop<T>(_ family: Int32, _ address: inout T, _ length: Int) -> String {
        var buffer = [CChar](repeating: 0, count: length)
        _ = withUnsafePointer(to: &address) { inet_ntop(family, $0, &buffer, socklen_t(length)) }
        return String(decoding: buffer.prefix { $0 != 0 }.map { UInt8(bitPattern: $0) }, as: UTF8.self)
    }
}
