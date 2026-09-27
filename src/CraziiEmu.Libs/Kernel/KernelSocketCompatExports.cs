// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Network;

namespace CraziiEmu.Libs.Kernel;

internal static class KernelSocketCompatExports
{
    private const int PosixEbadf = 9;
    private const int PosixEfault = 14;
    private const int PosixEinval = 22;
    private const int PosixEmfile = 24;
    private const int PosixEwouldblock = 35;
    private const int PosixEinprogress = 36;
    private const int PosixEdestaddrreq = 39;
    private const int PosixEmsgsize = 40;
    private const int PosixEprotonosupport = 43;
    private const int PosixEopnotsupp = 45;
    private const int PosixEafnosupport = 47;
    private const int PosixEaddrnotavail = 49;
    private const int PosixEnetdown = 50;
    private const int PosixEnetunreach = 51;
    private const int PosixEconnaborted = 53;
    private const int PosixEconnreset = 54;
    private const int PosixEisconn = 56;
    private const int PosixEnotconn = 57;
    private const int PosixEtimedout = 60;
    private const int PosixEconnrefused = 61;

    internal static bool IsEmulatedSocketFd(int fd) => SocketRegistry.IsSocket(fd);

    internal static bool TryGetReadEventState(
        int fd,
        ulong lowWater,
        out bool ready,
        out ulong availableBytes,
        out ushort eventFlags)
    {
        ready = false;
        availableBytes = 0;
        eventFlags = 0;

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return false;
        }

        var socket = state.NativeSocket ?? state.Client?.Client;
        if (socket is null)
        {
            return !state.Connected;
        }

