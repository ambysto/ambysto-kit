using System;
using System.Collections.Generic;
using System.Net;
using Ambysto.Kit.Processes.Internal;

namespace Ambysto.Kit.Processes;

/// <summary>IP address family of a listening TCP socket.</summary>
public enum SocketAddressFamily
{
    /// <summary>IPv4 (<c>AF_INET</c>).</summary>
    IPv4,

    /// <summary>IPv6 (<c>AF_INET6</c>).</summary>
    IPv6,
}

/// <summary>Address classification of a listening TCP socket (docs/spec/process.md, "Listening sockets").</summary>
public enum SocketAddressClass
{
    /// <summary>Wildcard (<c>0.0.0.0</c> or <c>::</c>).</summary>
    Any,

    /// <summary>Loopback (<c>127.0.0.0/8</c>, <c>::1</c>, or IPv4-mapped loopback <c>::ffff:127.x.y.z</c>).</summary>
    Loopback,

    /// <summary>A specific interface address (including link-local IPv6 <c>fe80::…%scope</c>).</summary>
    Specific,
}

/// <summary>A TCP socket in the LISTEN state (docs/spec/process.md, "Listening sockets").</summary>
public sealed class ListeningSocket
{
    /// <summary>Creates a listening socket record.</summary>
    /// <param name="processId">Owning process id (positive).</param>
    /// <param name="processIdentity">Owning process identity when present in the snapshot; otherwise null.</param>
    /// <param name="processName">Owning process short name when present in the snapshot; otherwise null.</param>
    /// <param name="isOwnerReadable">True when the owning process is a readable process in the snapshot.</param>
    /// <param name="port">Local TCP port in host byte order.</param>
    /// <param name="family">IPv4 or IPv6.</param>
    /// <param name="address">Printable local address.</param>
    /// <param name="addressClass">Any, Loopback or Specific.</param>
    /// <param name="socketId">System socket handle id when available (always null on Windows).</param>
    public ListeningSocket(
        int processId,
        ProcessIdentity? processIdentity,
        string? processName,
        bool isOwnerReadable,
        ushort port,
        SocketAddressFamily family,
        string address,
        SocketAddressClass addressClass,
        ulong? socketId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        ProcessId = processId;
        ProcessIdentity = processIdentity;
        ProcessName = processName;
        IsOwnerReadable = isOwnerReadable;
        Port = port;
        Family = family;
        Address = address;
        AddressClass = addressClass;
        SocketId = socketId;
    }

    /// <summary>Owning process id.</summary>
    public int ProcessId { get; }

    /// <summary>Owning process identity when found in the snapshot; otherwise null.</summary>
    public ProcessIdentity? ProcessIdentity { get; }

    /// <summary>Owning process short name when found in the snapshot; otherwise null.</summary>
    public string? ProcessName { get; }

    /// <summary>True when the owning process is readable in the snapshot.</summary>
    public bool IsOwnerReadable { get; }

    /// <summary>Local port in host byte order.</summary>
    public ushort Port { get; }

    /// <summary>Address family (IPv4 or IPv6).</summary>
    public SocketAddressFamily Family { get; }

    /// <summary>Printable local address.</summary>
    public string Address { get; }

    /// <summary>Address class (Any, Loopback or Specific).</summary>
    public SocketAddressClass AddressClass { get; }

    /// <summary>System socket handle id (always null on Windows).</summary>
    public ulong? SocketId { get; }
}

/// <summary>Reads listening TCP sockets from <c>GetExtendedTcpTable</c> (docs/spec/process.md, "Listening sockets").</summary>
public sealed class ListeningSockets
{
    private readonly ITcpTableSource _source;

    /// <summary>Creates a reader over the live Windows TCP listener tables.</summary>
    public ListeningSockets()
        : this(new SystemTcpTableSource())
    {
    }

