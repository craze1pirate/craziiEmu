// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.Libs.Network;

public static class NetExports
{
    private const int NetErrorBadFileDescriptor = unchecked((int)0x80410109);
    private const int NetErrorInvalidArgument = unchecked((int)0x80410116);
    private const int NetErrorWouldBlock = unchecked((int)0x80410123);
    private const int NetErrorAddressInUse = unchecked((int)0x80410130);
    private const int NetErrorNotInitialized = unchecked((int)0x804101C8);
    private const int NetErrnoBadFileDescriptor = 9;
    private const int NetErrnoInvalidArgument = 22;
    private const int NetErrnoWouldBlock = 35;
    private const int NetErrnoAddressInUse = 48;
    private const int NetErrnoNotInitialized = 200;
    private const int MaxNameLength = 256;

    private static readonly ConcurrentDictionary<int, NetPool> _pools = new();
    private static readonly ConcurrentDictionary<int, ResolverContext> _resolvers = new();
    private static int _nextPoolId;
    private static int _nextResolverId = 0x2000;
    // The platform networking module is usable immediately after it is loaded.
    // Games and middleware (notably FMOD) can create internal sockets before an
    // explicit sceNetInit call reaches application code.
    private static bool _initialized = true;

    [ThreadStatic]
    private static nint _errnoAddress;

    private sealed record NetPool(string Name, int Size, int Flags);

    private sealed record ResolverContext(string Name, int PoolId, int Flags, int LastError);

