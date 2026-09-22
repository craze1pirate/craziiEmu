// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.Pad;

namespace CraziiEmu.TestRunner;

public static class UnityEngineCompatTests
{
    private sealed class SimpleTestMemory : ICpuMemory
    {
        private readonly byte[] _storage = new byte[0x20000];

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address + (ulong)destination.Length > (ulong)_storage.Length)
            {
                return false;
            }

            _storage.AsSpan((int)address, destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
        {
            if (address + (ulong)source.Length > (ulong)_storage.Length)
            {
                return false;
            }

            source.CopyTo(_storage.AsSpan((int)address, source.Length));
            return true;
        }

        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => true;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running UnityEngineCompatTests...");

        TestLibkernelUnityExports();
        TestKernelGettimezone();
        TestKernelConvertUtcToLocaltimeAndRoundtrip();
        TestPadOpenAcceptsSpecialPortType();

        Console.WriteLine("[TEST] UnityEngineCompatTests PASSED successfully.");
    }

    private static void TestPadOpenAcceptsSpecialPortType()
    {
        var mem = new SimpleTestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        PadExports.PadInit(ctx);
        ctx[CpuRegister.Rdi] = 0x10000000; // PrimaryUserId
        ctx[CpuRegister.Rsi] = 2; // port type special (used by Unity)
        ctx[CpuRegister.Rdx] = 0; // index
        ctx[CpuRegister.Rcx] = 0; // parameter
        var padRes = PadExports.PadOpen(ctx);
        Assert(padRes > 0, $"PadOpen with type=2 should succeed and return handle, got {padRes}");

        Console.WriteLine("  [PASS] TestPadOpenAcceptsSpecialPortType");
    }

    private static void TestLibkernelUnityExports()
    {
        var mem = new SimpleTestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // Verify invalid argument rejection on Install / Remove exception handler
        ctx[CpuRegister.Rdi] = 999; // Invalid signal
        ctx[CpuRegister.Rsi] = 0x1234;
        var resInstall = KernelExceptionCompatExports.InstallExceptionHandlerUnity(ctx);
        Assert(resInstall == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            "InstallExceptionHandlerUnity should reject invalid signal number");

        var resRemove = KernelExceptionCompatExports.RemoveExceptionHandlerUnity(ctx);
        Assert(resRemove == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            "RemoveExceptionHandlerUnity should reject invalid signal number");

        // Verify ModuleManager registers and finds qualified exports
        var moduleMgr = new ModuleManager();
        var exports = new List<ExportedFunction>
        {
            new("libKernel", "WkwEd3N7w0Y", "sceKernelInstallExceptionHandler", Generation.Gen5, KernelExceptionCompatExports.InstallExceptionHandler),
            new("libkernel_unity", "WkwEd3N7w0Y", "sceKernelInstallExceptionHandler", Generation.Gen5, KernelExceptionCompatExports.InstallExceptionHandlerUnity),
            new("libKernel", "Qhv5ARAoOEc", "sceKernelRemoveExceptionHandler", Generation.Gen5, KernelExceptionCompatExports.RemoveExceptionHandler),
            new("libkernel_unity", "Qhv5ARAoOEc", "sceKernelRemoveExceptionHandler", Generation.Gen5, KernelExceptionCompatExports.RemoveExceptionHandlerUnity),
            new("libKernel", "il03nluKfMk", "sceKernelRaiseException", Generation.Gen5, KernelExceptionCompatExports.RaiseException),
            new("libkernel_unity", "il03nluKfMk", "sceKernelRaiseException", Generation.Gen5, KernelExceptionCompatExports.RaiseExceptionUnity),
        };

        var registered = moduleMgr.RegisterExports(exports);
        Assert(registered >= 3, "Expected at least 3 distinct NIDs registered");

        // Verify NID lookup
        Assert(moduleMgr.TryGetExport("WkwEd3N7w0Y", out var expInstall), "Should resolve WkwEd3N7w0Y by NID");
        Assert(moduleMgr.TryGetExport("Qhv5ARAoOEc", out var expRemove), "Should resolve Qhv5ARAoOEc by NID");
        Assert(moduleMgr.TryGetExport("il03nluKfMk", out var expRaise), "Should resolve il03nluKfMk by NID");

        // Verify qualified lookup
        Assert(moduleMgr.TryGetExport("libkernel_unity:WkwEd3N7w0Y", out var expUnityInstall), "Should resolve libkernel_unity:WkwEd3N7w0Y");
        Assert(expUnityInstall.LibraryName == "libkernel_unity", "Export library name must match libkernel_unity");

        Console.WriteLine("  [PASS] TestLibkernelUnityExports");
    }

