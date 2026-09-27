// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

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

    internal static bool TryGetReadEventState(
        int socketId,
        ulong lowWater,
        out bool ready,
        out ulong availableBytes,
        out ushort eventFlags)
    {
        return KernelSocketCompatExports.TryGetReadEventState(
            socketId,
            lowWater,
            out ready,
            out availableBytes,
            out eventFlags);
    }

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
        ClearEpolls();
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

        var fd = SocketRegistry.Allocate(family, type, protocol);
        if (fd < 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        TraceNet("socket.create", fd, unchecked((ulong)family), unchecked((ulong)type), unchecked((ulong)protocol));
        ctx[CpuRegister.Rax] = unchecked((ulong)fd);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "45ggEzakPJQ",
        ExportName = "sceNetSocketClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetSocketClose(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!SocketRegistry.TryClose(id))
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        TraceNet("socket.close", id, 0, 0, 0);
        return ctx.SetReturn(0);
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
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
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

        int intVal = BinaryPrimitives.ReadInt32LittleEndian(value);
        if (!KernelSocketCompatExports.TrySetSocketOption(state, level, option, intVal))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        TraceNet("socket.setsockopt", id, unchecked((uint)level), unchecked((uint)option), unchecked((uint)intVal));
        return ctx.SetReturn(0);
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
        var lengthAddress = ctx[CpuRegister.R8];
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (valueAddress == 0 || lengthAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(lengthAddress, lengthBytes))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(lengthBytes) < sizeof(int))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (!KernelSocketCompatExports.TryGetSocketOption(state, level, option, out var value))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        Span<byte> valueBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(valueBytes, value);
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, sizeof(int));
        if (!ctx.Memory.TryWrite(valueAddress, valueBytes) ||
            !ctx.Memory.TryWrite(lengthAddress, lengthBytes))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        TraceNet("socket.getsockopt", id, unchecked((uint)option), unchecked((uint)value), 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "OXXX4mUk3uk",
        ExportName = "sceNetConnect",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetConnect(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var sockaddrAddress = ctx[CpuRegister.Rsi];
        var addrlen = unchecked((int)ctx[CpuRegister.Rdx]);

        if (sockaddrAddress == 0 || addrlen < 16)
        {
            return SetNetError(ctx, -1, NetErrnoInvalidArgument);
        }

        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, -1, NetErrnoBadFileDescriptor);
        }

        if (!TryReadSocketAddress(ctx, sockaddrAddress, addrlen, out var endpoint))
        {
            return SetNetError(ctx, -1, NetErrnoInvalidArgument);
        }

        try
        {
            var socket = state.NativeSocket ?? state.Client?.Client;
            if (socket is null)
            {
                return SetNetError(ctx, -1, NetErrnoInvalidArgument);
            }

            if (state.IsNonBlocking())
            {
                socket.Blocking = false;
                try
                {
                    socket.Connect(endpoint);
                    state.Connected = true;
                    TraceNet("socket.connect", id, unchecked((ulong)endpoint.Port), 0, 0);
                    return ctx.SetReturn(0);
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
                {
                    state.LastError = 36; // EINPROGRESS
                    return SetNetError(ctx, -1, 36);
                }
                catch (SocketException ex)
                {
                    var posixErr = KernelSocketCompatExports.MapSocketErrorToPosixErrno(ex.SocketErrorCode);
                    state.LastError = posixErr != 0 ? posixErr : 61; // ECONNREFUSED
                    return SetNetError(ctx, -1, state.LastError);
                }
            }
            else
            {
                socket.Connect(endpoint);
                state.Connected = true;
                TraceNet("socket.connect", id, unchecked((ulong)endpoint.Port), 0, 0);
                return ctx.SetReturn(0);
            }
        }
        catch (SocketException ex)
        {
            var posixErr = KernelSocketCompatExports.MapSocketErrorToPosixErrno(ex.SocketErrorCode);
            state.LastError = posixErr != 0 ? posixErr : 61;
            return SetNetError(ctx, -1, state.LastError);
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
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }
        if (!TryReadSocketAddress(ctx, ctx[CpuRegister.Rsi], unchecked((int)ctx[CpuRegister.Rdx]), out var endpoint))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            var socket = state.NativeSocket ?? state.Client?.Client;
            if (socket is null)
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }
            socket.Bind(endpoint);
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
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        try
        {
            var socket = state.NativeSocket ?? state.Client?.Client;
            if (socket is null)
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }
            socket.Listen(Math.Max(0, unchecked((int)ctx[CpuRegister.Rsi])));
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
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        try
        {
            var socket = state.NativeSocket ?? state.Client?.Client;
            if (socket is null)
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }
            var accepted = socket.Accept();
            var acceptedId = SocketRegistry.Allocate(state.Family, state.Type, state.Protocol, accepted);
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
        Nid = "hoOAofhhRvE",
        ExportName = "sceNetGetsockname",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetGetsockname(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var addrAddress = ctx[CpuRegister.Rsi];
        var addrLenAddress = ctx[CpuRegister.Rdx];
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (addrAddress == 0 || addrLenAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        Span<byte> lenBytes = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(addrLenAddress, lenBytes))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var maxLen = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
        if (maxLen < 16)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var socket = state.NativeSocket ?? state.Client?.Client;
        IPEndPoint? endpoint = socket?.LocalEndPoint as IPEndPoint;
        Span<byte> sockaddr = stackalloc byte[16];
        sockaddr[0] = 16;
        sockaddr[1] = 2; // AF_INET
        if (endpoint is not null)
        {
            BinaryPrimitives.WriteUInt16BigEndian(sockaddr[2..4], (ushort)endpoint.Port);
            endpoint.Address.TryWriteBytes(sockaddr[4..8], out _);
        }

        BinaryPrimitives.WriteInt32LittleEndian(lenBytes, 16);
        if (!ctx.Memory.TryWrite(addrAddress, sockaddr) ||
            !ctx.Memory.TryWrite(addrLenAddress, lenBytes))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "TSM6whtekok",
        ExportName = "sceNetShutdown",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetShutdown(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var how = unchecked((int)ctx[CpuRegister.Rsi]);
        if (!SocketRegistry.TryGet(id, out var state) || state is null)
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        try
        {
            var socket = state.NativeSocket ?? state.Client?.Client;
            if (socket is not null && socket.Connected)
            {
                var shutdownHow = how switch
                {
                    0 => SocketShutdown.Receive,
                    1 => SocketShutdown.Send,
                    _ => SocketShutdown.Both,
                };
                socket.Shutdown(shutdownHow);
            }
            return ctx.SetReturn(0);
        }
        catch (SocketException)
        {
            return ctx.SetReturn(0);
        }
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

        if (hostnameAddress == 0 || addrAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (!_resolvers.ContainsKey(rid))
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        if (!TryReadUtf8Z(ctx, hostnameAddress, MaxNameLength, out var hostname) || string.IsNullOrEmpty(hostname))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if ((flags & 0x00010000) == 0 && IPAddress.TryParse(hostname, out var parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> ipBytes = stackalloc byte[4];
            parsedIp.TryWriteBytes(ipBytes, out _);
            if (ctx.Memory.TryWrite(addrAddress, ipBytes))
            {
                return ctx.SetReturn(0);
            }
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(hostname);
            foreach (var ip in addresses)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    Span<byte> ipBytes = stackalloc byte[4];
                    ip.TryWriteBytes(ipBytes, out _);
                    if (ctx.Memory.TryWrite(addrAddress, ipBytes))
                    {
                        return ctx.SetReturn(0);
                    }
                    break;
                }
            }
        }
        catch
        {
            // Fall through to error
        }

        // NET_ERROR_RESOLVER_ENOHOST = 0x80410114
        return SetNetError(ctx, unchecked((int)0x80410114), 20);
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

    private const int EpollIdBase = 1024;
    private const int EpollCtlAdd = 1;
    private const int EpollCtlMod = 2;
    private const int EpollCtlDel = 3;

    private const uint EpollIn = 0x00000001;
    private const uint EpollOut = 0x00000002;
    private const uint EpollErr = 0x00000008;

    public struct NetEpollEvent
    {
        public uint Events;
        public uint Reserved;
        public ulong Ident;
        public ulong Data;
    }

    private sealed class EpollRegistration
    {
        public int Id { get; init; }
        public NetEpollEvent Event { get; set; }
    }

    private sealed class EpollSlot
    {
        public int Eid { get; init; }
        public string Name { get; set; } = string.Empty;
        public List<EpollRegistration> Registrations { get; } = new();
    }

    private static readonly object _epollLock = new();
    private static readonly Dictionary<int, EpollSlot> _epolls = new();
    private static int _nextEpollId = EpollIdBase;

    public static void RemoveSocketFromEpolls(int id)
    {
        lock (_epollLock)
        {
            var changed = false;
            foreach (var slot in _epolls.Values)
            {
                if (slot.Registrations.RemoveAll(r => r.Id == id) > 0)
                {
                    changed = true;
                }
            }
            if (changed)
            {
                Monitor.PulseAll(_epollLock);
            }
        }
    }

    public static void ClearEpolls()
    {
        lock (_epollLock)
        {
            _epolls.Clear();
            Monitor.PulseAll(_epollLock);
        }
    }

    [SysAbiExport(
        Nid = "SF47kB2MNTo",
        ExportName = "sceNetEpollCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollCreate(CpuContext ctx)
    {
        var nameAddress = ctx[CpuRegister.Rdi];
        var flags = unchecked((int)ctx[CpuRegister.Rsi]);
        if (flags != 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var name = string.Empty;
        if (nameAddress != 0)
        {
            TryReadUtf8Z(ctx, nameAddress, MaxNameLength, out name);
        }

        lock (_epollLock)
        {
            var eid = _nextEpollId++;
            _epolls[eid] = new EpollSlot
            {
                Eid = eid,
                Name = name ?? string.Empty
            };
            TraceNet("epoll_create", eid, nameAddress, unchecked((ulong)flags), 0);
            return ctx.SetReturn(eid);
        }
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
        var eventAddress = ctx[CpuRegister.Rcx];

        if ((op == EpollCtlAdd || op == EpollCtlMod) && eventAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (op < EpollCtlAdd || op > EpollCtlDel)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (!SocketRegistry.IsSocket(id))
        {
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }

        NetEpollEvent ev = default;
        if (op != EpollCtlDel)
        {
            Span<byte> eventBytes = stackalloc byte[24];
            if (!ctx.Memory.TryRead(eventAddress, eventBytes))
            {
                return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
            }
            ev.Events = BinaryPrimitives.ReadUInt32LittleEndian(eventBytes[0x00..]);
            ev.Reserved = BinaryPrimitives.ReadUInt32LittleEndian(eventBytes[0x04..]);
            ev.Ident = BinaryPrimitives.ReadUInt64LittleEndian(eventBytes[0x08..]);
            ev.Data = BinaryPrimitives.ReadUInt64LittleEndian(eventBytes[0x10..]);
        }

        lock (_epollLock)
        {
            if (!_epolls.TryGetValue(eid, out var slot))
            {
                return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
            }

            var index = slot.Registrations.FindIndex(r => r.Id == id);
            switch (op)
            {
                case EpollCtlAdd:
                    if (index >= 0)
                    {
                        return SetNetError(ctx, unchecked((int)0x80410111), 17); // EEXIST
                    }
                    slot.Registrations.Add(new EpollRegistration { Id = id, Event = ev });
                    break;

                case EpollCtlMod:
                    if (index < 0)
                    {
                        return SetNetError(ctx, unchecked((int)0x80410102), 2); // ENOENT
                    }
                    slot.Registrations[index].Event = ev;
                    break;

                case EpollCtlDel:
                    if (index < 0)
                    {
                        return SetNetError(ctx, unchecked((int)0x80410102), 2); // ENOENT
                    }
                    slot.Registrations.RemoveAt(index);
                    break;
            }

            Monitor.PulseAll(_epollLock);
            TraceNet("epoll_ctl", eid, unchecked((ulong)op), unchecked((ulong)id), eventAddress);
            return ctx.SetReturn(0);
        }
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
        var maxevents = unchecked((int)ctx[CpuRegister.Rdx]);
        var timeout = unchecked((int)ctx[CpuRegister.Rcx]);

        if (eventsAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }
        if (maxevents <= 0 || timeout < -1)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        List<EpollRegistration> registrations;
        lock (_epollLock)
        {
            if (!_epolls.TryGetValue(eid, out var slot))
            {
                return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
            }

            if (slot.Registrations.Count == 0 && timeout != 0)
            {
                if (timeout < 0)
                {
                    while (slot.Registrations.Count == 0 && _epolls.ContainsKey(eid))
                    {
                        Monitor.Wait(_epollLock);
                    }
                }
                else
                {
                    var ms = Math.Max(1, timeout / 1000);
                    Monitor.Wait(_epollLock, ms);
                }

                if (!_epolls.TryGetValue(eid, out slot))
                {
                    return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
                }
            }

            registrations = new List<EpollRegistration>(slot.Registrations);
        }

        if (registrations.Count == 0)
        {
            return ctx.SetReturn(0);
        }

        var checkRead = new List<Socket>();
        var checkWrite = new List<Socket>();
        var checkError = new List<Socket>();
        var socketToReg = new List<(Socket Socket, EpollRegistration Reg)>();

        foreach (var reg in registrations)
        {
            if (SocketRegistry.TryGet(reg.Id, out var sock) && sock?.NativeSocket is not null)
            {
                var native = sock.NativeSocket;
                if ((reg.Event.Events & EpollIn) != 0)
                {
                    checkRead.Add(native);
                }
                if ((reg.Event.Events & EpollOut) != 0)
                {
                    checkWrite.Add(native);
                }
                checkError.Add(native);
                socketToReg.Add((native, reg));
            }
        }

        if (socketToReg.Count == 0)
        {
            if (timeout > 0)
            {
                Thread.Sleep(Math.Min(timeout / 1000, 1000));
            }
            return ctx.SetReturn(0);
        }

        int selectTimeout = timeout < 0 ? -1 : timeout;
        try
        {
            Socket.Select(
                checkRead.Count > 0 ? checkRead : null,
                checkWrite.Count > 0 ? checkWrite : null,
                checkError.Count > 0 ? checkError : null,
                selectTimeout);
        }
        catch (SocketException ex)
        {
            return SetNetError(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND, (int)ex.SocketErrorCode);
        }

        var count = 0;
        Span<byte> outEvent = stackalloc byte[24];
        foreach (var (native, reg) in socketToReg)
        {
            uint ready = 0;
            if (checkRead.Contains(native))
            {
                ready |= EpollIn;
            }
            if (checkWrite.Contains(native))
            {
                ready |= EpollOut;
            }
            if (checkError.Contains(native))
            {
                ready |= EpollErr;
            }

            if (ready == 0)
            {
                continue;
            }

            outEvent.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(outEvent[0x00..], ready);
            BinaryPrimitives.WriteUInt32LittleEndian(outEvent[0x04..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(outEvent[0x08..], (ulong)reg.Id);
            BinaryPrimitives.WriteUInt64LittleEndian(outEvent[0x10..], reg.Event.Data);

            var dest = eventsAddress + (ulong)(count * 24);
            if (!ctx.Memory.TryWrite(dest, outEvent))
            {
                break;
            }

            count++;
            if (count >= maxevents)
            {
                break;
            }
        }

        TraceNet("epoll_wait", eid, eventsAddress, unchecked((ulong)maxevents), unchecked((ulong)count));
        return ctx.SetReturn(count);
    }

    [SysAbiExport(
        Nid = "Inp1lfL+Jdw",
        ExportName = "sceNetEpollDestroy",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNet")]
    public static int NetEpollDestroy(CpuContext ctx)
    {
        var eid = unchecked((int)ctx[CpuRegister.Rdi]);
        lock (_epollLock)
        {
            if (_epolls.Remove(eid))
            {
                Monitor.PulseAll(_epollLock);
                TraceNet("epoll_destroy", eid, 0, 0, 0);
                return ctx.SetReturn(0);
            }
            return SetNetError(ctx, NetErrorBadFileDescriptor, NetErrnoBadFileDescriptor);
        }
    }


    private static int SetNetError(CpuContext ctx, int result, int errno)
    {
        if (_errnoAddress == 0)
        {
            _errnoAddress = Marshal.AllocHGlobal(sizeof(int));
        }
        Marshal.WriteInt32(_errnoAddress, errno);
        KernelRuntimeCompatExports.TrySetErrno(ctx, errno);
        return ctx.SetReturn(result);
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

        Span<byte> bytes = stackalloc byte[16];
        if (!ctx.Memory.TryRead(address, bytes) || bytes[1] != 2)
        {
            return false;
        }

        var port = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..4]);
        endpoint = new IPEndPoint(new IPAddress(bytes[4..8]), port);
        return true;
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
        var addressFamily = unchecked((int)ctx[CpuRegister.Rdi]);
        var sourceAddress = ctx[CpuRegister.Rsi];
        var destinationAddress = ctx[CpuRegister.Rdx];
        if (sourceAddress == 0 || destinationAddress == 0)
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        if (!TryReadUtf8Z(ctx, sourceAddress, MaxNameLength, out var source))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        var family = addressFamily switch
        {
            2 => AddressFamily.InterNetwork,      // AF_INET
            28 => AddressFamily.InterNetworkV6,   // AF_INET6
            _ => AddressFamily.Unknown,
        };
        if (family == AddressFamily.Unknown ||
            !IPAddress.TryParse(source, out var parsed) ||
            parsed.AddressFamily != family)
        {
            // Match BSD inet_pton: return 0 for a parseable-family miss.
            ctx[CpuRegister.Rax] = 0;
            return 0;
        }

        var bytes = parsed.GetAddressBytes();
        if (!ctx.Memory.TryWrite(destinationAddress, bytes))
        {
            return SetNetError(ctx, NetErrorInvalidArgument, NetErrnoInvalidArgument);
        }

        TraceNet("inet_pton", addressFamily, sourceAddress, destinationAddress, (ulong)bytes.Length);
        ctx[CpuRegister.Rax] = 1;
        return 1;
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
