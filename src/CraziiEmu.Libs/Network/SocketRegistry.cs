// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.Libs.Network;

/// <summary>
/// Unified thread-safe socket registry shared across libkernel and libSceNet.
/// Implements full descriptor parity matching KytyPS5 Libs::Network::Net architecture.
/// </summary>
public static class SocketRegistry
{
    public sealed class EmulatedSocket
    {
        public int Fd { get; init; }
        public int Family { get; set; }
        public int Type { get; set; }
        public int Protocol { get; set; }
        public Socket? NativeSocket { get; set; }
        public TcpClient? Client { get; set; }
        public NetworkStream? Stream { get; set; }
        public IPAddress BoundAddress { get; set; } = IPAddress.Any;
        public int BoundPort { get; set; }
        public bool Bound { get; set; }
        public bool Connected { get; set; }
        public bool NonBlocking { get; set; }
        public int LastError { get; set; }
        public bool ReuseAddress { get; set; }
        public bool KeepAlive { get; set; }
        public bool Broadcast { get; set; }
        public bool ReusePort { get; set; }
        public bool IPv6Only { get; set; }
        public bool NoDelay { get; set; }
        public int SendBufferSize { get; set; }
        public int ReceiveBufferSize { get; set; }
        public int SendLowWater { get; set; } = 1;
        public int ReceiveLowWater { get; set; } = 1;

        public void SetNonBlocking(bool nonBlocking)
        {
            NonBlocking = nonBlocking;
            if (NativeSocket is not null)
            {
                NativeSocket.Blocking = !nonBlocking;
            }
            if (Client is not null)
            {
                Client.Client.Blocking = !nonBlocking;
            }
        }

        public bool IsNonBlocking()
        {
            if (NativeSocket is not null)
            {
                return !NativeSocket.Blocking || NonBlocking;
            }
            if (Client is not null)
            {
                return !Client.Client.Blocking || NonBlocking;
            }
            return NonBlocking;
        }

        public void Dispose()
        {
            try { Stream?.Dispose(); } catch { }
            try { Client?.Dispose(); } catch { }
            try { NativeSocket?.Dispose(); } catch { }
            Stream = null;
            Client = null;
            NativeSocket = null;
            Connected = false;
            Bound = false;
        }
    }

    private static readonly ConcurrentDictionary<int, EmulatedSocket> _sockets = new();

    public static bool TryCreateNativeSocket(int family, int type, int protocol, out Socket? socket)
    {
        socket = null;
        var addressFamily = family switch
        {
            2 => AddressFamily.InterNetwork,      // AF_INET
            28 => AddressFamily.InterNetworkV6,   // AF_INET6
            _ => AddressFamily.Unknown
        };

        if (addressFamily == AddressFamily.Unknown)
        {
            return false;
        }

        var socketType = type switch
        {
            1 => SocketType.Stream,
            2 => SocketType.Dgram,
            3 => SocketType.Raw,
            _ => SocketType.Unknown
        };

        if (socketType == SocketType.Unknown)
        {
            return false;
        }

        var protocolType = protocol switch
        {
            0 when socketType == SocketType.Stream => ProtocolType.Tcp,
            0 when socketType == SocketType.Dgram => ProtocolType.Udp,
            6 => ProtocolType.Tcp,
            17 => ProtocolType.Udp,
            _ => ProtocolType.Unknown
        };

        try
        {
            socket = new Socket(addressFamily, socketType, protocolType);
            if (socketType == SocketType.Dgram && OperatingSystem.IsWindows())
            {
                // Disable WSAECONNRESET on UDP sockets on Windows (ICMP port unreachable)
                const int SIO_UDP_CONNRESET = -1744830452; // 0x9800000C
                socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
            }
            return true;
        }
        catch
        {
            socket = null;
            return false;
        }
    }

    public static int Allocate(int family, int type, int protocol, Socket? nativeSocket = null)
    {
        if (nativeSocket is null)
        {
            TryCreateNativeSocket(family, type, protocol, out nativeSocket);
        }

        var fd = KernelMemoryCompatExports.AllocateGuestFileDescriptor();
        var sock = new EmulatedSocket
        {
            Fd = fd,
            Family = family,
            Type = type,
            Protocol = protocol,
            NativeSocket = nativeSocket
        };

        if (nativeSocket is not null)
        {
            sock.NonBlocking = !nativeSocket.Blocking;
        }

        _sockets[fd] = sock;
        return fd;
    }

    public static bool TryGet(int fd, out EmulatedSocket? socket)
    {
        return _sockets.TryGetValue(fd, out socket);
    }

    public static bool IsSocket(int fd)
    {
        return _sockets.ContainsKey(fd);
    }

    public static bool TryClose(int fd)
    {
        if (_sockets.TryRemove(fd, out var socket))
        {
            socket.Dispose();
            NetExports.RemoveSocketFromEpolls(fd);
            return true;
        }
        return false;
    }

    public static void Clear()
    {
        foreach (var sock in _sockets.Values)
        {
            sock.Dispose();
        }
        _sockets.Clear();
        NetExports.ClearEpolls();
    }
}