    [SysAbiExport(
        Nid = "Nlev7Lg8k3A",
        ExportName = "sceNetInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetInit(CpuContext ctx)
    {
        _initialized = true;
        TraceNet("init", 0, 0, 0, 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "cTGkc6-TBlI",
        ExportName = "sceNetTerm",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetTerm(CpuContext ctx)
    {
        _initialized = false;
        _pools.Clear();
        _resolvers.Clear();
        SocketRegistry.Clear();
        TraceNet("term", 0, 0, 0, 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "Q4qBuN-c0ZM",
        ExportName = "sceNetSocket",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetSocket(CpuContext ctx)
    {
        if (!_initialized)
        {
            return SetNetError(ctx, NetErrorNotInitialized, NetErrnoNotInitialized);
        }

        var nameAddress = ctx[CpuRegister.Rdi];
        var family = unchecked((int)ctx[CpuRegister.Rsi]);
        var type = unchecked((int)ctx[CpuRegister.Rdx]);
        var protocol = unchecked((int)ctx[CpuRegister.Rcx]);
        var name = TryReadUtf8Z(ctx, nameAddress, MaxNameLength, out var value)
            ? value
            : string.Empty;

        if (!TryTranslateSocketParameters(family, type, protocol, out var addressFamily, out var socketType, out var protocolType))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            var socket = new Socket(addressFamily, socketType, protocolType);
            var id = SocketRegistry.Allocate(family, type, protocol, socket);
            TraceNet("socket.create", id, unchecked((ulong)family), unchecked((ulong)type), unchecked((ulong)protocol));
            ctx[CpuRegister.Rax] = unchecked((ulong)id);
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "45ggEzakPJQ",
        ExportName = "sceNetSocketClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetSocketClose(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (SocketRegistry.TryClose(id))
        {
            TraceNet("socket.close", id, 0, 0, 0);
            return ctx.SetReturn(0);
        }

        return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
    }

    [SysAbiExport(
        Nid = "2mKX2Spso7I",
        ExportName = "sceNetSetsockopt",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetSetsockopt(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var level = unchecked((int)ctx[CpuRegister.Rsi]);
        var option = unchecked((int)ctx[CpuRegister.Rdx]);
        var valueAddress = ctx[CpuRegister.Rcx];
        var valueLength = unchecked((int)ctx[CpuRegister.R8]);
        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (valueAddress == 0 || valueLength < sizeof(int))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        Span<byte> value = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(valueAddress, value))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var valInt = BinaryPrimitives.ReadInt32LittleEndian(value);

        if (level == 0xFFFF) // SOL_SOCKET
        {
            switch (option)
            {
                case 0x1200: // SO_NBIO
                    socket.SetNonBlocking(valInt != 0);
                    TraceNet("socket.nonblocking", id, socket.IsNonBlocking() ? 1UL : 0UL, 0, 0);
                    return ctx.SetReturn(0);

                case 0x0004: // SO_REUSEADDR
                    socket.NativeSocket?.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, valInt != 0);
                    TraceNet("socket.reuseaddr", id, (ulong)valInt, 0, 0);
                    return ctx.SetReturn(0);

                case 0x0008: // SO_KEEPALIVE
                    socket.NativeSocket?.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, valInt != 0);
                    return ctx.SetReturn(0);

                case 0x1001: // SO_RCVBUF
                    if (socket.NativeSocket is not null) socket.NativeSocket.ReceiveBufferSize = valInt;
                    return ctx.SetReturn(0);

                case 0x1002: // SO_SNDBUF
                    if (socket.NativeSocket is not null) socket.NativeSocket.SendBufferSize = valInt;
                    return ctx.SetReturn(0);
            }
        }

        return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
    }

    private const int NetErrorConnectionRefused = unchecked((int)0x8041013D);
    private const int NetErrnoConnectionRefused = 61;

    [SysAbiExport(
        Nid = "OXXX4mUk3uk",
        ExportName = "sceNetConnect",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetConnect(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var nameAddress = ctx[CpuRegister.Rsi];
        var namelen = unchecked((int)ctx[CpuRegister.Rdx]);

        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (!TryReadSocketAddress(ctx, nameAddress, namelen, out var endpoint) || endpoint is null)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            if (socket.NativeSocket is null)
            {
                socket.NativeSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.NativeSocket.Blocking = !socket.NonBlocking;
            }

            if (socket.IsNonBlocking())
            {
                socket.NativeSocket.ConnectAsync(endpoint);
                socket.BoundAddress = endpoint.Address;
                socket.BoundPort = endpoint.Port;
                socket.Bound = true;
                TraceNet("socket.connect_async", id, unchecked((ulong)endpoint.Port), 0, 0);
                return SetNetError(ctx, NetErrorWouldBlock, NetErrnoWouldBlock);
            }

            socket.NativeSocket.Connect(endpoint);
            socket.Connected = true;
            socket.BoundAddress = endpoint.Address;
            socket.BoundPort = endpoint.Port;
            socket.Bound = true;
            TraceNet("socket.connect", id, unchecked((ulong)endpoint.Port), 0, 0);
            return ctx.SetReturn(0);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.IsConnected || ex.SocketErrorCode == SocketError.InProgress)
        {
            return SetNetError(ctx, NetErrorWouldBlock, NetErrnoWouldBlock);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            socket.LastError = NetErrnoConnectionRefused;
            return SetNetError(ctx, NetErrorConnectionRefused, NetErrnoConnectionRefused);
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "bErx49PgxyY",
        ExportName = "sceNetBind",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetBind(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }
        if (!TryReadSocketAddress(ctx, ctx[CpuRegister.Rsi], unchecked((int)ctx[CpuRegister.Rdx]), out var endpoint))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            if (socket.NativeSocket is null)
            {
                socket.NativeSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socket.NativeSocket.Blocking = !socket.NonBlocking;
            }

            socket.NativeSocket.Bind(endpoint);
            socket.BoundAddress = endpoint.Address;
            socket.BoundPort = endpoint.Port;
            socket.Bound = true;
            TraceNet("socket.bind", id, unchecked((ulong)endpoint.Port), 0, 0);
            return ctx.SetReturn(0);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return SetNetError(ctx, NetErrorAddressInUse, NetErrnoAddressInUse);
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "kOj1HiAGE54",
        ExportName = "sceNetListen",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetListen(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        try
        {
            if (socket.NativeSocket is not null)
            {
                socket.NativeSocket.Listen(Math.Max(0, unchecked((int)ctx[CpuRegister.Rsi])));
            }
            TraceNet("socket.listen", id, ctx[CpuRegister.Rsi], 0, 0);
            return ctx.SetReturn(0);
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "PIWqhn9oSxc",
        ExportName = "sceNetAccept",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetAccept(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        try
        {
            if (socket.NativeSocket is null)
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }

            var accepted = socket.NativeSocket.Accept();
            var acceptedId = SocketRegistry.Allocate(socket.Family, socket.Type, socket.Protocol, accepted);
            TraceNet("socket.accept", acceptedId, unchecked((ulong)id), 0, 0);
            ctx[CpuRegister.Rax] = unchecked((ulong)acceptedId);
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.IOPending)
        {
            return SetNetError(ctx, NetErrorWouldBlock, NetErrnoWouldBlock);
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "xphrZusl78E",
        ExportName = "sceNetGetsockopt",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetGetsockopt(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var level = unchecked((int)ctx[CpuRegister.Rsi]);
        var option = unchecked((int)ctx[CpuRegister.Rdx]);
        var valueAddress = ctx[CpuRegister.Rcx];
        var optlenAddress = ctx[CpuRegister.R8];

        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (valueAddress == 0 || optlenAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        Span<byte> optlenBuf = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(optlenAddress, optlenBuf))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var optlen = BinaryPrimitives.ReadInt32LittleEndian(optlenBuf);
        if (optlen < sizeof(int))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (level == 0xFFFF) // SOL_SOCKET
        {
            int valInt = 0;
            switch (option)
            {
                case 0x1200: // SO_NBIO
                    valInt = socket.IsNonBlocking() ? 1 : 0;
                    break;

                case 0x0004: // SO_REUSEADDR
                    valInt = socket.NativeSocket is not null
                        ? (int)socket.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress)!
                        : 0;
                    break;

                case 0x0008: // SO_KEEPALIVE
                    valInt = socket.NativeSocket is not null
                        ? (int)socket.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!
                        : 0;
                    break;

                case 0x1001: // SO_RCVBUF
                    valInt = socket.NativeSocket?.ReceiveBufferSize ?? 8192;
                    break;

                case 0x1002: // SO_SNDBUF
                    valInt = socket.NativeSocket?.SendBufferSize ?? 8192;
                    break;

                case 0x1007: // SO_ERROR
                    try
                    {
                        if (socket.LastError != 0)
                        {
                            valInt = socket.LastError;
                            socket.LastError = 0;
                        }
                        else if (socket.NativeSocket is not null)
                        {
                            var rawErr = socket.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                            var errCode = rawErr is int errInt ? errInt : 0;
                            if (errCode == (int)SocketError.ConnectionRefused)
                            {
                                valInt = NetErrnoConnectionRefused; // 61
                            }
                            else if (errCode == (int)SocketError.TimedOut)
                            {
                                valInt = 60; // ETIMEDOUT (60 in FreeBSD / Orbis)
                            }
                            else if (errCode == (int)SocketError.IOPending || errCode == (int)SocketError.WouldBlock || errCode == (int)SocketError.InProgress)
                            {
                                valInt = NetErrnoWouldBlock; // 35
                            }
                            else if (errCode != 0)
                            {
                                valInt = errCode;
                            }
                            else if (!socket.Connected && socket.IsNonBlocking())
                            {
                                valInt = NetErrnoConnectionRefused; // 61
                            }
                            else
                            {
                                valInt = 0;
                            }
                        }
                        else
                        {
                            valInt = 0;
                        }
                    }
                    catch
                    {
                        valInt = NetErrnoConnectionRefused; // 61
                    }
                    break;

                default:
                    return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }

            Span<byte> valBuf = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(valBuf, valInt);
            if (!ctx.Memory.TryWrite(valueAddress, valBuf))
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }

            BinaryPrimitives.WriteInt32LittleEndian(optlenBuf, sizeof(int));
            ctx.Memory.TryWrite(optlenAddress, optlenBuf);
            return ctx.SetReturn(0);
        }

        return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
    }

    [SysAbiExport(
        Nid = "hoOAofhhRvE",
        ExportName = "sceNetGetsockname",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetGetsockname(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var sockaddrAddress = ctx[CpuRegister.Rsi];
        var addrlenAddress = ctx[CpuRegister.Rdx];

        if (!SocketRegistry.TryGet(id, out var socket) || socket is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (sockaddrAddress == 0 || addrlenAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            if (socket.NativeSocket?.LocalEndPoint is IPEndPoint ep)
            {
                Span<byte> sockaddr = stackalloc byte[16];
                sockaddr[0] = 16;
                sockaddr[1] = (byte)(ep.AddressFamily == AddressFamily.InterNetwork ? 2 : 28);
                BinaryPrimitives.WriteUInt16BigEndian(sockaddr.Slice(2, 2), (ushort)ep.Port);
                var addressBytes = ep.Address.GetAddressBytes();
                if (addressBytes.Length == 4)
                {
                    addressBytes.CopyTo(sockaddr.Slice(4, 4));
                }
                ctx.Memory.TryWrite(sockaddrAddress, sockaddr);

                Span<byte> addrlenBytes = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(addrlenBytes, 16);
                ctx.Memory.TryWrite(addrlenAddress, addrlenBytes);
            }
            else if (socket.Bound)
            {
                Span<byte> sockaddr = stackalloc byte[16];
                sockaddr[0] = 16;
                sockaddr[1] = 2;
                BinaryPrimitives.WriteUInt16BigEndian(sockaddr.Slice(2, 2), (ushort)socket.BoundPort);
                var addressBytes = socket.BoundAddress.GetAddressBytes();
                if (addressBytes.Length == 4)
                {
                    addressBytes.CopyTo(sockaddr.Slice(4, 4));
                }
                ctx.Memory.TryWrite(sockaddrAddress, sockaddr);

                Span<byte> addrlenBytes = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(addrlenBytes, 16);
                ctx.Memory.TryWrite(addrlenAddress, addrlenBytes);
            }

            return ctx.SetReturn(0);
        }
        catch (SocketException)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
    }

    [SysAbiExport(
        Nid = "HQOwnfMGipQ",
        ExportName = "sceNetErrnoLoc",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetErrnoLoc(CpuContext ctx)
    {
        if (_errnoAddress == 0)
        {
            _errnoAddress = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(_errnoAddress, 0);
        }

        ctx[CpuRegister.Rax] = unchecked((ulong)_errnoAddress);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "dgJBaeJnGpo",
        ExportName = "sceNetPoolCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetPoolCreate(CpuContext ctx)
    {
        var nameAddress = ctx[CpuRegister.Rdi];
        var size = unchecked((int)ctx[CpuRegister.Rsi]);
        var flags = unchecked((int)ctx[CpuRegister.Rdx]);

        if (size <= 0)
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        var name = TryReadUtf8Z(ctx, nameAddress, MaxNameLength, out var value)
            ? value
            : string.Empty;

        var id = Interlocked.Increment(ref _nextPoolId);
        _pools[id] = new NetPool(name, size, flags);

        TraceNet("pool.create", id, unchecked((ulong)size), unchecked((ulong)flags), _initialized ? 1UL : 0UL);
        ctx[CpuRegister.Rax] = unchecked((ulong)id);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "K7RlrTkI-mw",
        ExportName = "sceNetPoolDestroy",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetPoolDestroy(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!_pools.TryRemove(id, out _))
        {
            return ctx.SetReturn(NetErrorBadFileDescriptor);
        }

        TraceNet("pool.destroy", id, 0, 0, _initialized ? 1UL : 0UL);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "9T2pDF2Ryqg",
        ExportName = "sceNetHtonl",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetHtonl(CpuContext ctx)
    {
        var value = unchecked((uint)ctx[CpuRegister.Rdi]);
        // The byte-swapped result is the return value and already lives in Rax; return OK as the
        // dispatch status without going through SetReturn, which would overwrite Rax with 0.
        ctx[CpuRegister.Rax] = BinaryPrimitives.ReverseEndianness(value);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "iWQWrwiSt8A",
        ExportName = "sceNetHtons",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetHtons(CpuContext ctx)
    {
        var value = unchecked((ushort)ctx[CpuRegister.Rdi]);
        // The byte-swapped result is the return value and already lives in Rax; return OK as the
        // dispatch status without going through SetReturn, which would overwrite Rax with 0.
        ctx[CpuRegister.Rax] = BinaryPrimitives.ReverseEndianness(value);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "pQGpHYopAIY",
        ExportName = "sceNetNtohl",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetNtohl(CpuContext ctx)
    {
        var value = unchecked((uint)ctx[CpuRegister.Rdi]);
        // The byte-swapped result is the return value and already lives in Rax; return OK as the
        // dispatch status without going through SetReturn, which would overwrite Rax with 0.
        ctx[CpuRegister.Rax] = BinaryPrimitives.ReverseEndianness(value);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "Rbvt+5Y2iEw",
        ExportName = "sceNetNtohs",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetNtohs(CpuContext ctx)
    {
        var value = unchecked((ushort)ctx[CpuRegister.Rdi]);
        // The byte-swapped result is the return value and already lives in Rax; return OK as the
        // dispatch status without going through SetReturn, which would overwrite Rax with 0.
        ctx[CpuRegister.Rax] = BinaryPrimitives.ReverseEndianness(value);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "C4UgDHHPvdw",
        ExportName = "sceNetResolverCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetResolverCreate(CpuContext ctx)
    {
        var nameAddress = ctx[CpuRegister.Rdi];
        var poolId = unchecked((int)ctx[CpuRegister.Rsi]);
        var flags = unchecked((int)ctx[CpuRegister.Rdx]);
        if (flags != 0)
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        var name = TryReadUtf8Z(ctx, nameAddress, MaxNameLength, out var value)
            ? value
            : string.Empty;
        var id = Interlocked.Increment(ref _nextResolverId);
        _resolvers[id] = new ResolverContext(name, poolId, flags, 0);
        TraceNet("resolver.create", id, unchecked((ulong)poolId), unchecked((ulong)flags), _initialized ? 1UL : 0UL);
        ctx[CpuRegister.Rax] = unchecked((ulong)id);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "kJlYH5uMAWI",
        ExportName = "sceNetResolverDestroy",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetResolverDestroy(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        return _resolvers.TryRemove(id, out _)
            ? ctx.SetReturn(0)
            : ctx.SetReturn(NetErrorBadFileDescriptor);
    }

    [SysAbiExport(
        Nid = "J5i3hiLJMPk",
        ExportName = "sceNetResolverGetError",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetResolverGetError(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var statusAddress = ctx[CpuRegister.Rsi];
        if (statusAddress == 0)
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        if (!_resolvers.TryGetValue(id, out var resolver))
        {
            return ctx.SetReturn(NetErrorBadFileDescriptor);
        }

        Span<byte> status = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(status, resolver.LastError);
        return ctx.Memory.TryWrite(statusAddress, status)
            ? ctx.SetReturn(0)
            : ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "Nd91WaWmG2w",
        ExportName = "sceNetResolverStartNtoa",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetResolverStartNtoa(CpuContext ctx)
    {
        var rid = unchecked((int)ctx[CpuRegister.Rdi]);
        var hostnameAddress = ctx[CpuRegister.Rsi];
        var addrAddress = ctx[CpuRegister.Rdx];
        var timeout = unchecked((int)ctx[CpuRegister.Rcx]);
        var retry = unchecked((int)ctx[CpuRegister.R8]);
        var flags = unchecked((int)ctx[CpuRegister.R9]);

        const int NetErrorResolverNoHost = unchecked((int)0x80410103);
        const int NetErrorResolverInternal = unchecked((int)0x80410104);

        if (hostnameAddress == 0 || addrAddress == 0 || (flags & ~0x00010000) != 0)
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        if (!_resolvers.ContainsKey(rid))
        {
            return ctx.SetReturn(NetErrorBadFileDescriptor);
        }

        if (!TryReadUtf8Z(ctx, hostnameAddress, MaxNameLength, out var hostname) || string.IsNullOrWhiteSpace(hostname))
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        if ((flags & 0x00010000) == 0 && IPAddress.TryParse(hostname, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return ctx.Memory.TryWrite(addrAddress, bytes)
                ? ctx.SetReturn(0)
                : ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(hostname, AddressFamily.InterNetwork);
            if (addresses.Length > 0)
            {
                var bytes = addresses[0].GetAddressBytes();
                return ctx.Memory.TryWrite(addrAddress, bytes)
                    ? ctx.SetReturn(0)
                    : ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }
        catch (SocketException)
        {
            return ctx.SetReturn(NetErrorResolverNoHost);
        }
        catch (Exception)
        {
            return ctx.SetReturn(NetErrorResolverInternal);
        }

        return ctx.SetReturn(NetErrorResolverNoHost);
    }

    private static int SetNetError(CpuContext ctx, int result, int errno)
    {
        if (_errnoAddress == 0)
        {
            _errnoAddress = Marshal.AllocHGlobal(sizeof(int));
        }
        Marshal.WriteInt32(_errnoAddress, errno);
        return ctx.SetReturn(-1);
    }

    private static bool TryTranslateSocketParameters(
        int family,
        int type,
        int protocol,
        out AddressFamily addressFamily,
        out SocketType socketType,
        out ProtocolType protocolType)
    {
        addressFamily = family switch
        {
            2 => AddressFamily.InterNetwork,
            28 => AddressFamily.InterNetworkV6,
            _ => AddressFamily.Unspecified,
        };
        socketType = type switch
        {
            1 => SocketType.Stream,
            2 => SocketType.Dgram,
            _ => SocketType.Unknown,
        };
        protocolType = protocol switch
        {
            0 when socketType == SocketType.Stream => ProtocolType.Tcp,
            0 when socketType == SocketType.Dgram => ProtocolType.Udp,
            6 => ProtocolType.Tcp,
            17 => ProtocolType.Udp,
            _ => ProtocolType.Unknown,
        };

        return addressFamily != AddressFamily.Unspecified &&
            socketType != SocketType.Unknown &&
            protocolType != ProtocolType.Unknown;
    }

    private static bool TryReadSocketAddress(CpuContext ctx, ulong address, int length, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Any, 0);
        if (address == 0 || length < 16)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[28];
        var readLen = Math.Min(length, 28);
        if (!ctx.Memory.TryRead(address, bytes[..readLen]))
        {
            return false;
        }

        var family = bytes[1];
        if (family == 2) // AF_INET
        {
            var port = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..4]);
            endpoint = new IPEndPoint(new IPAddress(bytes[4..8]), port);
            return true;
        }

        if (family == 28) // AF_INET6
        {
            if (readLen < 28)
            {
                return false;
            }

            var port = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..4]);
            endpoint = new IPEndPoint(new IPAddress(bytes[8..24]), port);
            return true;
        }

        return false;
    }

    private static bool TryReadUtf8Z(CpuContext ctx, ulong address, int maxLength, out string value)
    {
        value = string.Empty;
        if (address == 0)
        {
            return true;
        }

        Span<byte> one = stackalloc byte[1];
        var bytes = new byte[maxLength];
        var count = 0;
        for (; count < maxLength; count++)
        {
            if (!ctx.Memory.TryRead(address + (ulong)count, one))
            {
                return false;
            }

            if (one[0] == 0)
            {
                break;
            }

            bytes[count] = one[0];
        }

        value = Encoding.UTF8.GetString(bytes, 0, count);
        return true;
    }

    [SysAbiExport(
        Nid = "8Kcp5d-q1Uo",
        ExportName = "sceNetInetPton",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetInetPton(CpuContext ctx)
    {
        var af = (int)ctx[CpuRegister.Rdi];
        var srcAddress = ctx[CpuRegister.Rsi];
        var dstAddress = ctx[CpuRegister.Rdx];

        if (srcAddress == 0 || dstAddress == 0)
        {
            return ctx.SetReturn(NetErrorInvalidArgument);
        }

        const int AF_INET = 2;
        const int AF_INET6 = 28;
        const int NetErrorAddressFamilyNotSupported = unchecked((int)0x8041012F);

        if (af != AF_INET && af != AF_INET6)
        {
            return ctx.SetReturn(NetErrorAddressFamilyNotSupported);
        }

        if (!TryReadUtf8Z(ctx, srcAddress, 256, out var src) || string.IsNullOrEmpty(src))
        {
            return ctx.SetReturn(0);
        }

        if (IPAddress.TryParse(src, out var ip))
        {
            if (af == AF_INET && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                Span<byte> bytes = stackalloc byte[4];
                if (ip.TryWriteBytes(bytes, out _))
                {
                    ctx.Memory.TryWrite(dstAddress, bytes);
                    return ctx.SetReturn(1);
                }
            }
            else if (af == AF_INET6 && ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> bytes = stackalloc byte[16];
                if (ip.TryWriteBytes(bytes, out _))
                {
                    ctx.Memory.TryWrite(dstAddress, bytes);
                    return ctx.SetReturn(1);
                }
            }
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "TDfQqO-gMbY",
        ExportName = "sceSslGetCaCerts",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int SslGetCaCerts(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "qIvLs0gYxi0",
        ExportName = "sceSslFreeCaCerts",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int SslFreeCaCerts(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    private static int _nextEpollId = 1;

    [SysAbiExport(
        Nid = "SF47kB2MNTo",
        ExportName = "sceNetEpollCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollCreate(CpuContext ctx)
    {
        var nameAddress = ctx[CpuRegister.Rdi];
        var flags = unchecked((int)ctx[CpuRegister.Rsi]);
        var epollId = Interlocked.Increment(ref _nextEpollId);
        TraceNet("epoll.create", epollId, unchecked((ulong)flags), 0, 0);
        return ctx.SetReturn(epollId);
    }

    [SysAbiExport(
        Nid = "ZVw46bsasAk",
        ExportName = "sceNetEpollControl",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollControl(CpuContext ctx)
    {
        var eid = unchecked((int)ctx[CpuRegister.Rdi]);
        var op = unchecked((int)ctx[CpuRegister.Rsi]);
        var id = unchecked((int)ctx[CpuRegister.Rdx]);
        var evAddress = ctx[CpuRegister.Rcx];
        TraceNet("epoll.control", eid, unchecked((ulong)op), unchecked((ulong)id), evAddress);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "drjIbDbA7UQ",
        ExportName = "sceNetEpollWait",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollWait(CpuContext ctx)
    {
        var eid = unchecked((int)ctx[CpuRegister.Rdi]);
        var eventsAddress = ctx[CpuRegister.Rsi];
        var maxEvents = unchecked((int)ctx[CpuRegister.Rdx]);
        var timeoutUsec = unchecked((int)ctx[CpuRegister.Rcx]);
        TraceNet("epoll.wait", eid, unchecked((ulong)maxEvents), unchecked((ulong)timeoutUsec), eventsAddress);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "Inp1lfL+Jdw",
        ExportName = "sceNetEpollDestroy",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollDestroy(CpuContext ctx)
    {
        var eid = unchecked((int)ctx[CpuRegister.Rdi]);
        TraceNet("epoll.destroy", eid, 0, 0, 0);
        return ctx.SetReturn(0);
    }

    private static void TraceNet(string operation, int id, ulong arg0, ulong arg1, ulong arg2)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_NET"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine(
            $"[LOADER][TRACE] net.{operation} id={id} arg0=0x{arg0:X16} arg1=0x{arg1:X16} arg2=0x{arg2:X16}");
    }
}
