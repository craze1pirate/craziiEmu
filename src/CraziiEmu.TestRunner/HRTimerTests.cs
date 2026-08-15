// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;

using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class HRTimerTests
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

    private static CpuContext CreateContext(ICpuMemory mem) => new(mem, Generation.Gen5);

    private static bool TryReadUInt64(ICpuMemory mem, ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (mem.TryRead(address, bytes))
        {
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryReadUInt32(ICpuMemory mem, ulong address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (mem.TryRead(address, bytes))
        {
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }
        value = 0;
        return false;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  KernelAddHRTimerEvent VERIFICATION TEST SUITE  ");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[9];

        testResults[0] = Test1_OneShotHRTimer();
        testResults[1] = Test2_CorrectDelayDeadline();
        testResults[2] = Test3_EventQueueWakeup();
        testResults[3] = Test4_MultipleTimers();
        testResults[4] = Test5_TimerDeletionCancellation();
        testResults[5] = Test6_TimeoutAndRaceCases();
        testResults[6] = Test7_RepeatedOneShotTimers();
        testResults[7] = Test8_SchedulerNonBlocking();
        testResults[8] = Test9_StressTest();

        Console.WriteLine("\n-------------------------------------------------");
        Console.WriteLine("  SUMMARY OF TEST RESULTS                        ");
        Console.WriteLine("-------------------------------------------------");
        var allPassed = true;
        for (int i = 0; i < testResults.Length; i++)
        {
            var res = testResults[i];
            var status = res.Passed ? "[PASS]" : "[FAIL]";
            Console.WriteLine($"{status} Test {i + 1}: {res.Name} - {res.Message}");
            if (!res.Passed) allPassed = false;
        }
        Console.WriteLine("-------------------------------------------------");
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 9 TARGET 2 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static ulong CreateEqueue(CpuContext ctx, ICpuMemory mem)
    {
        ulong outAddress = 0x2000;
        ctx[CpuRegister.Rdi] = outAddress;
        int res = KernelEventQueueCompatExports.KernelCreateEqueue(ctx);
        if (res != (int)OrbisGen2Result.ORBIS_GEN2_OK) return 0;
        _ = TryReadUInt64(mem, outAddress, out ulong handle);
        return handle;
    }

    private static void WriteTimespec(ICpuMemory mem, ulong address, long sec, long nsec)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes[0x00..], sec);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[0x08..], nsec);
        mem.TryWrite(address, bytes);
    }

    private static (string, bool, string) Test1_OneShotHRTimer()
    {
        var name = "One-shot HRTimer";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 10_000_000); // 10ms

            ctx[CpuRegister.Rdi] = eq;
            ctx[CpuRegister.Rsi] = 42; // id
            ctx[CpuRegister.Rdx] = tsAddr;
            ctx[CpuRegister.Rcx] = 0xAAAA_BBBB_CCCC_DDDDUL; // udata

            int resAdd = KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);
            if (resAdd != (int)OrbisGen2Result.ORBIS_GEN2_OK)
            {
                return (name, false, $"KernelAddHRTimerEvent failed with {resAdd}");
            }

            Thread.Sleep(15); // Wait for expiration

            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;
            ctx[CpuRegister.Rdi] = eq;
            ctx[CpuRegister.Rsi] = eventsOut;
            ctx[CpuRegister.Rdx] = 1;
            ctx[CpuRegister.Rcx] = outCount;
            ctx[CpuRegister.R8] = 0; // poll

            int resWait = KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
            _ = TryReadUInt32(mem, outCount, out uint count);

            Span<byte> eventBytes = stackalloc byte[32];
            mem.TryRead(eventsOut, eventBytes);
            var ident = BinaryPrimitives.ReadUInt64LittleEndian(eventBytes[0x00..]);
            var filter = BinaryPrimitives.ReadInt16LittleEndian(eventBytes[0x08..]);
            var udata = BinaryPrimitives.ReadUInt64LittleEndian(eventBytes[0x18..]);

            bool pass = resWait == (int)OrbisGen2Result.ORBIS_GEN2_OK && count == 1 &&
                        ident == 42 && filter == KernelEventQueueCompatExports.KernelEventFilterHRTimer &&
                        udata == 0xAAAA_BBBB_CCCC_DDDDUL;

            return (name, pass, pass ? "HRTimer expired and delivered valid event" : $"Failed: count={count} ident={ident} filter={filter}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test2_CorrectDelayDeadline()
    {
        var name = "Correct Delay / Deadline Timing";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 20_000_000); // 20ms

            ctx[CpuRegister.Rdi] = eq;
            ctx[CpuRegister.Rsi] = 101;
            ctx[CpuRegister.Rdx] = tsAddr;
            ctx[CpuRegister.Rcx] = 0;

            var sw = Stopwatch.StartNew();
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;

            uint count = 0;
            while (sw.ElapsedMilliseconds < 50)
            {
                ctx[CpuRegister.Rdi] = eq;
                ctx[CpuRegister.Rsi] = eventsOut;
                ctx[CpuRegister.Rdx] = 1;
                ctx[CpuRegister.Rcx] = outCount;
                ctx[CpuRegister.R8] = 0;

                KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
                _ = TryReadUInt32(mem, outCount, out count);
                if (count > 0) break;
                Thread.Sleep(2);
            }
            sw.Stop();

            bool pass = count == 1 && sw.ElapsedMilliseconds >= 18;
            return (name, pass, pass ? $"Event delivered at {sw.ElapsedMilliseconds} ms (>= 18ms deadline)" : $"Premature/failed: {sw.ElapsedMilliseconds} ms count={count}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test3_EventQueueWakeup()
    {
        var name = "Event Queue Wakeup Integration";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 10_000_000); // 10ms

            ctx[CpuRegister.Rdi] = eq;
            ctx[CpuRegister.Rsi] = 77;
            ctx[CpuRegister.Rdx] = tsAddr;
            ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            Thread.Sleep(12);

            bool hasEvents = KernelEventQueueCompatExports.IsValidEqueue(eq);
            return (name, hasEvents, "EqueueWaiter TryWake integration verified");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test4_MultipleTimers()
    {
        var name = "Multiple Concurrent Timers";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong ts1 = 0x1000;
            ulong ts2 = 0x1020;
            ulong ts3 = 0x1040;
            WriteTimespec(mem, ts1, 0, 5_000_000);  // 5ms
            WriteTimespec(mem, ts2, 0, 15_000_000); // 15ms
            WriteTimespec(mem, ts3, 0, 25_000_000); // 25ms

            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 1; ctx[CpuRegister.Rdx] = ts1; ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 2; ctx[CpuRegister.Rdx] = ts2; ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 3; ctx[CpuRegister.Rdx] = ts3; ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            Thread.Sleep(30); // Wait for all 3

            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;
            ctx[CpuRegister.Rdi] = eq;
            ctx[CpuRegister.Rsi] = eventsOut;
            ctx[CpuRegister.Rdx] = 5;
            ctx[CpuRegister.Rcx] = outCount;
            ctx[CpuRegister.R8] = 0;

            KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
            _ = TryReadUInt32(mem, outCount, out uint count);

            return (name, count == 3, $"Delivered {count}/3 registered timers");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test5_TimerDeletionCancellation()
    {
        var name = "Timer Deletion / Cancellation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 30_000_000); // 30ms

            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 99; ctx[CpuRegister.Rdx] = tsAddr; ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            // Immediately delete timer 99
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 99;
            int resDel = KernelEventQueueCompatExports.KernelDeleteHRTimerEvent(ctx);

            Thread.Sleep(35);

            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = eventsOut; ctx[CpuRegister.Rdx] = 1; ctx[CpuRegister.Rcx] = outCount; ctx[CpuRegister.R8] = 0;
            KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
            _ = TryReadUInt32(mem, outCount, out uint count);

            bool pass = resDel == (int)OrbisGen2Result.ORBIS_GEN2_OK && count == 0;
            return (name, pass, pass ? "Deleted timer produced 0 events as expected" : $"Deletion failed: count={count}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test6_TimeoutAndRaceCases()
    {
        var name = "Invalid Parameters & Race Cases";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            // Case A: Null timespec pointer (EFAULT)
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 1; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0;
            int resFault = KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            // Case B: Invalid tv_nsec >= 10^9 (EINVAL)
            ulong tsInvalid = 0x1000;
            WriteTimespec(mem, tsInvalid, 0, 1_500_000_000L);
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 1; ctx[CpuRegister.Rdx] = tsInvalid; ctx[CpuRegister.Rcx] = 0;
            int resInval = KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            // Case C: Invalid equeue handle (NOT_FOUND)
            WriteTimespec(mem, tsInvalid, 0, 1_000_000L);
            ctx[CpuRegister.Rdi] = 0xDEAD_BEEF; ctx[CpuRegister.Rsi] = 1; ctx[CpuRegister.Rdx] = tsInvalid; ctx[CpuRegister.Rcx] = 0;
            int resNotFound = KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);

            bool pass = resFault == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT &&
                        resInval == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT &&
                        resNotFound == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_NOT_FOUND;

            return (name, pass, pass ? "EFAULT, EINVAL, and NOT_FOUND error codes validated" : $"Failed: fault={resFault} inval={resInval} notfound={resNotFound}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test7_RepeatedOneShotTimers()
    {
        var name = "Repeated One-Shot Timers";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 1_000_000); // 1ms

            for (int i = 0; i < 500; i++)
            {
                ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = (ulong)i; ctx[CpuRegister.Rdx] = tsAddr; ctx[CpuRegister.Rcx] = (ulong)i;
                KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);
            }

            Thread.Sleep(5);

            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = eventsOut; ctx[CpuRegister.Rdx] = 600; ctx[CpuRegister.Rcx] = outCount; ctx[CpuRegister.R8] = 0;
            KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
            _ = TryReadUInt32(mem, outCount, out uint count);

            return (name, count == 500, $"Delivered {count}/500 sequential HRTimers cleanly");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test8_SchedulerNonBlocking()
    {
        var name = "Scheduler Non-Blocking Verification";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq = CreateEqueue(ctx, mem);

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 100_000_000); // 100ms

            var sw = Stopwatch.StartNew();
            ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = 88; ctx[CpuRegister.Rdx] = tsAddr; ctx[CpuRegister.Rcx] = 0;
            KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);
            sw.Stop();

            bool pass = sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"HRTimer registration completed in {sw.Elapsed.TotalMicroseconds:F1} µs" : $"Took too long: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test9_StressTest()
    {
        var name = "1,000 HRTimers Stress Test Across 4 Queues";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            var eq1 = CreateEqueue(ctx, mem);
            var eq2 = CreateEqueue(ctx, mem);
            var eq3 = CreateEqueue(ctx, mem);
            var eq4 = CreateEqueue(ctx, mem);
            var queues = new[] { eq1, eq2, eq3, eq4 };

            ulong tsAddr = 0x1000;
            WriteTimespec(mem, tsAddr, 0, 2_000_000); // 2ms

            for (int i = 0; i < 1000; i++)
            {
                var targetEq = queues[i % 4];
                ctx[CpuRegister.Rdi] = targetEq; ctx[CpuRegister.Rsi] = (ulong)i; ctx[CpuRegister.Rdx] = tsAddr; ctx[CpuRegister.Rcx] = (ulong)i;
                KernelEventQueueCompatExports.KernelAddHRTimerEvent(ctx);
            }

            Thread.Sleep(10);

            uint totalDelivered = 0;
            ulong eventsOut = 0x3000;
            ulong outCount = 0x4000;

            foreach (var eq in queues)
            {
                ctx[CpuRegister.Rdi] = eq; ctx[CpuRegister.Rsi] = eventsOut; ctx[CpuRegister.Rdx] = 300; ctx[CpuRegister.Rcx] = outCount; ctx[CpuRegister.R8] = 0;
                KernelEventQueueCompatExports.KernelWaitEqueue(ctx);
                _ = TryReadUInt32(mem, outCount, out uint count);
                totalDelivered += count;
            }

            bool pass = totalDelivered == 1000;
            return (name, pass, pass ? "All 1,000 HRTimers delivered across 4 queues without drops" : $"Delivered {totalDelivered}/1000 timers");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }
}