    internal ListeningSockets(ITcpTableSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Returns all listening TCP sockets owned by readable processes in <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The process snapshot to match against.</param>
    /// <returns>Listening sockets of readable processes.</returns>
    public IReadOnlyList<ListeningSocket> ReadForReadableProcesses(ProcessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ReadCore(snapshot, readableOnly: true);
    }

    /// <summary>Returns all listening TCP sockets on the machine (with owner identity and short name populated when the owner is in <paramref name="snapshot"/>).</summary>
    /// <param name="snapshot">The process snapshot to match against.</param>
    /// <returns>All listening TCP sockets with positive owner process ids.</returns>
    public IReadOnlyList<ListeningSocket> ReadAll(ProcessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ReadCore(snapshot, readableOnly: false);
    }

    private List<ListeningSocket> ReadCore(ProcessSnapshot snapshot, bool readableOnly)
    {
        var ownersByPid = new Dictionary<int, ProcessRecord>(snapshot.Processes.Count);
        foreach (ProcessRecord record in snapshot.Processes)
        {
            if (!ownersByPid.TryGetValue(record.Identity.ProcessId, out ProcessRecord? existing) ||
                record.Identity.StartTimeTicks > existing.Identity.StartTimeTicks)
            {
                ownersByPid[record.Identity.ProcessId] = record;
            }
        }

        IReadOnlyList<RawTcpListenerRow> rows = _source.ReadListeners();
        var results = new List<ListeningSocket>(rows.Count);

        foreach (RawTcpListenerRow row in rows)
        {
            if (row.OwningProcessId <= 0)
            {
                continue;
            }

            bool hasOwner = ownersByPid.TryGetValue(row.OwningProcessId, out ProcessRecord? owner);
            bool isOwnerReadable = hasOwner && owner!.IsReadable;

            if (readableOnly && !isOwnerReadable)
            {
                continue;
            }

            ushort port = DecodePort(row.RawPortDword);
            (string address, SocketAddressClass addressClass) = FormatAndClassifyAddress(
                row.Family,
                row.LocalAddressBytes,
                row.ScopeId);

            results.Add(new ListeningSocket(
                processId: row.OwningProcessId,
                processIdentity: hasOwner ? owner!.Identity : null,
                processName: hasOwner ? owner!.Name : null,
                isOwnerReadable: isOwnerReadable,
                port: port,
                family: row.Family,
                address: address,
                addressClass: addressClass,
                socketId: null));
        }

        return results;
    }

    internal static ushort DecodePort(uint rawPortDword)
    {
        // Only the low 16 bits contain the port in network byte order; upper 16 bits are undefined garbage.
        uint low16 = rawPortDword & 0xFFFFu;
        return (ushort)(((low16 & 0x00FFu) << 8) | ((low16 & 0xFF00u) >> 8));
    }

    private static (string Address, SocketAddressClass AddressClass) FormatAndClassifyAddress(
        SocketAddressFamily family,
        byte[] addressBytes,
        uint scopeId)
    {
        if (family == SocketAddressFamily.IPv4)
        {
            var ip = new IPAddress(addressBytes);
            SocketAddressClass cls;
            if (IPAddress.Any.Equals(ip))
            {
                cls = SocketAddressClass.Any;
            }
            else if (IPAddress.IsLoopback(ip))
            {
                cls = SocketAddressClass.Loopback;
            }
            else
            {
                cls = SocketAddressClass.Specific;
            }

            return (ip.ToString(), cls);
        }
        else
        {
            var ip = new IPAddress(addressBytes, scopeId);

            SocketAddressClass cls;
            if (scopeId == 0 && IPAddress.IPv6Any.Equals(ip))
            {
                cls = SocketAddressClass.Any;
            }
            else if (IPAddress.IsLoopback(ip) ||
                     (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4())))
            {
                cls = SocketAddressClass.Loopback;
            }
            else
            {
                cls = SocketAddressClass.Specific;
            }

            return (ip.ToString(), cls);
        }
    }
}
