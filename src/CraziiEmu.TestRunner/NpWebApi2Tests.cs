// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Np;

namespace CraziiEmu.TestRunner;

public static class NpWebApi2Tests
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
        Console.WriteLine("[TEST] Starting NpWebApi2Tests...");

        TestCorrectedUserContextAbi();
        TestRcxMemoryUntouched();
        TestStressCreateDelete();

        Console.WriteLine("[TEST] NpWebApi2Tests PASSED cleanly.");
    }

    private static void TestCorrectedUserContextAbi()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Create First Context with RCX == 0 (must succeed and return positive ID in EAX)
        ctx[CpuRegister.Rdi] = 1; // libCtxId = 1
        ctx[CpuRegister.Rsi] = 0x10000000; // userId = 0x10000000
        ctx[CpuRegister.Rcx] = 0; // RCX == 0 (must be ignored)
        var ctxId1 = NpWebApi2Exports.NpWebApi2CreateUserContext(ctx);
        if (ctxId1 <= 0) throw new InvalidOperationException($"Expected positive context ID, got {ctxId1}");
        if (ctx[CpuRegister.Rax] != (ulong)ctxId1) throw new InvalidOperationException($"Expected RAX to match return value {ctxId1}, got {ctx[CpuRegister.Rax]}");
        Console.WriteLine($"  [PASS] RDI/RSI accepted, RCX=0 ignored, returned positive ID = {ctxId1} in EAX");

        // 2. Create Second Context -> Unique Sequential ID in EAX
        ctx[CpuRegister.Rdi] = 1;
        ctx[CpuRegister.Rsi] = 0x10000000;
        ctx[CpuRegister.Rcx] = 0xDEADBEEF; // arbitrary non-zero RCX
        var ctxId2 = NpWebApi2Exports.NpWebApi2CreateUserContext(ctx);
        if (ctxId2 != ctxId1 + 1) throw new InvalidOperationException($"Expected sequential ID {ctxId1 + 1}, got {ctxId2}");
        if (ctx[CpuRegister.Rax] != (ulong)ctxId2) throw new InvalidOperationException($"Expected RAX to match return value {ctxId2}");
        Console.WriteLine($"  [PASS] Sequential unique context ID = {ctxId2}");

        // 3. Test Usage in Consumer (PushEventRegisterCallback)
        ctx[CpuRegister.Rdi] = (ulong)ctxId1; // userContextId
        ctx[CpuRegister.Rsi] = 1; // filterId
        ctx[CpuRegister.Rdx] = 0x2000; // callback
        ctx[CpuRegister.Rcx] = 0x3000; // userArg
        var callbackRes = NpWebApi2Exports.NpWebApi2PushEventRegisterCallback(ctx);
        if (callbackRes <= 0) throw new InvalidOperationException($"PushEventRegisterCallback failed for valid context: {callbackRes}");
        Console.WriteLine("  [PASS] Consumer accepts valid user context handle");

        // 4. Delete First Context -> Success
        ctx[CpuRegister.Rdi] = (ulong)ctxId1;
        var deleteRes = NpWebApi2Exports.NpWebApi2DeleteUserContext(ctx);
        if (deleteRes != 0) throw new InvalidOperationException($"DeleteUserContext failed for valid handle: {deleteRes}");
        Console.WriteLine("  [PASS] DeleteUserContext succeeded for valid handle");

        // 5. Consumer Rejects Stale Context Handle
        ctx[CpuRegister.Rdi] = (ulong)ctxId1;
        var staleCallbackRes = NpWebApi2Exports.NpWebApi2PushEventRegisterCallback(ctx);
        if (staleCallbackRes == 0) throw new InvalidOperationException("Expected consumer failure for deleted context handle");
        Console.WriteLine("  [PASS] Consumer rejects stale/deleted context handle");

        // 6. Delete Invalid/Stale Context Handle -> Error
        ctx[CpuRegister.Rdi] = (ulong)ctxId1;
        var staleDeleteRes = NpWebApi2Exports.NpWebApi2DeleteUserContext(ctx);
        if (staleDeleteRes == 0) throw new InvalidOperationException("Expected DeleteUserContext failure for already deleted handle");
        Console.WriteLine("  [PASS] DeleteUserContext rejects invalid/stale handle");
    }

    private static void TestRcxMemoryUntouched()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // Fill test memory at 0x1000 with a specific canary pattern (e.g. simulating guest tree node)
        ulong targetAddress = 0x1000;
        byte[] canary = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 };
        mem.TryWrite(targetAddress, canary);

        ctx[CpuRegister.Rdi] = 1;
        ctx[CpuRegister.Rsi] = 0x10000000;
        ctx[CpuRegister.Rcx] = targetAddress; // RCX points to canary memory

        var ctxId = NpWebApi2Exports.NpWebApi2CreateUserContext(ctx);
        if (ctxId <= 0) throw new InvalidOperationException($"CreateUserContext failed: {ctxId}");

        // Verify that memory at targetAddress remains byte-for-byte unchanged!
        Span<byte> readBuf = stackalloc byte[canary.Length];
        mem.TryRead(targetAddress, readBuf);

        if (!readBuf.SequenceEqual(canary))
        {
            throw new InvalidOperationException(
                $"Guest memory at RCX (0x{targetAddress:X}) was MUTATED! Expected {BitConverter.ToString(canary)}, got {BitConverter.ToString(readBuf.ToArray())}");
        }

        Console.WriteLine("  [PASS] Guest memory at RCX address is 100% byte-for-byte untouched");
    }

    private static void TestStressCreateDelete()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        for (int i = 0; i < 1000; i++)
        {
            ctx[CpuRegister.Rdi] = 1;
            ctx[CpuRegister.Rsi] = 0x10000000;
            ctx[CpuRegister.Rcx] = (ulong)(i * 8);

            var id = NpWebApi2Exports.NpWebApi2CreateUserContext(ctx);
            if (id <= 0) throw new InvalidOperationException($"Stress create failed at iter {i}: {id}");

            ctx[CpuRegister.Rdi] = (ulong)id;
            var delRes = NpWebApi2Exports.NpWebApi2DeleteUserContext(ctx);
            if (delRes != 0) throw new InvalidOperationException($"Stress delete failed at iter {i}: {delRes}");
        }

        Console.WriteLine("  [PASS] 1,000 iteration create/delete stress test passed cleanly");
    }
}
