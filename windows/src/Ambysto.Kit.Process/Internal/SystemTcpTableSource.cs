using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ambysto.Kit.Processes.Native;

namespace Ambysto.Kit.Processes.Internal;

internal sealed class SystemTcpTableSource : ITcpTableSource
{
    public SystemTcpTableSource()
    {
        if (Marshal.SizeOf<ProcessNativeMethods.MibTcpRowOwnerPid>() != 24)
        {
            throw new InvalidOperationException("Unexpected MIB_TCPROW_OWNER_PID struct size.");
        }

        if (Marshal.SizeOf<ProcessNativeMethods.MibTcp6RowOwnerPid>() != 56)
        {
            throw new InvalidOperationException("Unexpected MIB_TCP6ROW_OWNER_PID struct size.");
        }
    }

    public IReadOnlyList<RawTcpListenerRow> ReadListeners()
    {
        var list = new List<RawTcpListenerRow>(64);
        ReadIPv4Listeners(list);
        ReadIPv6Listeners(list);
        return list;
    }

    private static void ReadIPv4Listeners(List<RawTcpListenerRow> destination)
    {
        nint table = QueryTcpTableBuffer(ProcessNativeMethods.AfInet);
        if (table == 0)
        {
            return;
        }

        try
        {
            uint count = (uint)Marshal.ReadInt32(table, 0);
            int rowSize = Marshal.SizeOf<ProcessNativeMethods.MibTcpRowOwnerPid>();
            nint rowPtr = table + sizeof(uint);

            for (uint i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<ProcessNativeMethods.MibTcpRowOwnerPid>(rowPtr);
                if (row.State == ProcessNativeMethods.MibTcpStateListen)
                {
                    byte[] addrBytes =
                    [
                        (byte)(row.LocalAddr & 0xFFu),
                        (byte)((row.LocalAddr >> 8) & 0xFFu),
                        (byte)((row.LocalAddr >> 16) & 0xFFu),
                        (byte)((row.LocalAddr >> 24) & 0xFFu),
                    ];

                    destination.Add(new RawTcpListenerRow(
                        Family: SocketAddressFamily.IPv4,
                        LocalAddressBytes: addrBytes,
                        ScopeId: 0,
                        RawPortDword: row.LocalPort,
                        OwningProcessId: unchecked((int)row.OwningPid)));
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    private static unsafe void ReadIPv6Listeners(List<RawTcpListenerRow> destination)
    {
        nint table = QueryTcpTableBuffer(ProcessNativeMethods.AfInet6);
        if (table == 0)
        {
            return;
        }

        try
        {
            uint count = (uint)Marshal.ReadInt32(table, 0);
            int rowSize = Marshal.SizeOf<ProcessNativeMethods.MibTcp6RowOwnerPid>();
            nint rowPtr = table + sizeof(uint);

            for (uint i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<ProcessNativeMethods.MibTcp6RowOwnerPid>(rowPtr);
                if (row.State == ProcessNativeMethods.MibTcpStateListen)
                {
                    byte[] addrBytes = new byte[16];
                    for (int b = 0; b < 16; b++)
                    {
                        addrBytes[b] = row.LocalAddr[b];
                    }

                    destination.Add(new RawTcpListenerRow(
                        Family: SocketAddressFamily.IPv6,
                        LocalAddressBytes: addrBytes,
                        ScopeId: row.LocalScopeId,
                        RawPortDword: row.LocalPort,
                        OwningProcessId: unchecked((int)row.OwningPid)));
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    private static nint QueryTcpTableBuffer(uint addressFamily)
    {
        uint size = 0;
        uint result = ProcessNativeMethods.GetExtendedTcpTable(
            0,
            ref size,
            bOrder: false,
            addressFamily,
            ProcessNativeMethods.TcpTableOwnerPidListener,
            reserved: 0);

        if (result != 0 && result != ProcessNativeMethods.ErrorInsufficientBuffer)
        {
            return 0;
        }

        uint allocSize = Math.Max(size + 1024u, 4096u);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal((int)allocSize);
            uint currentSize = allocSize;
            result = ProcessNativeMethods.GetExtendedTcpTable(
                buffer,
                ref currentSize,
                bOrder: false,
                addressFamily,
                ProcessNativeMethods.TcpTableOwnerPidListener,
                reserved: 0);

            if (result == 0)
            {
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);
            if (result != ProcessNativeMethods.ErrorInsufficientBuffer)
            {
                return 0;
            }

            allocSize = Math.Max(allocSize * 2, currentSize + 1024u);
        }

        return 0;
    }
}
