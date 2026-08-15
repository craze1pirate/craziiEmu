// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.Generated;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Network;

namespace CraziiEmu.TestRunner;

public static class NetSocketOptionTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[65536];

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
        Console.WriteLine("[TEST] Starting NetSocketOptionTests...");

        TestNidResolution();
        TestSocketOptionsFull();

        Console.WriteLine("[TEST] NetSocketOptionTests PASSED cleanly.");
    }

    private static void TestNidResolution()
    {
        var stubs = SysAbiExportRegistry.CreateExports(Generation.Gen4 | Generation.Gen5);
        bool foundGetsockopt = false;
        foreach (var stub in stubs)
        {
            if (stub.Nid == "xphrZusl78E")
            {
                if (stub.Name != "sceNetGetsockopt")
                {
                    throw new InvalidOperationException($"Expected NID xphrZusl78E to be sceNetGetsockopt, got {stub.Name}");
                }
                foundGetsockopt = true;
            }
        }

        if (!foundGetsockopt)
        {
            throw new InvalidOperationException("NID xphrZusl78E not found in registered HLE stubs.");
        }
        Console.WriteLine("  [PASS] NID xphrZusl78E resolves to sceNetGetsockopt");
    }

    private static void TestSocketOptionsFull()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Initialize Net
        NetExports.NetInit(ctx);

        // 2. Create Socket
        mem.TryWrite(0x1000, "test_socket\0"u8);
        ctx[CpuRegister.Rdi] = 0x1000;
        ctx[CpuRegister.Rsi] = 2; // AF_INET
        ctx[CpuRegister.Rdx] = 1; // SOCK_STREAM
        ctx[CpuRegister.Rcx] = 6; // IPPROTO_TCP
        var res = NetExports.NetSocket(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSocket failed: {res}");
        int socketFd = (int)ctx[CpuRegister.Rax];

        // 3. Test SO_NBIO (set non-blocking = 1)
        ulong optvalAddr = 0x2000;
        ulong optlenAddr = 0x2010;

        Span<byte> valBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 1);
        mem.TryWrite(optvalAddr, valBuf);

        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        ctx[CpuRegister.Rdx] = 0x1200; // SO_NBIO
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSetsockopt SO_NBIO failed: {res}");

        // 4. Test SO_NBIO (get)
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 4);
        mem.TryWrite(optlenAddr, valBuf);
        mem.TryWrite(optvalAddr, stackalloc byte[4]); // clear buffer

        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        ctx[CpuRegister.Rdx] = 0x1200; // SO_NBIO
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_NBIO failed: {res}");

        mem.TryRead(optvalAddr, valBuf);
        int fetchedNb = BinaryPrimitives.ReadInt32LittleEndian(valBuf);
        if (fetchedNb != 1) throw new InvalidOperationException($"Expected SO_NBIO = 1, got {fetchedNb}");
        Console.WriteLine("  [PASS] SO_NBIO set/get matches non-blocking state");

        // 5. Test SO_REUSEADDR
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 1);
        mem.TryWrite(optvalAddr, valBuf);
        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = 0xFFFF;
        ctx[CpuRegister.Rdx] = 0x0004; // SO_REUSEADDR
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSetsockopt SO_REUSEADDR failed: {res}");

        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_REUSEADDR failed: {res}");
        Console.WriteLine("  [PASS] SO_REUSEADDR set/get passed");

        // 6. Test SO_KEEPALIVE
        ctx[CpuRegister.Rdx] = 0x0008; // SO_KEEPALIVE
        ctx[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSetsockopt SO_KEEPALIVE failed: {res}");
        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_KEEPALIVE failed: {res}");
        Console.WriteLine("  [PASS] SO_KEEPALIVE set/get passed");

        // 7. Test SO_RCVBUF
        BinaryPrimitives.WriteInt32LittleEndian(valBuf, 65536);
        mem.TryWrite(optvalAddr, valBuf);
        ctx[CpuRegister.Rdx] = 0x1001; // SO_RCVBUF
        ctx[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSetsockopt SO_RCVBUF failed: {res}");
        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_RCVBUF failed: {res}");
        Console.WriteLine("  [PASS] SO_RCVBUF set/get passed");

        // 8. Test SO_SNDBUF
        ctx[CpuRegister.Rdx] = 0x1002; // SO_SNDBUF
        ctx[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetSetsockopt SO_SNDBUF failed: {res}");
        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_SNDBUF failed: {res}");
        Console.WriteLine("  [PASS] SO_SNDBUF set/get passed");

        // 9. Test Invalid Option
        ctx[CpuRegister.Rdx] = 0x9999;
        res = NetExports.NetSetsockopt(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for invalid optname 0x9999");
        res = NetExports.NetGetsockopt(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for invalid optname 0x9999");
        Console.WriteLine("  [PASS] Invalid option returns error correctly");

        // 10. Test Invalid Socket Handle
        ctx[CpuRegister.Rdi] = 99999;
        ctx[CpuRegister.Rdx] = 0x1200;
        res = NetExports.NetSetsockopt(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for invalid socket handle");
        res = NetExports.NetGetsockopt(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for invalid socket handle");
        Console.WriteLine("  [PASS] Invalid socket handle returns error correctly");

        // 11. Test Invalid Pointers
        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rcx] = 0; // null optval
        res = NetExports.NetGetsockopt(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for null optval pointer");
        Console.WriteLine("  [PASS] Invalid pointers return error correctly");

        // 12. Test sceNetConnect Non-Blocking & Invalid Parameters
        ulong sockaddrAddr = 0x3000;
        Span<byte> sockaddrBuf = stackalloc byte[16];
        sockaddrBuf[0] = 16; // sa_len
        sockaddrBuf[1] = 2;  // AF_INET
        BinaryPrimitives.WriteUInt16BigEndian(sockaddrBuf[2..4], 8080); // port 8080
        sockaddrBuf[4] = 127; sockaddrBuf[5] = 0; sockaddrBuf[6] = 0; sockaddrBuf[7] = 1; // 127.0.0.1
        mem.TryWrite(sockaddrAddr, sockaddrBuf);

        // Non-blocking socket connect
        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = sockaddrAddr;
        ctx[CpuRegister.Rdx] = 16;
        res = NetExports.NetConnect(ctx);
        if (res != -1)
        {
            throw new InvalidOperationException($"Expected NetConnect non-blocking to return -1, got {res}");
        }
        Console.WriteLine("  [PASS] NetConnect non-blocking returned -1 cleanly");

        // Invalid socket handle
        ctx[CpuRegister.Rdi] = 999999;
        res = NetExports.NetConnect(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for NetConnect with invalid socket handle");
        Console.WriteLine("  [PASS] NetConnect invalid socket handle rejected cleanly");

        // Null sockaddr pointer
        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = 0;
        res = NetExports.NetConnect(ctx);
        if (res == 0) throw new InvalidOperationException("Expected failure for NetConnect with null sockaddr pointer");
        Console.WriteLine("  [PASS] NetConnect null sockaddr pointer rejected cleanly");

        // 13. Test SO_ERROR reporting on unconnected non-blocking socket
        ctx[CpuRegister.Rdi] = (ulong)socketFd;
        ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        ctx[CpuRegister.Rdx] = 0x1007; // SO_ERROR
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(ctx);
        if (res != 0) throw new InvalidOperationException($"NetGetsockopt SO_ERROR failed: {res}");
        mem.TryRead(optvalAddr, valBuf);
        int soErr = BinaryPrimitives.ReadInt32LittleEndian(valBuf);
        if (soErr == 0)
        {
            throw new InvalidOperationException("Expected non-zero SO_ERROR for unconnected socket");
        }
        Console.WriteLine($"  [PASS] SO_ERROR query returned error code {soErr} correctly for unconnected non-blocking socket");
    }
}
