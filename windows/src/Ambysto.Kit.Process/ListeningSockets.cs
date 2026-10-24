using System;
using System.Collections.Generic;

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
        throw new NotImplementedException();
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
    public string Address { get; } = string.Empty;

    /// <summary>Address class (Any, Loopback or Specific).</summary>
    public SocketAddressClass AddressClass { get; }

    /// <summary>System socket handle id (always null on Windows).</summary>
    public ulong? SocketId { get; }
}

/// <summary>Reads listening TCP sockets from <c>GetExtendedTcpTable</c> (docs/spec/process.md, "Listening sockets").</summary>
public sealed class ListeningSockets
{
    /// <summary>Creates a reader over the live Windows TCP listener tables.</summary>
    public ListeningSockets()
    {
        throw new NotImplementedException();
    }

    internal ListeningSockets(ITcpTableSource source)
    {
        throw new NotImplementedException();
    }

    /// <summary>Returns all listening TCP sockets owned by readable processes in <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The process snapshot to match against.</param>
    /// <returns>Listening sockets of readable processes.</returns>
    public IReadOnlyList<ListeningSocket> ReadForReadableProcesses(ProcessSnapshot snapshot)
    {
        throw new NotImplementedException();
    }

    /// <summary>Returns all listening TCP sockets on the machine (with owner identity and short name populated when the owner is in <paramref name="snapshot"/>).</summary>
    /// <param name="snapshot">The process snapshot to match against.</param>
    /// <returns>All listening TCP sockets with positive owner process ids.</returns>
    public IReadOnlyList<ListeningSocket> ReadAll(ProcessSnapshot snapshot)
    {
        throw new NotImplementedException();
    }
}
