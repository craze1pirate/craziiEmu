// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.Generated;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.Network;

namespace CraziiEmu.TestRunner;

public static class KernelSocketErrnoTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[1024 * 1024];

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address + (ulong)destination.Length > (ulong)_ram.Length) return false;
            _ram.AsSpan((int)address, destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
        {
            if (address + (ulong)source.Length > (ulong)_ram.Length) return false;
            source.CopyTo(_ram.AsSpan((int)address, source.Length));
            return true;
        }

        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => true;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting KernelSocketErrnoTests...");

        TestInvalidDescriptorErrno();
        TestNonBlockingConnectNoListenerErrno();
        TestBlockingConnectRefusedErrno();
        TestSuccessfulLocalConnectionErrno();
        TestSceNetConnectUnchanged();
        TestRepeatedConnectStress();

        Console.WriteLine("[TEST] KernelSocketErrnoTests PASSED cleanly.");
    }

    private static int ReadErrno(CpuContext ctx)
    {
        KernelRuntimeCompatExports.ErrorAddress(ctx);
        var errnoAddress = ctx[CpuRegister.Rax];
        Span<byte> buf = stackalloc byte[4];
        if (ctx.Memory.TryRead(errnoAddress, buf))
        {
            return BinaryPrimitives.ReadInt32LittleEndian(buf);
        }
        return -1;
    }

    private static void SetupContext(out DummyMemory mem, out CpuContext ctx)
    {
        mem = new DummyMemory();
        ctx = new CpuContext(mem, Generation.Gen5);
        // Set FsBase so TLS scratch (errno at FsBase + 0x40) is accessible
        ctx.FsBase = 0x10000;
    }

    private static void TestInvalidDescriptorErrno()
    {
        SetupContext(out var mem, out var ctx);

        ctx[CpuRegister.Rdi] = 999999; // invalid fd
        ctx[CpuRegister.Rsi] = 0x2000;
        ctx[CpuRegister.Rdx] = 16;
        var res = KernelSocketCompatExports.Connect(ctx);
        var ret = (long)ctx[CpuRegister.Rax];
        if (ret != -1)
        {
            throw new InvalidOperationException($"Expected connect on invalid fd to return -1, got {ret}");
        }
        var err = ReadErrno(ctx);
        if (err != 9) // EBADF
        {
            throw new InvalidOperationException($"Expected errno=9 (EBADF) on invalid fd, got {err}");
        }
        Console.WriteLine("  [PASS] Invalid descriptor returns -1 and errno=9 (EBADF)");
    }

    private static void TestNonBlockingConnectNoListenerErrno()
    {
        SetupContext(out var mem, out var ctx);

        // 1. Create socket
        ctx[CpuRegister.Rdi] = 2; // AF_INET
        ctx[CpuRegister.Rsi] = 1; // SOCK_STREAM
        ctx[CpuRegister.Rdx] = 6; // IPPROTO_TCP
        KernelSocketCompatExports.Socket(ctx);
        var fd = ctx[CpuRegister.Rax];

        // 2. Set SO_NBIO (non-blocking)
        ulong optvalAddr = 0x3000;
        Span<byte> valBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 1);
        mem.TryWrite(optvalAddr, valBuf);

        ctx[CpuRegister.Rdi] = fd;
        ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        ctx[CpuRegister.Rdx] = 0x1200; // SO_NBIO
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = 4;
        KernelSocketCompatExports.Setsockopt(ctx);

        // 3. Connect to 127.0.0.1:8096 (no listener)
        ulong sockaddrAddr = 0x4000;
        Span<byte> sockaddrBuf = stackalloc byte[16];
        sockaddrBuf[0] = 16;
        sockaddrBuf[1] = 2; // AF_INET
        BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], 8096);
        sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1;
        mem.TryWrite(sockaddrAddr, sockaddrBuf);

        ctx[CpuRegister.Rdi] = fd;
        ctx[CpuRegister.Rsi] = sockaddrAddr;
        ctx[CpuRegister.Rdx] = 16;
        KernelSocketCompatExports.Connect(ctx);
        var ret = (long)ctx[CpuRegister.Rax];
        if (ret != -1)
        {
            throw new InvalidOperationException($"Expected non-blocking connect to return -1, got {ret}");
        }

        var err = ReadErrno(ctx);
        if (err != 36) // EINPROGRESS
        {
            throw new InvalidOperationException($"Expected errno=36 (EINPROGRESS) for non-blocking connect, got {err}");
        }
        Console.WriteLine("  [PASS] Non-blocking connect with no listener returns -1 and errno=36 (EINPROGRESS)");
    }

    private static void TestBlockingConnectRefusedErrno()
    {
        SetupContext(out var mem, out var ctx);

        // 1. Create socket
        KernelSocketCompatExports.Socket(ctx);
        var fd = ctx[CpuRegister.Rax];

        // 2. Connect blocking to non-existent port 59999
        ulong sockaddrAddr = 0x4000;
        Span<byte> sockaddrBuf = stackalloc byte[16];
        sockaddrBuf[0] = 16;
        sockaddrBuf[1] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], 59999);
        sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1;
        mem.TryWrite(sockaddrAddr, sockaddrBuf);

        ctx[CpuRegister.Rdi] = fd;
        ctx[CpuRegister.Rsi] = sockaddrAddr;
        ctx[CpuRegister.Rdx] = 16;
        KernelSocketCompatExports.Connect(ctx);
        var ret = (long)ctx[CpuRegister.Rax];
        if (ret != -1)
        {
            throw new InvalidOperationException($"Expected blocking connect to return -1, got {ret}");
        }

        var err = ReadErrno(ctx);
        if (err != 61) // ECONNREFUSED
        {
            throw new InvalidOperationException($"Expected errno=61 (ECONNREFUSED) for refused blocking connect, got {err}");
        }
        Console.WriteLine("  [PASS] Blocking connect refused returns -1 and errno=61 (ECONNREFUSED)");
    }

    private static void TestSuccessfulLocalConnectionErrno()
    {
        SetupContext(out var mem, out var ctx);

        // Start temporary local TCP listener
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            KernelSocketCompatExports.Socket(ctx);
            var fd = ctx[CpuRegister.Rax];

            ulong sockaddrAddr = 0x4000;
            Span<byte> sockaddrBuf = stackalloc byte[16];
            sockaddrBuf[0] = 16;
            sockaddrBuf[1] = 2;
            BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], (ushort)port);
            sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1;
            mem.TryWrite(sockaddrAddr, sockaddrBuf);

            ctx[CpuRegister.Rdi] = fd;
            ctx[CpuRegister.Rsi] = sockaddrAddr;
            ctx[CpuRegister.Rdx] = 16;
            KernelSocketCompatExports.Connect(ctx);
            var ret = (long)ctx[CpuRegister.Rax];
            if (ret != 0)
            {
                throw new InvalidOperationException($"Expected successful connect to return 0, got {ret}");
            }
            Console.WriteLine("  [PASS] Successful local connection returns 0 cleanly");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TestSceNetConnectUnchanged()
    {
        SetupContext(out var mem, out var ctx);

        NetExports.NetInit(ctx);
        mem.TryWrite(0x1000, "test_scenet\0"u8);
        ctx[CpuRegister.Rdi] = 0x1000;
        ctx[CpuRegister.Rsi] = 2;
        ctx[CpuRegister.Rdx] = 1;
        ctx[CpuRegister.Rcx] = 6;
        NetExports.NetSocket(ctx);
        var fd = ctx[CpuRegister.Rax];

        ulong sockaddrAddr = 0x4000;
        Span<byte> sockaddrBuf = stackalloc byte[16];
        sockaddrBuf[0] = 16;
        sockaddrBuf[1] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], 8096);
        sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1;
        mem.TryWrite(sockaddrAddr, sockaddrBuf);

        ctx[CpuRegister.Rdi] = fd;
        ctx[CpuRegister.Rsi] = sockaddrAddr;
        ctx[CpuRegister.Rdx] = 16;
        var res = NetExports.NetConnect(ctx);
        if (res != -1)
        {
            throw new InvalidOperationException($"Expected sceNetConnect on unconnected nonblocking to return -1, got {res}");
        }
        Console.WriteLine("  [PASS] Existing sceNetConnect behavior preserved");
    }

    private static void TestRepeatedConnectStress()
    {
        SetupContext(out var mem, out var ctx);

        ulong sockaddrAddr = 0x4000;
        Span<byte> sockaddrBuf = stackalloc byte[16];
        sockaddrBuf[0] = 16;
        sockaddrBuf[1] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], 8096);
        sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1;
        mem.TryWrite(sockaddrAddr, sockaddrBuf);

        ulong optvalAddr = 0x3000;
        Span<byte> valBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 1);
        mem.TryWrite(optvalAddr, valBuf);

        for (int i = 0; i < 100; i++)
        {
            KernelSocketCompatExports.Socket(ctx);
            var fd = ctx[CpuRegister.Rax];

            ctx[CpuRegister.Rdi] = fd;
            ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
            ctx[CpuRegister.Rdx] = 0x1200; // SO_NBIO
            ctx[CpuRegister.Rcx] = optvalAddr;
            ctx[CpuRegister.R8] = 4;
            KernelSocketCompatExports.Setsockopt(ctx);

            ctx[CpuRegister.Rdi] = fd;
            ctx[CpuRegister.Rsi] = sockaddrAddr;
            ctx[CpuRegister.Rdx] = 16;
            KernelSocketCompatExports.Connect(ctx);
            var ret = (long)ctx[CpuRegister.Rax];
            if (ret != -1)
            {
                throw new InvalidOperationException($"Iteration {i}: Expected -1, got {ret}");
            }
            var err = ReadErrno(ctx);
            if (err != 36)
            {
                throw new InvalidOperationException($"Iteration {i}: Expected errno=36, got {err}");
            }
            KernelSocketCompatExports.TryCloseSocketFd((int)fd);
        }
        Console.WriteLine("  [PASS] 100-iteration connect error stress test completed cleanly");
    }
}
