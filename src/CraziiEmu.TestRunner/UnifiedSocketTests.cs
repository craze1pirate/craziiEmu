// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.Network;

namespace CraziiEmu.TestRunner;

public static class UnifiedSocketTests
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
        Console.WriteLine("[TEST] Starting UnifiedSocketTests...");

        TestKernelSocketConfiguredViaSceNet();
        TestSceNetSocketConfiguredViaKernel();
        TestCrossApiClose();
        Test1000IterationCrossApiStress();

        Console.WriteLine("[TEST] UnifiedSocketTests PASSED cleanly.");
    }

    private static void TestKernelSocketConfiguredViaSceNet()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen4 | Generation.Gen5);

        // 1. Create socket via libkernel:socket()
        ctx[CpuRegister.Rdi] = 2; // AF_INET
        ctx[CpuRegister.Rsi] = 1; // SOCK_STREAM
        ctx[CpuRegister.Rdx] = 6; // IPPROTO_TCP
        var sockRes = KernelSocketCompatExports.Socket(ctx);
        if (sockRes != 0) throw new InvalidOperationException($"Kernel socket creation failed with {sockRes}");
        var fd = (int)ctx[CpuRegister.Rax];
        if (fd <= 0) throw new InvalidOperationException($"Invalid kernel fd: {fd}");

        // 2. Configure socket via libSceNet:sceNetSetsockopt (SO_NBIO = 0x1200)
        ulong optvalAddr = 0x1000;
        ulong optlenAddr = 0x1020;
        Span<byte> valBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valBytes, 1);
        mem.TryWrite(optvalAddr, valBytes);

        ctx[CpuRegister.Rdi] = (ulong)fd;
        ctx[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        ctx[CpuRegister.Rdx] = 0x1200; // SO_NBIO
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = 4;
        var setRes = NetExports.NetSetsockopt(ctx);
        if (setRes != 0) throw new InvalidOperationException($"sceNetSetsockopt failed with {setRes} on kernel fd {fd}");

        // 3. Query via libkernel:getsockopt
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBytes, 4);
        mem.TryWrite(optlenAddr, lenBytes);

        ctx[CpuRegister.Rdi] = (ulong)fd;
        ctx[CpuRegister.Rsi] = 0xFFFF;
        ctx[CpuRegister.Rdx] = 0x1200;
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = optlenAddr;
        var getResKernel = KernelSocketCompatExports.Getsockopt(ctx);
        if (getResKernel != 0) throw new InvalidOperationException($"kernel getsockopt failed with {getResKernel}");

        mem.TryRead(optvalAddr, valBytes);
        var readVal = BinaryPrimitives.ReadInt32LittleEndian(valBytes);
        if (readVal != 1) throw new InvalidOperationException($"Expected SO_NBIO=1, got {readVal}");

        // 4. Query via libSceNet:sceNetGetsockopt
        mem.TryWrite(optlenAddr, lenBytes);
        var getResSceNet = NetExports.NetGetsockopt(ctx);
        if (getResSceNet != 0) throw new InvalidOperationException($"sceNetGetsockopt failed with {getResSceNet}");

        mem.TryRead(optvalAddr, valBytes);
        readVal = BinaryPrimitives.ReadInt32LittleEndian(valBytes);
        if (readVal != 1) throw new InvalidOperationException($"Expected sceNetGetsockopt SO_NBIO=1, got {readVal}");

        SocketRegistry.TryClose(fd);
        Console.WriteLine("  [PASS] 1. Kernel socket configured via libSceNet:sceNetSetsockopt verified");
    }

    private static void TestSceNetSocketConfiguredViaKernel()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen4 | Generation.Gen5);

        // 1. Create socket via libSceNet:sceNetSocket()
        ctx[CpuRegister.Rdi] = 0; // name
        ctx[CpuRegister.Rsi] = 2; // AF_INET
        ctx[CpuRegister.Rdx] = 1; // SOCK_STREAM
        ctx[CpuRegister.Rcx] = 6; // IPPROTO_TCP
        var sockRes = NetExports.NetSocket(ctx);
        if (sockRes != 0) throw new InvalidOperationException($"NetSocket creation failed with {sockRes}");
        var fd = (int)ctx[CpuRegister.Rax];
        if (fd <= 0) throw new InvalidOperationException($"Invalid NetSocket fd: {fd}");

        // 2. Configure via libkernel:setsockopt (SO_NBIO = 0x1200)
        ulong optvalAddr = 0x1000;
        ulong optlenAddr = 0x1020;
        Span<byte> valBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valBytes, 1);
        mem.TryWrite(optvalAddr, valBytes);

        ctx[CpuRegister.Rdi] = (ulong)fd;
        ctx[CpuRegister.Rsi] = 0xFFFF;
        ctx[CpuRegister.Rdx] = 0x1200;
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = 4;
        var setRes = KernelSocketCompatExports.Setsockopt(ctx);
        if (setRes != 0) throw new InvalidOperationException($"Kernel setsockopt failed with {setRes} on NetSocket fd {fd}");

        // 3. Query via libSceNet:sceNetGetsockopt
        Span<byte> lenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBytes, 4);
        mem.TryWrite(optlenAddr, lenBytes);

        ctx[CpuRegister.Rdi] = (ulong)fd;
        ctx[CpuRegister.Rsi] = 0xFFFF;
        ctx[CpuRegister.Rdx] = 0x1200;
        ctx[CpuRegister.Rcx] = optvalAddr;
        ctx[CpuRegister.R8] = optlenAddr;
        var getResSceNet = NetExports.NetGetsockopt(ctx);
        if (getResSceNet != 0) throw new InvalidOperationException($"sceNetGetsockopt failed with {getResSceNet}");

        mem.TryRead(optvalAddr, valBytes);
        var readVal = BinaryPrimitives.ReadInt32LittleEndian(valBytes);
        if (readVal != 1) throw new InvalidOperationException($"Expected SO_NBIO=1, got {readVal}");

        SocketRegistry.TryClose(fd);
        Console.WriteLine("  [PASS] 2. libSceNet socket configured via libkernel:setsockopt verified");
    }

    private static void TestCrossApiClose()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen4 | Generation.Gen5);

        // Kernel socket closed by sceNetSocketClose
        ctx[CpuRegister.Rdi] = 2;
        ctx[CpuRegister.Rsi] = 1;
        ctx[CpuRegister.Rdx] = 6;
        KernelSocketCompatExports.Socket(ctx);
        var fd1 = (int)ctx[CpuRegister.Rax];

        ctx[CpuRegister.Rdi] = (ulong)fd1;
        var closeRes1 = NetExports.NetSocketClose(ctx);
        if (closeRes1 != 0) throw new InvalidOperationException($"sceNetSocketClose failed to close kernel fd {fd1}");
        if (SocketRegistry.IsSocket(fd1)) throw new InvalidOperationException($"fd1 still alive after close");

        // NetSocket closed by TryCloseSocketFd
        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = 2;
        ctx[CpuRegister.Rdx] = 1;
        ctx[CpuRegister.Rcx] = 6;
        NetExports.NetSocket(ctx);
        var fd2 = (int)ctx[CpuRegister.Rax];

        var closeRes2 = KernelSocketCompatExports.TryCloseSocketFd(fd2);
        if (!closeRes2) throw new InvalidOperationException($"TryCloseSocketFd failed to close NetSocket fd {fd2}");
        if (SocketRegistry.IsSocket(fd2)) throw new InvalidOperationException($"fd2 still alive after close");

        Console.WriteLine("  [PASS] 3. Cross-API socket close operations verified");
    }

    private static void Test1000IterationCrossApiStress()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen4 | Generation.Gen5);

        for (int i = 0; i < 1000; i++)
        {
            ctx[CpuRegister.Rdi] = 2;
            ctx[CpuRegister.Rsi] = 1;
            ctx[CpuRegister.Rdx] = 6;
            KernelSocketCompatExports.Socket(ctx);
            var fd = (int)ctx[CpuRegister.Rax];

            ctx[CpuRegister.Rdi] = (ulong)fd;
            ctx[CpuRegister.Rsi] = 0xFFFF;
            ctx[CpuRegister.Rdx] = 0x1200;
            ctx[CpuRegister.Rcx] = 0x1000;
            ctx[CpuRegister.R8] = 4;
            NetExports.NetSetsockopt(ctx);

            NetExports.NetSocketClose(ctx);
        }

        Console.WriteLine("  [PASS] 4. 1,000 iteration cross-API socket lifecycle stress test passed cleanly");
    }
}