        try
        {
            var readSignaled = socket.Poll(0, SelectMode.SelectRead);
            availableBytes = unchecked((ulong)Math.Max(0, socket.Available));
            if (readSignaled && availableBytes == 0)
            {
                ready = true;
                eventFlags = KernelEventQueueCompatExports.KernelEventFlagEof;
            }
            else
            {
                ready = availableBytes >= Math.Max(1UL, lowWater);
            }
        }
        catch (SocketException)
        {
            ready = true;
            eventFlags = KernelEventQueueCompatExports.KernelEventFlagEof;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    internal static bool TryCloseSocketFd(int fd)
    {
        return SocketRegistry.TryClose(fd);
    }

    internal static bool TryReadSocketFd(
        CpuContext ctx,
        int fd,
        ulong bufferAddress,
        int requested,
        out ulong bytesRead,
        out OrbisGen2Result error)
    {
        bytesRead = 0;
        error = OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND;

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return false;
        }

        if (state.NativeSocket is not null)
        {
            var buf = GC.AllocateUninitializedArray<byte>(requested);
            try
            {
                var read = state.NativeSocket.Receive(buf, SocketFlags.None);
                if (read > 0 && !ctx.Memory.TryWrite(bufferAddress, buf.AsSpan(0, read)))
                {
                    error = OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
                    return true;
                }
                bytesRead = unchecked((ulong)read);
                error = OrbisGen2Result.ORBIS_GEN2_OK;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        if (state.Connected && state.Stream is not null)
        {
            var socketBuffer = GC.AllocateUninitializedArray<byte>(requested);
            int socketRead;
            try
            {
                socketRead = state.Stream.Read(socketBuffer, 0, requested);
            }
            catch (IOException)
            {
                return false;
            }

            if (socketRead > 0 && !ctx.Memory.TryWrite(bufferAddress, socketBuffer.AsSpan(0, socketRead)))
            {
                error = OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
                return true;
            }

            bytesRead = unchecked((ulong)socketRead);
            error = OrbisGen2Result.ORBIS_GEN2_OK;
            return true;
        }

        return false;
    }

    internal static bool TryWriteSocketFd(
        CpuContext ctx,
        int fd,
        byte[] payload,
        out OrbisGen2Result error)
    {
        error = OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND;

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return false;
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                state.NativeSocket.Send(payload, SocketFlags.None);
                error = OrbisGen2Result.ORBIS_GEN2_OK;
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        if (state.Connected && state.Stream is not null)
        {
            try
            {
                state.Stream.Write(payload, 0, payload.Length);
                state.Stream.Flush();
            }
            catch (IOException)
            {
                return false;
            }

            error = OrbisGen2Result.ORBIS_GEN2_OK;
            return true;
        }

        return false;
    }

    internal static int PosixSetSocketOption(CpuContext ctx)
    {
        return Setsockopt(ctx);
    }

    internal static int PosixGetSocketOption(CpuContext ctx)
    {
        return Getsockopt(ctx);
    }

    internal static int PosixSendTo(CpuContext ctx)
    {
        return SendTo(ctx);
    }

    internal static int PosixReceiveFrom(CpuContext ctx)
    {
        return RecvFrom(ctx);
    }

    [SysAbiExport(
        Nid = "TU-d9PfIHPM",
        ExportName = "socket",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Socket(CpuContext ctx)
    {
        var family = unchecked((int)ctx[CpuRegister.Rdi]);
        var type = unchecked((int)ctx[CpuRegister.Rsi]);
        var protocol = unchecked((int)ctx[CpuRegister.Rdx]);

        if (family is not (2 or 28))
        {
            family = 2;
            type = (type is 1 or 2 or 3) ? type : 1;
            protocol = (protocol is 0 or 6 or 17) ? protocol : 6;
        }

        var fd = SocketRegistry.Allocate(family, type, protocol);
        if (fd <= 0)
        {
            return PosixSocketFailure(ctx, PosixEmfile);
        }

        LogNet($"socket created: fd={fd} family={family} type={type} protocol={protocol}");
        ctx[CpuRegister.Rax] = unchecked((ulong)fd);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "XVL8So3QJUk",
        ExportName = "connect",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Connect(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var sockaddrAddress = ctx[CpuRegister.Rsi];
        var addrlen = unchecked((int)ctx[CpuRegister.Rdx]);

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (sockaddrAddress == 0 || addrlen < 8)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!TryParseGuestSockaddrIn(sockaddrAddress, addrlen, ctx, out var ipAddress, out var port))
        {
            LogNet($"connect sockaddr parse failed: fd={fd} addr=0x{sockaddrAddress:X} len={addrlen}");
            return PosixSocketFailure(ctx, PosixEinval);
        }

        var redirectApplied = TryApplyNetRedirect(ref ipAddress);
        if (redirectApplied)
        {
            LogNet($"connect redirect: fd={fd} ip={ipAddress} port={port}");
        }

        var targetEndpoint = new IPEndPoint(ipAddress, port);
        if (state.NativeSocket is not null)
        {
            try
            {
                if (state.IsNonBlocking())
                {
                    try
                    {
                        state.NativeSocket.Connect(targetEndpoint);
                        state.Connected = true;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress or SocketError.AlreadyInProgress)
                    {
                        state.LastError = PosixEinprogress;
                        return PosixSocketFailure(ctx, PosixEinprogress);
                    }
                }
                else
                {
                    state.NativeSocket.Connect(targetEndpoint);
                    state.Connected = true;
                }

                state.BoundAddress = ipAddress;
                state.BoundPort = port;
                state.Bound = true;
                LogNet($"connect ok (native): fd={fd} ip={ipAddress} port={port}");
                ctx[CpuRegister.Rax] = 0;
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (SocketException ex)
            {
                state.LastError = MapSocketErrorToPosixErrno(ex.SocketErrorCode);
                LogNet($"connect failed: fd={fd} ip={ipAddress} port={port} err={state.LastError}");
                return PosixSocketFailure(ctx, state.LastError);
            }
        }

        if (!TryEstablishHostTcpConnection(ipAddress, port, out var client, out var stream))
        {
            LogNet($"connect failed (tcpclient): fd={fd} ip={ipAddress} port={port}");
            return PosixSocketFailure(ctx, PosixEconnrefused);
        }

        state.Client = client;
        state.Stream = stream;
        state.Connected = true;
        state.BoundAddress = ipAddress;
        state.BoundPort = port;
        state.Bound = true;
        ApplySocketOptions(state);

        LogNet($"connect ok (tcpclient): fd={fd} ip={ipAddress} port={port}");
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "KuOmgKoqCdY",
        ExportName = "bind",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Bind(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var sockaddrAddress = ctx[CpuRegister.Rsi];
        var addrlen = unchecked((int)ctx[CpuRegister.Rdx]);

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (sockaddrAddress == 0 || addrlen < 8)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!TryParseGuestSockaddrIn(sockaddrAddress, addrlen, ctx, out var ipAddress, out var port))
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                state.NativeSocket.Bind(new IPEndPoint(ipAddress, port));
                if (state.NativeSocket.LocalEndPoint is IPEndPoint lep)
                {
                    ipAddress = lep.Address;
                    port = lep.Port;
                }
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        state.BoundAddress = ipAddress;
        state.BoundPort = port;
        state.Bound = true;
        LogNet($"bind: fd={fd} ip={ipAddress} port={port}");
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "pxnCmagrtao",
        ExportName = "listen",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Listen(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var backlog = unchecked((int)ctx[CpuRegister.Rsi]);

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                state.NativeSocket.Listen(backlog);
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "3e+4Iv7IJ8U",
        ExportName = "accept",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Accept(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var addrAddress = ctx[CpuRegister.Rsi];
        var addrlenAddress = ctx[CpuRegister.Rdx];

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (state.NativeSocket is null)
        {
            return PosixSocketFailure(ctx, PosixEwouldblock);
        }

        Socket accepted;
        try
        {
            accepted = state.NativeSocket.Accept();
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress)
        {
            return PosixSocketFailure(ctx, PosixEwouldblock);
        }
        catch (SocketException ex)
        {
            return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
        }

        var acceptedFd = SocketRegistry.Allocate(state.Family, state.Type, state.Protocol, accepted);
        if (acceptedFd <= 0)
        {
            accepted.Dispose();
            return PosixSocketFailure(ctx, PosixEmfile);
        }

        if (addrAddress != 0 && addrlenAddress != 0 && accepted.RemoteEndPoint is IPEndPoint rep)
        {
            Span<byte> sockaddr = stackalloc byte[16];
            sockaddr[0] = 16;
            sockaddr[1] = 2;
            BinaryPrimitives.WriteUInt16BigEndian(sockaddr.Slice(2, 2), (ushort)rep.Port);
            rep.Address.GetAddressBytes().CopyTo(sockaddr.Slice(4, 4));
            ctx.Memory.TryWrite(addrAddress, sockaddr);

            Span<byte> lenBuf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lenBuf, 16);
            ctx.Memory.TryWrite(addrlenAddress, lenBuf);
        }

        ctx[CpuRegister.Rax] = unchecked((ulong)acceptedFd);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "fZOeZIOEmLw",
        ExportName = "send",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Send(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var bufferAddress = ctx[CpuRegister.Rsi];
        var len = unchecked((int)ctx[CpuRegister.Rdx]);
        var flags = unchecked((int)ctx[CpuRegister.Rcx]);

        if (len < 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (len == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        if (bufferAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        var payload = GC.AllocateUninitializedArray<byte>(len);
        if (!ctx.Memory.TryRead(bufferAddress, payload))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                var socketFlags = SocketFlags.None;
                if ((flags & 0x4) != 0) socketFlags |= SocketFlags.DontRoute;
                if ((flags & 0x1) != 0) socketFlags |= SocketFlags.OutOfBand;
                var sent = state.NativeSocket.Send(payload, socketFlags);
                ctx[CpuRegister.Rax] = unchecked((ulong)sent);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        if (state.Connected && state.Stream is not null)
        {
            try
            {
                state.Stream.Write(payload, 0, len);
                state.Stream.Flush();
                ctx[CpuRegister.Rax] = unchecked((ulong)len);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (IOException)
            {
                return PosixSocketFailure(ctx, PosixEconnreset);
            }
        }

        return PosixSocketFailure(ctx, PosixEnotconn);
    }

    [SysAbiExport(
        Nid = "oBr313PppNE",
        ExportName = "sendto",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int SendTo(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var bufferAddress = ctx[CpuRegister.Rsi];
        var len = unchecked((int)ctx[CpuRegister.Rdx]);
        var flags = unchecked((int)ctx[CpuRegister.Rcx]);
        var sockaddrAddress = ctx[CpuRegister.R8];
        var addrlen = unchecked((int)ctx[CpuRegister.R9]);

        if (sockaddrAddress == 0)
        {
            return Send(ctx);
        }

        if (len < 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (len == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        if (bufferAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (!TryParseGuestSockaddrIn(sockaddrAddress, addrlen, ctx, out var ipAddress, out var port))
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        var payload = GC.AllocateUninitializedArray<byte>(len);
        if (!ctx.Memory.TryRead(bufferAddress, payload))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                var socketFlags = SocketFlags.None;
                if ((flags & 0x4) != 0) socketFlags |= SocketFlags.DontRoute;
                var endpoint = new IPEndPoint(ipAddress, port);
                var sent = state.NativeSocket.SendTo(payload, socketFlags, endpoint);
                ctx[CpuRegister.Rax] = unchecked((ulong)sent);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        return PosixSocketFailure(ctx, PosixEnotconn);
    }

    [SysAbiExport(
        Nid = "Ez8xjo9UF4E",
        ExportName = "recv",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Recv(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var bufferAddress = ctx[CpuRegister.Rsi];
        var len = unchecked((int)ctx[CpuRegister.Rdx]);
        var flags = unchecked((int)ctx[CpuRegister.Rcx]);

        if (len < 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (len == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        if (bufferAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (state.NativeSocket is not null)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(len);
            try
            {
                var socketFlags = SocketFlags.None;
                if ((flags & 0x1) != 0) socketFlags |= SocketFlags.OutOfBand;
                if ((flags & 0x2) != 0) socketFlags |= SocketFlags.Peek;
                var received = state.NativeSocket.Receive(buffer, socketFlags);
                if (received > 0 && !ctx.Memory.TryWrite(bufferAddress, buffer.AsSpan(0, received)))
                {
                    return PosixSocketFailure(ctx, PosixEfault);
                }

                ctx[CpuRegister.Rax] = unchecked((ulong)received);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        if (state.Connected && state.Stream is not null)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(len);
            try
            {
                var received = state.Stream.Read(buffer, 0, len);
                if (received > 0 && !ctx.Memory.TryWrite(bufferAddress, buffer.AsSpan(0, received)))
                {
                    return PosixSocketFailure(ctx, PosixEfault);
                }

                ctx[CpuRegister.Rax] = unchecked((ulong)received);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (IOException)
            {
                return PosixSocketFailure(ctx, PosixEconnreset);
            }
        }

        return PosixSocketFailure(ctx, PosixEnotconn);
    }

    [SysAbiExport(
        Nid = "lUk6wrGXyMw",
        ExportName = "recvfrom",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int RecvFrom(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var bufferAddress = ctx[CpuRegister.Rsi];
        var len = unchecked((int)ctx[CpuRegister.Rdx]);
        var flags = unchecked((int)ctx[CpuRegister.Rcx]);
        var sockaddrAddress = ctx[CpuRegister.R8];
        var addrlenAddress = ctx[CpuRegister.R9];

        if (sockaddrAddress == 0)
        {
            return Recv(ctx);
        }

        if (len < 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (len == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        if (bufferAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (state.NativeSocket is not null)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(len);
            try
            {
                var socketFlags = SocketFlags.None;
                if ((flags & 0x1) != 0) socketFlags |= SocketFlags.OutOfBand;
                if ((flags & 0x2) != 0) socketFlags |= SocketFlags.Peek;
                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                var received = state.NativeSocket.ReceiveFrom(buffer, socketFlags, ref remoteEp);
                if (received > 0 && !ctx.Memory.TryWrite(bufferAddress, buffer.AsSpan(0, received)))
                {
                    return PosixSocketFailure(ctx, PosixEfault);
                }

                if (addrlenAddress != 0 && remoteEp is IPEndPoint rep)
                {
                    Span<byte> sockaddr = stackalloc byte[16];
                    sockaddr[0] = 16;
                    sockaddr[1] = 2;
                    BinaryPrimitives.WriteUInt16BigEndian(sockaddr.Slice(2, 2), (ushort)rep.Port);
                    rep.Address.GetAddressBytes().CopyTo(sockaddr.Slice(4, 4));
                    ctx.Memory.TryWrite(sockaddrAddress, sockaddr);

                    Span<byte> lenBuf = stackalloc byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(lenBuf, 16);
                    ctx.Memory.TryWrite(addrlenAddress, lenBuf);
                }

                ctx[CpuRegister.Rax] = unchecked((ulong)received);
                return (int)OrbisGen2Result.ORBIS_GEN2_OK;
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        return PosixSocketFailure(ctx, PosixEnotconn);
    }

    [SysAbiExport(
        Nid = "TUuiYS2kE8s",
        ExportName = "shutdown",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Shutdown(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var how = unchecked((int)ctx[CpuRegister.Rsi]);

        if (how is < 0 or > 2)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                state.NativeSocket.Shutdown((SocketShutdown)how);
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "RenI1lL1WFk",
        ExportName = "getsockname",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Getsockname(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var sockaddrAddress = ctx[CpuRegister.Rsi];
        var addrlenAddress = ctx[CpuRegister.Rdx];

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        Span<byte> addrlenBuffer = stackalloc byte[4];
        if (!ctx.Memory.TryRead(addrlenAddress, addrlenBuffer))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        var addrlen = BinaryPrimitives.ReadInt32LittleEndian(addrlenBuffer);
        if (addrlen < 8)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        var port = state.BoundPort;
        var ip = state.BoundAddress;
        if (state.NativeSocket?.LocalEndPoint is IPEndPoint ep)
        {
            port = ep.Port;
            ip = ep.Address;
        }

        Span<byte> sockaddr = stackalloc byte[16];
        sockaddr[0] = 16;
        sockaddr[1] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(sockaddr.Slice(2, 2), (ushort)port);
        ip.GetAddressBytes().CopyTo(sockaddr.Slice(4, 4));

        var writeLength = Math.Min(addrlen, 16);
        if (!ctx.Memory.TryWrite(sockaddrAddress, sockaddr.Slice(0, writeLength)))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        BinaryPrimitives.WriteInt32LittleEndian(addrlenBuffer, writeLength);
        ctx.Memory.TryWrite(addrlenAddress, addrlenBuffer);

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "fFxGkxF2bVo",
        ExportName = "setsockopt",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Setsockopt(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var level = unchecked((int)ctx[CpuRegister.Rsi]);
        var option = unchecked((int)ctx[CpuRegister.Rdx]);
        var valueAddress = ctx[CpuRegister.Rcx];
        var valueLength = unchecked((int)ctx[CpuRegister.R8]);

        if (valueAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (valueLength < sizeof(int))
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        Span<byte> valueBytes = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(valueAddress, valueBytes))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(valueBytes);
        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (!TrySetSocketOption(state, level, option, value))
        {
            LogNet($"setsockopt unsupported: fd={fd} level=0x{level:X} option=0x{option:X}");
            return PosixSocketFailure(ctx, PosixEinval);
        }

        LogNet($"setsockopt: fd={fd} level=0x{level:X} option=0x{option:X} value={value}");
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "6O8EwYOgH9Y",
        ExportName = "getsockopt",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Getsockopt(CpuContext ctx)
    {
        var fd = unchecked((int)ctx[CpuRegister.Rdi]);
        var level = unchecked((int)ctx[CpuRegister.Rsi]);
        var option = unchecked((int)ctx[CpuRegister.Rdx]);
        var valueAddress = ctx[CpuRegister.Rcx];
        var lengthAddress = ctx[CpuRegister.R8];

        if (valueAddress == 0 || lengthAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        if (!ctx.Memory.TryRead(lengthAddress, lengthBytes))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(lengthBytes) < sizeof(int))
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (!SocketRegistry.TryGet(fd, out var state) || state is null)
        {
            return PosixSocketFailure(ctx, PosixEbadf);
        }

        if (!TryGetSocketOption(state, level, option, out var value))
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        Span<byte> valueBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(valueBytes, value);
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, sizeof(int));
        if (!ctx.Memory.TryWrite(valueAddress, valueBytes) ||
            !ctx.Memory.TryWrite(lengthAddress, lengthBytes))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "T8fER+tIGgk",
        ExportName = "select",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Select(CpuContext ctx)
    {
        var nfds = unchecked((int)ctx[CpuRegister.Rdi]);
        var readfdsAddr = ctx[CpuRegister.Rsi];
        var writefdsAddr = ctx[CpuRegister.Rdx];
        var exceptfdsAddr = ctx[CpuRegister.Rcx];
        var timeoutAddr = ctx[CpuRegister.R8];

        if (nfds < 0 || nfds > 1024)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        int microSeconds = -1;
        if (timeoutAddr != 0)
        {
            Span<byte> tv = stackalloc byte[16];
            if (ctx.Memory.TryRead(timeoutAddr, tv))
            {
                var sec = BinaryPrimitives.ReadInt64LittleEndian(tv[..8]);
                var usec = BinaryPrimitives.ReadInt64LittleEndian(tv[8..16]);
                microSeconds = (int)Math.Min(int.MaxValue, sec * 1_000_000 + usec);
            }
        }

        List<Socket> checkRead = new();
        List<Socket> checkWrite = new();
        List<Socket> checkError = new();
        Dictionary<Socket, int> socketToFd = new();

        void CollectFds(ulong addr, List<Socket> list)
        {
            if (addr == 0) return;
            Span<byte> buf = stackalloc byte[128];
            var byteCount = (nfds + 7) / 8;
            if (byteCount <= buf.Length && ctx.Memory.TryRead(addr, buf[..byteCount]))
            {
                for (var fd = 0; fd < nfds; fd++)
                {
                    if ((buf[fd / 8] & (1 << (fd % 8))) != 0 &&
                        SocketRegistry.TryGet(fd, out var sock) && sock?.NativeSocket is not null)
                    {
                        list.Add(sock.NativeSocket);
                        socketToFd[sock.NativeSocket] = fd;
                    }
                }
            }
        }

        CollectFds(readfdsAddr, checkRead);
        CollectFds(writefdsAddr, checkWrite);
        CollectFds(exceptfdsAddr, checkError);

        if (checkRead.Count > 0 || checkWrite.Count > 0 || checkError.Count > 0)
        {
            try
            {
                System.Net.Sockets.Socket.Select(checkRead, checkWrite, checkError, microSeconds);
            }
            catch (SocketException ex)
            {
                return PosixSocketFailure(ctx, MapSocketErrorToPosixErrno(ex.SocketErrorCode));
            }
        }
        else if (microSeconds > 0)
        {
            Thread.Sleep(microSeconds / 1000);
        }

        void WriteFds(ulong addr, List<Socket> list)
        {
            if (addr == 0) return;
            Span<byte> buf = stackalloc byte[128];
            var byteCount = (nfds + 7) / 8;
            buf[..byteCount].Clear();
            foreach (var s in list)
            {
                if (socketToFd.TryGetValue(s, out var fd))
                {
                    buf[fd / 8] |= (byte)(1 << (fd % 8));
                }
            }
            ctx.Memory.TryWrite(addr, buf[..byteCount]);
        }

        WriteFds(readfdsAddr, checkRead);
        WriteFds(writefdsAddr, checkWrite);
        WriteFds(exceptfdsAddr, checkError);

        var total = checkRead.Count + checkWrite.Count + checkError.Count;
        ctx[CpuRegister.Rax] = unchecked((ulong)total);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "9oiX1kyeedA",
        ExportName = "bzero",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Bzero(CpuContext ctx)
    {
        var address = ctx[CpuRegister.Rdi];
        var length = unchecked((int)ctx[CpuRegister.Rsi]);
        if (length > 0 && address != 0)
        {
            var zeros = new byte[length];
            ctx.Memory.TryWrite(address, zeros);
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "4n51s0zEf0c",
        ExportName = "inet_pton",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int InetPton(CpuContext ctx)
    {
        var af = unchecked((int)ctx[CpuRegister.Rdi]);
        var srcAddress = ctx[CpuRegister.Rsi];
        var dstAddress = ctx[CpuRegister.Rdx];
        if (af != 2 || srcAddress == 0 || dstAddress == 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        if (!TryReadCString(srcAddress, ctx, out var text) ||
            !TryParseIpv4Address(text, out var octets))
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_OK;
        }

        Span<byte> packed = stackalloc byte[4];
        packed[0] = octets[0];
        packed[1] = octets[1];
        packed[2] = octets[2];
        packed[3] = octets[3];
        if (!ctx.Memory.TryWrite(dstAddress, packed))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        ctx[CpuRegister.Rax] = 1;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "5jRCs2axtr4",
        ExportName = "inet_ntop",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int InetNtop(CpuContext ctx)
    {
        var af = unchecked((int)ctx[CpuRegister.Rdi]);
        var srcAddress = ctx[CpuRegister.Rsi];
        var dstAddress = ctx[CpuRegister.Rdx];
        var size = unchecked((int)ctx[CpuRegister.Rcx]);

        if (af != 2 || srcAddress == 0 || dstAddress == 0 || size <= 0)
        {
            return PosixSocketFailure(ctx, PosixEinval);
        }

        Span<byte> packed = stackalloc byte[4];
        if (!ctx.Memory.TryRead(srcAddress, packed))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        var text = FormattableString.Invariant($"{packed[0]}.{packed[1]}.{packed[2]}.{packed[3]}");
        var encoded = Encoding.ASCII.GetBytes(text);
        if (encoded.Length + 1 > size)
        {
            return PosixSocketFailure(ctx, 28); // ENOSPC
        }

        var destination = new byte[encoded.Length + 1];
        Array.Copy(encoded, destination, encoded.Length);
        destination[^1] = 0;
        if (!ctx.Memory.TryWrite(dstAddress, destination))
        {
            return PosixSocketFailure(ctx, PosixEfault);
        }

        ctx[CpuRegister.Rax] = dstAddress;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "jogUIsOV3-U",
        ExportName = "htons",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int Htons(CpuContext ctx)
    {
        var value = unchecked((ushort)ctx[CpuRegister.Rdi]);
        var swapped = (ushort)(((value & 0x00FF) << 8) | ((value >> 8) & 0x00FF));
        ctx[CpuRegister.Rax] = swapped;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    internal static bool TrySetSocketOption(
        SocketRegistry.EmulatedSocket state,
        int level,
        int option,
        int value)
    {
        switch (level, option)
        {
            case (0xFFFF, 0x1200): // SO_NBIO
                state.SetNonBlocking(value != 0);
                break;
            case (0xFFFF, 0x0004): // SO_REUSEADDR
                state.ReuseAddress = value != 0;
                break;
            case (0xFFFF, 0x0008): // SO_KEEPALIVE
                state.KeepAlive = value != 0;
                break;
            case (0xFFFF, 0x0020): // SO_BROADCAST
                state.Broadcast = value != 0;
                break;
            case (0xFFFF, 0x0200): // SO_REUSEPORT
                state.ReusePort = value != 0;
                break;
            case (0xFFFF, 0x1001) when value > 0: // SO_RCVBUF
                state.ReceiveBufferSize = value;
                break;
            case (0xFFFF, 0x1002) when value > 0: // SO_SNDBUF
                state.SendBufferSize = value;
                break;
            case (0xFFFF, 0x1003) when value > 0:
                state.SendLowWater = value;
                break;
            case (0xFFFF, 0x1004) when value > 0:
                state.ReceiveLowWater = value;
                break;
            case (41, 27) when state.Family == 28:
                state.IPv6Only = value != 0;
                break;
            case (6, 1) when state.Type == 1: // TCP_NODELAY
                state.NoDelay = value != 0;
                break;
            default:
                return false;
        }

        ApplySocketOptions(state);
        return true;
    }

    internal static bool TryGetSocketOption(
        SocketRegistry.EmulatedSocket state,
        int level,
        int option,
        out int value)
    {
        value = (level, option) switch
        {
            (0xFFFF, 0x1200) => state.IsNonBlocking() ? 1 : 0,
            (0xFFFF, 0x0004) => state.NativeSocket is not null
                ? (int)state.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress)!
                : (state.ReuseAddress ? 1 : 0),
            (0xFFFF, 0x0008) => state.NativeSocket is not null
                ? (int)state.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!
                : (state.KeepAlive ? 1 : 0),
            (0xFFFF, 0x0020) => state.Broadcast ? 1 : 0,
            (0xFFFF, 0x0200) => state.ReusePort ? 1 : 0,
            (0xFFFF, 0x1001) => state.NativeSocket?.ReceiveBufferSize ?? state.ReceiveBufferSize,
            (0xFFFF, 0x1002) => state.NativeSocket?.SendBufferSize ?? state.SendBufferSize,
            (0xFFFF, 0x1003) => state.SendLowWater,
            (0xFFFF, 0x1004) => state.ReceiveLowWater,
            (0xFFFF, 0x1007) => GetSocketErrorOption(state),
            (41, 27) when state.Family == 28 => state.IPv6Only ? 1 : 0,
            (6, 1) when state.Type == 1 => state.NoDelay ? 1 : 0,
            _ => -1,
        };
        return value >= 0;
    }

    private static int GetSocketErrorOption(SocketRegistry.EmulatedSocket state)
    {
        if (state.LastError != 0)
        {
            var err = state.LastError;
            state.LastError = 0;
            return err;
        }

        if (state.NativeSocket is not null)
        {
            try
            {
                var raw = state.NativeSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                var errInt = raw is int i ? i : 0;
                return errInt != 0 ? MapSocketErrorToPosixErrno((SocketError)errInt) : 0;
            }
            catch
            {
                return PosixEconnrefused;
            }
        }

        return 0;
    }

    private static void ApplySocketOptions(SocketRegistry.EmulatedSocket state)
    {
        var socket = state.NativeSocket ?? state.Client?.Client;
        if (socket is null)
        {
            return;
        }

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, state.ReuseAddress);
            if (socket.SocketType == SocketType.Stream)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, state.KeepAlive);
                socket.NoDelay = state.NoDelay;
            }

            if (socket.SocketType == SocketType.Dgram)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, state.Broadcast);
            }

            if (state.SendBufferSize > 0)
            {
                socket.SendBufferSize = state.SendBufferSize;
            }

            if (state.ReceiveBufferSize > 0)
            {
                socket.ReceiveBufferSize = state.ReceiveBufferSize;
            }

            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = !state.IPv6Only;
            }
        }
        catch (SocketException)
        {
        }
    }

    private static int PosixSocketFailure(CpuContext ctx, int errno)
    {
        KernelRuntimeCompatExports.TrySetErrno(ctx, errno);
        ctx[CpuRegister.Rax] = ulong.MaxValue;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    private static bool TryParseGuestSockaddrIn(
        ulong address,
        int addrlen,
        CpuContext ctx,
        out IPAddress ipAddress,
        out int port)
    {
        ipAddress = IPAddress.None;
        port = 0;
        if (address == 0 || addrlen < 8)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[16];
        var readLength = Math.Min(addrlen, buffer.Length);
        if (!ctx.Memory.TryRead(address, buffer.Slice(0, readLength)))
        {
            return false;
        }

        if (buffer[1] != 2)
        {
            return false;
        }

        port = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
        ipAddress = new IPAddress(buffer.Slice(4, 4).ToArray());
        return true;
    }

    private static void LogNet(string message)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_NET"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[LOADER][DEBUG] {message}");
        }
    }

    private static bool TryApplyNetRedirect(ref IPAddress ipAddress)
    {
        var redirect = Environment.GetEnvironmentVariable("CRAZIIEMU_NET_REDIRECT");
        if (string.IsNullOrWhiteSpace(redirect))
        {
            return false;
        }

        if (!IPAddress.TryParse(redirect.Trim(), out var redirectAddress))
        {
            return false;
        }

        ipAddress = redirectAddress;
        return true;
    }

    internal static int MapSocketErrorToPosixErrno(SocketError socketError)
    {
        return socketError switch
        {
            SocketError.WouldBlock or SocketError.IOPending => PosixEwouldblock,
            SocketError.InProgress => PosixEinprogress,
            SocketError.DestinationAddressRequired => PosixEdestaddrreq,
            SocketError.MessageSize => PosixEmsgsize,
            SocketError.ProtocolType => 41,
            SocketError.ProtocolOption => 42,
            SocketError.ProtocolNotSupported => PosixEprotonosupport,
            SocketError.OperationNotSupported => PosixEopnotsupp,
            SocketError.AddressFamilyNotSupported => PosixEafnosupport,
            SocketError.AddressAlreadyInUse => 48,
            SocketError.AddressNotAvailable => PosixEaddrnotavail,
            SocketError.NetworkDown => PosixEnetdown,
            SocketError.NetworkUnreachable => PosixEnetunreach,
            SocketError.ConnectionAborted => PosixEconnaborted,
            SocketError.ConnectionReset => PosixEconnreset,
            SocketError.NoBufferSpaceAvailable => 55,
            SocketError.IsConnected => PosixEisconn,
            SocketError.NotConnected => PosixEnotconn,
            SocketError.TimedOut => PosixEtimedout,
            SocketError.ConnectionRefused => PosixEconnrefused,
            SocketError.HostDown => 64,
            SocketError.HostUnreachable => 65,
            SocketError.AccessDenied => 13,
            _ => PosixEinval,
        };
    }

    private static bool TryEstablishHostTcpConnection(
        IPAddress ipAddress,
        int port,
        out TcpClient client,
        out NetworkStream stream)
    {
        client = null!;
        stream = null!;
        if (!TryConnectTcpClient(ipAddress, port, out client))
        {
            return false;
        }

        stream = client.GetStream();
        return true;
    }

    private static bool TryConnectTcpClient(IPAddress ipAddress, int port, out TcpClient client)
    {
        client = new TcpClient();
        try
        {
            client.Connect(ipAddress, port);
            return true;
        }
        catch (SocketException)
        {
            client.Dispose();
            client = null!;
            return false;
        }
        catch (IOException)
        {
            client.Dispose();
            client = null!;
            return false;
        }
    }

    private static bool TryReadCString(ulong address, CpuContext ctx, out string text)
    {
        const int maxLength = 64;
        var buffer = new byte[maxLength];
        var length = 0;
        for (; length < maxLength; length++)
        {
            if (!ctx.Memory.TryRead(address + (ulong)length, buffer.AsSpan(length, 1)))
            {
                text = string.Empty;
                return false;
            }

            if (buffer[length] == 0)
            {
                break;
            }
        }

        text = Encoding.ASCII.GetString(buffer, 0, length);
        return true;
    }

    private static bool TryParseIpv4Address(string text, out byte[] octets)
    {
        octets = Array.Empty<byte>();
        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        var parsed = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out parsed[i]))
            {
                return false;
            }
        }

        octets = parsed;
        return true;
    }
}
