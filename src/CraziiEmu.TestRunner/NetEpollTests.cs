// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.Generated;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Network;

namespace CraziiEmu.TestRunner;

public static class NetEpollTests
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
        Console.WriteLine("[TEST] Starting NetEpollTests...");

        TestNidResolution();
        TestEpollLifecycleAndControl();
        TestEpollWaitReady();

        Console.WriteLine("[TEST] NetEpollTests PASSED cleanly.");
    }

    private static void TestNidResolution()
    {
        var stubs = SysAbiExportRegistry.CreateExports(Generation.Gen4 | Generation.Gen5);
        bool foundCreate = false, foundControl = false, foundWait = false, foundDestroy = false;

        foreach (var stub in stubs)
        {
            if (stub.Nid == "SF47kB2MNTo" && stub.Name == "sceNetEpollCreate") foundCreate = true;
            if (stub.Nid == "ZVw46bsasAk" && stub.Name == "sceNetEpollControl") foundControl = true;
            if (stub.Nid == "drjIbDbA7UQ" && stub.Name == "sceNetEpollWait") foundWait = true;
            if (stub.Nid == "Inp1lfL+Jdw" && stub.Name == "sceNetEpollDestroy") foundDestroy = true;
        }

        if (!foundCreate) throw new InvalidOperationException("Missing sceNetEpollCreate (SF47kB2MNTo)");
        if (!foundControl) throw new InvalidOperationException("Missing sceNetEpollControl (ZVw46bsasAk)");
        if (!foundWait) throw new InvalidOperationException("Missing sceNetEpollWait (drjIbDbA7UQ)");
        if (!foundDestroy) throw new InvalidOperationException("Missing sceNetEpollDestroy (Inp1lfL+Jdw)");

        Console.WriteLine("  [PASS] All 4 sceNetEpoll* NIDs resolved correctly");
    }

    private static void TestEpollLifecycleAndControl()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. sceNetEpollCreate
        ctx[CpuRegister.Rdi] = 0; // name = null
        ctx[CpuRegister.Rsi] = 0; // flags = 0
        NetExports.NetEpollCreate(ctx);
        var eid = (int)ctx[CpuRegister.Rax];
        if (eid <= 0) throw new InvalidOperationException($"sceNetEpollCreate returned {eid}");

        // Allocate a dummy socket
        var fd = SocketRegistry.Allocate(2, 1, 6);

        // 2. EPOLL_CTL_ADD
        ulong eventPtr = 0x1000;
        Span<byte> ev = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(ev[0x00..], 1); // EPOLL_IN
        BinaryPrimitives.WriteUInt32LittleEndian(ev[0x04..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(ev[0x08..], (ulong)fd);
        BinaryPrimitives.WriteUInt64LittleEndian(ev[0x10..], 0x12345678UL);
        mem.TryWrite(eventPtr, ev);

        ctx[CpuRegister.Rdi] = (ulong)eid;
        ctx[CpuRegister.Rsi] = 1; // EPOLL_CTL_ADD
        ctx[CpuRegister.Rdx] = (ulong)fd;
        ctx[CpuRegister.Rcx] = eventPtr;
        NetExports.NetEpollControl(ctx);
        if ((int)ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("EPOLL_CTL_ADD failed");

        // Duplicate ADD should return error
        NetExports.NetEpollControl(ctx);
        if ((int)ctx[CpuRegister.Rax] == 0) throw new InvalidOperationException("Duplicate EPOLL_CTL_ADD should fail");

        // 3. EPOLL_CTL_MOD
        BinaryPrimitives.WriteUInt32LittleEndian(ev[0x00..], 3); // EPOLL_IN | EPOLL_OUT
        mem.TryWrite(eventPtr, ev);
        ctx[CpuRegister.Rsi] = 2; // EPOLL_CTL_MOD
        NetExports.NetEpollControl(ctx);
        if ((int)ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("EPOLL_CTL_MOD failed");

        // 4. EPOLL_CTL_DEL
        ctx[CpuRegister.Rsi] = 3; // EPOLL_CTL_DEL
        ctx[CpuRegister.Rcx] = 0;
        NetExports.NetEpollControl(ctx);
        if ((int)ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("EPOLL_CTL_DEL failed");

        // 5. sceNetEpollDestroy
        ctx[CpuRegister.Rdi] = (ulong)eid;
        NetExports.NetEpollDestroy(ctx);
        if ((int)ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("sceNetEpollDestroy failed");

        SocketRegistry.TryClose(fd);
        Console.WriteLine("  [PASS] Epoll create, add, mod, del, destroy lifecycle verified cleanly");
    }

    private static void TestEpollWaitReady()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // Create epoll
        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = 0;
        NetExports.NetEpollCreate(ctx);
        var eid = (int)ctx[CpuRegister.Rax];

        // Create a pair of loopback TCP sockets to test EpollWait
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        var clientSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        clientSock.Connect(new IPEndPoint(IPAddress.Loopback, port));
        var serverSock = listener.Accept();

        var clientFd = SocketRegistry.Allocate(2, 1, 6, clientSock);
        var serverFd = SocketRegistry.Allocate(2, 1, 6, serverSock);

        // Register serverFd for EPOLL_IN
        ulong eventPtr = 0x1000;
        Span<byte> ev = stackalloc byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(ev[0x00..], 1); // EPOLL_IN
        BinaryPrimitives.WriteUInt32LittleEndian(ev[0x04..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(ev[0x08..], (ulong)serverFd);
        BinaryPrimitives.WriteUInt64LittleEndian(ev[0x10..], 0xCAFEBABEUL);
        mem.TryWrite(eventPtr, ev);

        ctx[CpuRegister.Rdi] = (ulong)eid;
        ctx[CpuRegister.Rsi] = 1; // EPOLL_CTL_ADD
        ctx[CpuRegister.Rdx] = (ulong)serverFd;
        ctx[CpuRegister.Rcx] = eventPtr;
        NetExports.NetEpollControl(ctx);

        // Before sending data: EpollWait with 0 timeout should return 0 ready events
        ulong outEventsPtr = 0x2000;
        ctx[CpuRegister.Rdi] = (ulong)eid;
        ctx[CpuRegister.Rsi] = outEventsPtr;
        ctx[CpuRegister.Rdx] = 10; // maxevents
        ctx[CpuRegister.Rcx] = 0;  // timeout = 0 (poll)
        NetExports.NetEpollWait(ctx);
        if ((int)ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("EpollWait should return 0 before data sent");

        // Send 1 byte from client to server
        clientSock.Send(new byte[] { 0x42 });

        // Now EpollWait should detect readable socket
        ctx[CpuRegister.Rdi] = (ulong)eid;
        ctx[CpuRegister.Rsi] = outEventsPtr;
        ctx[CpuRegister.Rdx] = 10;
        ctx[CpuRegister.Rcx] = 100000; // 100ms
        NetExports.NetEpollWait(ctx);
        var readyCount = (int)ctx[CpuRegister.Rax];
        if (readyCount != 1) throw new InvalidOperationException($"EpollWait expected 1 ready event, got {readyCount}");

        // Verify out event
        Span<byte> outEv = stackalloc byte[24];
        mem.TryRead(outEventsPtr, outEv);
        var readyEvents = BinaryPrimitives.ReadUInt32LittleEndian(outEv[0x00..]);
        var outIdent = BinaryPrimitives.ReadUInt64LittleEndian(outEv[0x08..]);
        var outData = BinaryPrimitives.ReadUInt64LittleEndian(outEv[0x10..]);

        if ((readyEvents & 1) == 0) throw new InvalidOperationException("Ready events does not have EPOLL_IN");
        if (outIdent != (ulong)serverFd) throw new InvalidOperationException("Out ident does not match serverFd");
        if (outData != 0xCAFEBABEUL) throw new InvalidOperationException("Out data does not match userData");

        // Clean up
        SocketRegistry.TryClose(clientFd);
        SocketRegistry.TryClose(serverFd);
        ctx[CpuRegister.Rdi] = (ulong)eid;
        NetExports.NetEpollDestroy(ctx);

        Console.WriteLine("  [PASS] EpollWait detected ready socket with correct user payload and ident");
    }
}
