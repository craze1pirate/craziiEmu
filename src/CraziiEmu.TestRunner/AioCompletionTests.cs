// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.IO;
using System.Text;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class AioCompletionTests
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
        Console.WriteLine("[TEST] Starting AioCompletionTests...");

        TestAioCompletionWithSemaphoreSignal();
        TestAioCompletionWithZeroSemaphore();

        Console.WriteLine("[TEST] AioCompletionTests PASSED cleanly.");
    }

    private static void TestAioCompletionWithSemaphoreSignal()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Create a completion semaphore: name="AioSema", initialCount=0, maxCount=1
        ulong semaHandleAddr = 0x800;
        ulong nameAddr = 0x1000;
        mem.TryWrite(nameAddr, "AioSema\0"u8);
        ctx[CpuRegister.Rdi] = semaHandleAddr;
        ctx[CpuRegister.Rsi] = nameAddr;
        ctx[CpuRegister.Rdx] = 0; // attr = 0
        ctx[CpuRegister.Rcx] = 0; // initialCount = 0
        ctx[CpuRegister.R8] = 1;  // maxCount = 1
        ctx[CpuRegister.R9] = 0;  // optionAddress = 0

        int res = KernelSemaphoreCompatExports.KernelCreateSema(ctx);
        if (res != 0) throw new InvalidOperationException($"KernelCreateSema failed: {res}");

        Span<byte> semaHandleBytes = stackalloc byte[4];
        mem.TryRead(semaHandleAddr, semaHandleBytes);
        uint semaHandle = BitConverter.ToUInt32(semaHandleBytes);

        // 2. Prepare SceKernelAioRWRequest at 0x2000:
        // offset(8) nbyte(8) buf(8) resultPtr(8) fd(4) semaId(4)
        ulong reqAddr = 0x2000;
        ulong dataBufAddr = 0x3000;
        ulong resultPtrAddr = 0x4000;

        Span<byte> reqStruct = stackalloc byte[40];
        reqStruct.Clear();
        BitConverter.GetBytes((long)0).CopyTo(reqStruct[0..]);
        BitConverter.GetBytes((ulong)16).CopyTo(reqStruct[8..]);
        BitConverter.GetBytes(dataBufAddr).CopyTo(reqStruct[16..]);
        BitConverter.GetBytes(resultPtrAddr).CopyTo(reqStruct[24..]);
        BitConverter.GetBytes((int)1).CopyTo(reqStruct[32..]); // fd = 1 (dummy)
        BitConverter.GetBytes((int)semaHandle).CopyTo(reqStruct[36..]); // semaId

        mem.TryWrite(reqAddr, reqStruct);

        ctx[CpuRegister.Rdi] = reqAddr;
        ctx[CpuRegister.Rsi] = 1; // count = 1
        ctx[CpuRegister.Rdx] = 0;
        ctx[CpuRegister.Rcx] = 0;

        res = KernelMemoryCompatExports.KernelAioSubmitReadCommands(ctx);
        if (res != 0) throw new InvalidOperationException($"KernelAioSubmitReadCommands failed: {res}");

        // 3. Verify that the completion semaphore was signaled (count incremented from 0 to 1)
        ctx[CpuRegister.Rdi] = (ulong)semaHandle;
        ctx[CpuRegister.Rsi] = 1; // needCount = 1
        res = KernelSemaphoreCompatExports.KernelPollSema(ctx, semaHandle, 1);
        if (res != 0)
        {
            throw new InvalidOperationException($"Expected KernelPollSema to succeed on signaled AIO semaphore, got {res:X8}");
        }

        Console.WriteLine("  [PASS] AIO read with semaId != 0 signaled completion semaphore cleanly");
    }

    private static void TestAioCompletionWithZeroSemaphore()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ulong reqAddr = 0x2000;
        ulong dataBufAddr = 0x3000;
        ulong resultPtrAddr = 0x4000;

        Span<byte> reqStruct = stackalloc byte[40];
        reqStruct.Clear();
        BitConverter.GetBytes((long)0).CopyTo(reqStruct[0..]);
        BitConverter.GetBytes((ulong)16).CopyTo(reqStruct[8..]);
        BitConverter.GetBytes(dataBufAddr).CopyTo(reqStruct[16..]);
        BitConverter.GetBytes(resultPtrAddr).CopyTo(reqStruct[24..]);
        BitConverter.GetBytes((int)1).CopyTo(reqStruct[32..]);
        BitConverter.GetBytes((int)0).CopyTo(reqStruct[36..]); // semaId = 0

        mem.TryWrite(reqAddr, reqStruct);

        ctx[CpuRegister.Rdi] = reqAddr;
        ctx[CpuRegister.Rsi] = 1;

        int res = KernelMemoryCompatExports.KernelAioSubmitReadCommands(ctx);
        if (res != 0) throw new InvalidOperationException($"KernelAioSubmitReadCommands failed: {res}");

        Console.WriteLine("  [PASS] AIO read with semaId == 0 completed cleanly without signaling");
    }
}