    private static void TestKernelGettimezone()
    {
        var mem = new SimpleTestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // Null pointer check
        ctx[CpuRegister.Rdi] = 0;
        var nullRes = KernelRuntimeCompatExports.KernelGettimezone(ctx);
        Assert(nullRes == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT,
            "KernelGettimezone with NULL pointer must return INVALID_ARGUMENT");

        // Valid buffer check
        ulong tzAddress = 0x1000;
        ctx[CpuRegister.Rdi] = tzAddress;
        var okRes = KernelRuntimeCompatExports.KernelGettimezone(ctx);
        Assert(okRes == (int)OrbisGen2Result.ORBIS_GEN2_OK, "KernelGettimezone should succeed");
        Assert(ctx[CpuRegister.Rax] == 0, "RAX must be 0 on success");

        Span<byte> minutesWestBytes = stackalloc byte[4];
        Span<byte> dstTimeBytes = stackalloc byte[4];
        Assert(mem.TryRead(tzAddress, minutesWestBytes), "Failed to read minutesWest");
        Assert(mem.TryRead(tzAddress + 4, dstTimeBytes), "Failed to read dstTime");

        int minutesWest = BinaryPrimitives.ReadInt32LittleEndian(minutesWestBytes);
        int dstTime = BinaryPrimitives.ReadInt32LittleEndian(dstTimeBytes);

        // minutesWest should be within [-720, 720] (UTC-12 to UTC+12)
        Assert(minutesWest >= -840 && minutesWest <= 840, $"minutesWest out of expected range: {minutesWest}");
        Assert(dstTime is 0 or 4, $"dstTime must be DST_NONE(0) or DST_MET(4), got {dstTime}");

        Console.WriteLine($"  [PASS] TestKernelGettimezone (minutesWest={minutesWest}, dstTime={dstTime})");
    }

    private static void TestKernelConvertUtcToLocaltimeAndRoundtrip()
    {
        var mem = new SimpleTestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Convert UTC to Localtime
        const long testUtcSeconds = 1700000000L; // Sun Nov 15 2023
        ulong localTimeAddr = 0x2000;
        ulong timesecAddr = 0x3000;
        ulong dstSecondsAddr = 0x4000;

        ctx[CpuRegister.Rdi] = unchecked((ulong)testUtcSeconds);
        ctx[CpuRegister.Rsi] = localTimeAddr;
        ctx[CpuRegister.Rdx] = timesecAddr;
        ctx[CpuRegister.Rcx] = dstSecondsAddr;

        var convertRes = KernelRuntimeCompatExports.KernelConvertUtcToLocaltime(ctx);
        Assert(convertRes == (int)OrbisGen2Result.ORBIS_GEN2_OK, "KernelConvertUtcToLocaltime should succeed");

        Span<byte> localSecBytes = stackalloc byte[8];
        Assert(mem.TryRead(localTimeAddr, localSecBytes), "Failed to read localTime");
        long localSeconds = BinaryPrimitives.ReadInt64LittleEndian(localSecBytes);

        Span<byte> timesecBytes = stackalloc byte[16];
        Assert(mem.TryRead(timesecAddr, timesecBytes), "Failed to read timesec");
        long timesecT = BinaryPrimitives.ReadInt64LittleEndian(timesecBytes.Slice(0, 8));
        uint timesecWest = BinaryPrimitives.ReadUInt32LittleEndian(timesecBytes.Slice(8, 4));
        uint timesecDst = BinaryPrimitives.ReadUInt32LittleEndian(timesecBytes.Slice(12, 4));

        Span<byte> dstSecBytes = stackalloc byte[8];
        Assert(mem.TryRead(dstSecondsAddr, dstSecBytes), "Failed to read dstSec");
        ulong dstSec = BinaryPrimitives.ReadUInt64LittleEndian(dstSecBytes);

        Assert(timesecT == testUtcSeconds, "timesec.t must equal original utc_time");
        Assert(dstSec == (ulong)timesecDst, "dst_sec pointer must match timesec.dst_sec");

        // Verify equation: local_time == utc_time - west + dst
        long expectedWest = -unchecked((int)timesecWest);
        long expectedLocal = testUtcSeconds - expectedWest + (long)timesecDst;
        Assert(localSeconds == expectedLocal, $"localSeconds {localSeconds} != expected {expectedLocal}");

        // 2. Roundtrip: Convert Localtime back to UTC
        ulong roundtripUtcAddr = 0x5000;
        ulong roundtripTzAddr = 0x6000;
        ulong roundtripDstAddr = 0x7000;

        ctx[CpuRegister.Rdi] = unchecked((ulong)localSeconds);
        ctx[CpuRegister.Rsi] = 0; // reserved
        ctx[CpuRegister.Rdx] = roundtripUtcAddr;
        ctx[CpuRegister.Rcx] = roundtripTzAddr;
        ctx[CpuRegister.R8] = roundtripDstAddr;

        var roundtripRes = KernelRuntimeCompatExports.KernelConvertLocaltimeToUtc(ctx);
        Assert(roundtripRes == (int)OrbisGen2Result.ORBIS_GEN2_OK, "KernelConvertLocaltimeToUtc should succeed");

        Span<byte> roundtripUtcBytes = stackalloc byte[8];
        Assert(mem.TryRead(roundtripUtcAddr, roundtripUtcBytes), "Failed to read roundtrip UTC");
        long roundtripUtcSeconds = BinaryPrimitives.ReadInt64LittleEndian(roundtripUtcBytes);

        Assert(roundtripUtcSeconds == testUtcSeconds,
            $"Roundtrip UTC mismatch: original={testUtcSeconds}, roundtrip={roundtripUtcSeconds}");

        Console.WriteLine($"  [PASS] TestKernelConvertUtcToLocaltimeAndRoundtrip (UTC={testUtcSeconds} <-> Local={localSeconds})");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
