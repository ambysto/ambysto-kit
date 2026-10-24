using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Processes.Tests;

[TestClass]
public sealed class ListeningSocketsTests
{
    private const string CurrentUserSid = "S-1-5-21-1111-2222-3333-1001";
    private const long BaseTicks = 133_700_000_000_000_000L;
    private static readonly string[] ServerArguments = ["server.exe"];
    private static readonly string[] DevServerArguments = ["dev-server.exe"];
    private static readonly char[] LineSeparators = ['\r', '\n'];

    [TestMethod]
    public void SeamAddressClassificationFormattingAndDirtyPortUpperBitsAreDecodedAccurately()
    {
        var readableOwner = ProcessRecord.Readable(
            identity: new ProcessIdentity(1000, BaseTicks),
            parentProcessId: null,
            name: "server.exe",
            sessionId: 1,
            ownerSid: CurrentUserSid,
            executablePath: @"C:\App\server.exe",
            workingDirectory: @"C:\App",
            commandLine: "server.exe",
            arguments: ServerArguments,
            usage: new ProcessCounters(4096, 1000, 0, 0));

        var snapshot = new ProcessSnapshot([readableOwner], unreadableProcessCount: 0);

        RawTcpListenerRow[] rows =
        [
            // IPv4 wildcard 0.0.0.0:80 with dirty upper 16 bits in RawPortDword
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Any, scopeId: 0, port: 80, dirtyUpperBits: 0xDEAD, pid: 1000),
            // IPv4 loopback 127.0.0.1:8080 and 127.0.0.53:53
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Loopback, scopeId: 0, port: 8080, dirtyUpperBits: 0xBEEF, pid: 1000),
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Parse("127.0.0.53"), scopeId: 0, port: 53, dirtyUpperBits: 0, pid: 1000),
            // IPv4 specific interface 192.168.1.10 and 10.0.0.5
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Parse("192.168.1.10"), scopeId: 0, port: 3000, dirtyUpperBits: 0, pid: 1000),
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Parse("10.0.0.5"), scopeId: 0, port: 3001, dirtyUpperBits: 0, pid: 1000),
            // IPv6 wildcard [::]:443
            CreateRow(SocketAddressFamily.IPv6, IPAddress.IPv6Any, scopeId: 0, port: 443, dirtyUpperBits: 0xFFFF, pid: 1000),
            // IPv6 loopback [::1]:8443
            CreateRow(SocketAddressFamily.IPv6, IPAddress.IPv6Loopback, scopeId: 0, port: 8443, dirtyUpperBits: 0, pid: 1000),
            // IPv4-mapped IPv6 loopback ::ffff:127.0.0.1
            CreateRow(SocketAddressFamily.IPv6, IPAddress.Loopback.MapToIPv6(), scopeId: 0, port: 8444, dirtyUpperBits: 0, pid: 1000),
            // IPv4-mapped IPv6 specific ::ffff:192.168.1.10
            CreateRow(SocketAddressFamily.IPv6, IPAddress.Parse("192.168.1.10").MapToIPv6(), scopeId: 0, port: 8445, dirtyUpperBits: 0, pid: 1000),
            // Scoped link-local IPv6 fe80::1%12
            CreateRow(SocketAddressFamily.IPv6, IPAddress.Parse("fe80::1"), scopeId: 12, port: 9000, dirtyUpperBits: 0x1234, pid: 1000),
            // Global IPv6 2001:db8::1 (ScopeId 0 -> no %0 suffix)
            CreateRow(SocketAddressFamily.IPv6, IPAddress.Parse("2001:db8::1"), scopeId: 0, port: 9001, dirtyUpperBits: 0, pid: 1000),
        ];

        var sockets = new ListeningSockets(new FakeTcpTableSource(rows));
        IReadOnlyList<ListeningSocket> result = sockets.ReadForReadableProcesses(snapshot);

        Assert.AreEqual(rows.Length, result.Count);
        Dictionary<ushort, ListeningSocket> byPort = result.ToDictionary(s => s.Port);

        Assert.AreEqual("0.0.0.0", byPort[80].Address);
        Assert.AreEqual(SocketAddressClass.Any, byPort[80].AddressClass);
        Assert.AreEqual(SocketAddressFamily.IPv4, byPort[80].Family);
        Assert.IsNull(byPort[80].SocketId);

        Assert.AreEqual("127.0.0.1", byPort[8080].Address);
        Assert.AreEqual(SocketAddressClass.Loopback, byPort[8080].AddressClass);

        Assert.AreEqual("127.0.0.53", byPort[53].Address);
        Assert.AreEqual(SocketAddressClass.Loopback, byPort[53].AddressClass);

        Assert.AreEqual("192.168.1.10", byPort[3000].Address);
        Assert.AreEqual(SocketAddressClass.Specific, byPort[3000].AddressClass);

        Assert.AreEqual("10.0.0.5", byPort[3001].Address);
        Assert.AreEqual(SocketAddressClass.Specific, byPort[3001].AddressClass);

        Assert.AreEqual("::", byPort[443].Address);
        Assert.AreEqual(SocketAddressClass.Any, byPort[443].AddressClass);
        Assert.AreEqual(SocketAddressFamily.IPv6, byPort[443].Family);

        Assert.AreEqual("::1", byPort[8443].Address);
        Assert.AreEqual(SocketAddressClass.Loopback, byPort[8443].AddressClass);

        Assert.AreEqual(SocketAddressClass.Loopback, byPort[8444].AddressClass);
        Assert.AreEqual(SocketAddressClass.Specific, byPort[8445].AddressClass);

        Assert.AreEqual("fe80::1%12", byPort[9000].Address);
        Assert.AreEqual(SocketAddressClass.Specific, byPort[9000].AddressClass);

        Assert.AreEqual("2001:db8::1", byPort[9001].Address);
        Assert.AreEqual(SocketAddressClass.Specific, byPort[9001].AddressClass);
    }

    [TestMethod]
    public void SeamReadForReadableProcessesVsReadAllFiltersUnreadableExitedAndZeroPidRows()
    {
        var readableOwner = ProcessRecord.Readable(
            identity: new ProcessIdentity(1000, BaseTicks),
            parentProcessId: null,
            name: "dev-server.exe",
            sessionId: 1,
            ownerSid: CurrentUserSid,
            executablePath: @"C:\App\dev-server.exe",
            workingDirectory: @"C:\App",
            commandLine: "dev-server.exe",
            arguments: DevServerArguments,
            usage: new ProcessCounters(4096, 1000, 0, 0));

        var unreadableSystem = ProcessRecord.Unreadable(
            identity: new ProcessIdentity(4, BaseTicks - 10_000),
            parentProcessId: null,
            name: "System",
            sessionId: 0);

        var snapshot = new ProcessSnapshot([readableOwner, unreadableSystem], unreadableProcessCount: 1);

        RawTcpListenerRow[] rows =
        [
            // OwningProcessId == 0 must be ignored by both methods
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Any, scopeId: 0, port: 99, dirtyUpperBits: 0, pid: 0),
            // Readable owner (PID 1000)
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Loopback, scopeId: 0, port: 5173, dirtyUpperBits: 0, pid: 1000),
            // Unreadable owner in snapshot (PID 4)
            CreateRow(SocketAddressFamily.IPv4, IPAddress.Any, scopeId: 0, port: 445, dirtyUpperBits: 0, pid: 4),
            // Exited owner not in snapshot (PID 9999)
            CreateRow(SocketAddressFamily.IPv6, IPAddress.IPv6Loopback, scopeId: 0, port: 6000, dirtyUpperBits: 0, pid: 9999),
        ];

        var sockets = new ListeningSockets(new FakeTcpTableSource(rows));

        IReadOnlyList<ListeningSocket> readableOnly = sockets.ReadForReadableProcesses(snapshot);
        Assert.AreEqual(1, readableOnly.Count);
        Assert.AreEqual(5173, readableOnly[0].Port);
        Assert.AreEqual(1000, readableOnly[0].ProcessId);
        Assert.AreEqual(readableOwner.Identity, readableOnly[0].ProcessIdentity);
        Assert.AreEqual("dev-server.exe", readableOnly[0].ProcessName);
        Assert.IsTrue(readableOnly[0].IsOwnerReadable);

        IReadOnlyList<ListeningSocket> all = sockets.ReadAll(snapshot);
        Assert.AreEqual(3, all.Count);
        Dictionary<ushort, ListeningSocket> byPort = all.ToDictionary(s => s.Port);

        Assert.AreEqual(readableOwner.Identity, byPort[5173].ProcessIdentity);
        Assert.AreEqual("dev-server.exe", byPort[5173].ProcessName);
        Assert.IsTrue(byPort[5173].IsOwnerReadable);

        Assert.AreEqual(unreadableSystem.Identity, byPort[445].ProcessIdentity);
        Assert.AreEqual("System", byPort[445].ProcessName);
        Assert.IsFalse(byPort[445].IsOwnerReadable);

        Assert.AreEqual(9999, byPort[6000].ProcessId);
        Assert.IsNull(byPort[6000].ProcessIdentity);
        Assert.IsNull(byPort[6000].ProcessName);
        Assert.IsFalse(byPort[6000].IsOwnerReadable);
    }

    [TestMethod]
    public void RealSocketsBoundTcpListenersAndNetstatComparisonMatchAcrossThreeConsecutiveRuns()
    {
        var listeners = new List<(TcpListener Listener, SocketAddressFamily Family, string ExpectedAddress, SocketAddressClass ExpectedClass)>();
        try
        {
            BindListener(listeners, IPAddress.Loopback, SocketAddressFamily.IPv4, "127.0.0.1", SocketAddressClass.Loopback);
            BindListener(listeners, IPAddress.Any, SocketAddressFamily.IPv4, "0.0.0.0", SocketAddressClass.Any);
            BindListener(listeners, IPAddress.IPv6Loopback, SocketAddressFamily.IPv6, "::1", SocketAddressClass.Loopback);
            BindListener(listeners, IPAddress.IPv6Any, SocketAddressFamily.IPv6, "::", SocketAddressClass.Any);

            IPAddress? linkLocalV6 = FindOperationalLinkLocalIPv6Address();
            if (linkLocalV6 is not null)
            {
                BindListener(listeners, linkLocalV6, SocketAddressFamily.IPv6, linkLocalV6.ToString(), SocketAddressClass.Specific);
            }

            var processReader = new ProcessReader();
            var socketsReader = new ListeningSockets();

            ProcessSnapshot snapshot = processReader.ReadSnapshot();
            ProcessRecord self = snapshot.Processes.Single(p => p.Identity.ProcessId == Environment.ProcessId);

            IReadOnlyList<ListeningSocket> readableSockets = socketsReader.ReadForReadableProcesses(snapshot);
            foreach (var (listener, family, expectedAddress, expectedClass) in listeners)
            {
                ushort boundPort = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
                ListeningSocket? match = readableSockets.SingleOrDefault(s =>
                    s.ProcessId == Environment.ProcessId &&
                    s.Family == family &&
                    s.Port == boundPort &&
                    string.Equals(s.Address, expectedAddress, StringComparison.OrdinalIgnoreCase));

                Assert.IsNotNull(match, $"Expected bound listener {expectedAddress}:{boundPort} ({family}) in ReadForReadableProcesses.");
                Assert.AreEqual(self.Identity, match.ProcessIdentity);
                Assert.AreEqual(self.Name, match.ProcessName);
                Assert.IsTrue(match.IsOwnerReadable);
                Assert.AreEqual(expectedClass, match.AddressClass);
                Assert.IsNull(match.SocketId);
            }

            // Criterion 5: compare the set of listening (family, address, port, pid) tuples from ReadAll
            // against netstat -ano -p tcp and netstat -ano -p tcpv6 across three consecutive runs.
            for (int run = 0; run < 3; run++)
            {
                ProcessSnapshot runSnapshot = processReader.ReadSnapshot();
                var fromReadAll = socketsReader
                    .ReadAll(runSnapshot)
                    .Select(s => (s.Family, Address: s.Address.ToLowerInvariant(), s.Port, s.ProcessId))
                    .ToHashSet();

                var fromNetstat = ReadNetstatListeningSet();
                CollectionAssert.AreEquivalent(
                    fromNetstat.ToArray(),
                    fromReadAll.ToArray(),
                    $"Run {run + 1}: ReadAll tuple set differed from netstat -ano -p tcp/tcpv6.");
            }
        }
        finally
        {
            foreach (var entry in listeners)
            {
                entry.Listener.Stop();
            }
        }
    }

    private static void BindListener(
        List<(TcpListener Listener, SocketAddressFamily Family, string ExpectedAddress, SocketAddressClass ExpectedClass)> list,
        IPAddress address,
        SocketAddressFamily family,
        string expectedAddress,
        SocketAddressClass expectedClass)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        list.Add((listener, family, expectedAddress, expectedClass));
    }

    private static IPAddress? FindOperationalLinkLocalIPv6Address()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    unicast.Address.IsIPv6LinkLocal &&
                    unicast.Address.ScopeId != 0)
                {
                    return unicast.Address;
                }
            }
        }

        return null;
    }

    private static HashSet<(SocketAddressFamily Family, string Address, ushort Port, int ProcessId)> ReadNetstatListeningSet()
    {
        var set = new HashSet<(SocketAddressFamily Family, string Address, ushort Port, int ProcessId)>();
        ParseNetstatOutput(RunNetstat("-ano -p tcp"), SocketAddressFamily.IPv4, set);
        ParseNetstatOutput(RunNetstat("-ano -p tcpv6"), SocketAddressFamily.IPv6, set);
        return set;
    }

    private static string RunNetstat(string arguments)
    {
        string netstatExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "NETSTAT.EXE");
        var psi = new ProcessStartInfo(netstatExe, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using Process proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10_000);
        return output;
    }

    private static void ParseNetstatOutput(
        string output,
        SocketAddressFamily family,
        HashSet<(SocketAddressFamily Family, string Address, ushort Port, int ProcessId)> destination)
    {
        string[] lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawLine in lines)
        {
            string[] tokens = rawLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 5 ||
                !string.Equals(tokens[0], "TCP", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(tokens[3], "LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(tokens[4], out int pid) || pid <= 0)
            {
                continue;
            }

            string localEndpoint = tokens[1];
            int lastColon = localEndpoint.LastIndexOf(':');
            if (lastColon <= 0 || !ushort.TryParse(localEndpoint.AsSpan(lastColon + 1), out ushort port))
            {
                continue;
            }

            string rawAddr = localEndpoint.Substring(0, lastColon).Trim('[', ']');
            string canonicalAddr = IPAddress.Parse(rawAddr).ToString().ToLowerInvariant();
            destination.Add((family, canonicalAddr, port, pid));
        }
    }

    private static RawTcpListenerRow CreateRow(
        SocketAddressFamily family,
        IPAddress address,
        uint scopeId,
        ushort port,
        ushort dirtyUpperBits,
        int pid)
    {
        uint networkOrderLow16 = (uint)(((port & 0xFF00) >> 8) | ((port & 0x00FF) << 8));
        uint rawPortDword = ((uint)dirtyUpperBits << 16) | networkOrderLow16;
        return new RawTcpListenerRow(family, address.GetAddressBytes(), scopeId, rawPortDword, pid);
    }

    private sealed class FakeTcpTableSource : ITcpTableSource
    {
        private readonly IReadOnlyList<RawTcpListenerRow> _rows;

        public FakeTcpTableSource(IReadOnlyList<RawTcpListenerRow> rows)
        {
            _rows = rows;
        }

        public IReadOnlyList<RawTcpListenerRow> ReadListeners() => _rows;
    }
}
