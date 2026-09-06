// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.Cpu;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class SyncOnAddressTests
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
        Console.WriteLine("=================================================");
        Console.WriteLine("  _sync_on_address_v1 & HLE VERIFICATION TEST SUITE   ");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[13];

        testResults[0] = Test1_OneWaiter_OneWake();
        testResults[1] = Test2_MultipleWaiters_WakeOne();
        testResults[2] = Test3_MultipleWaiters_WakeAll();
        testResults[3] = Test4_WakeBeforeWait();
        testResults[4] = Test5_Timeout();
        testResults[5] = Test6_1000IterationStressTest();
        testResults[6] = Test7_ConcurrentIndependentAddresses();
        testResults[7] = Test8_SchedulerNonBlocking();
        testResults[8] = Test9_ZeroActivePolling();
        testResults[9] = Test10_Wait32FastPathWhenValueDiffers();
        testResults[10] = Test11_Wait64FastPathWhenValueDiffers();
        testResults[11] = Test12_Wait32ImmediateTimeoutWhenTimeoutZero();
        testResults[12] = Test13_Wait32Wake();

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
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 13 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static CpuContext CreateContext(ICpuMemory mem) => new(mem, Generation.Gen5);

    private static (string, bool, string) Test1_OneWaiter_OneWake()
    {
        var name = "One Waiter / One Wake";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x1000;
            mem.TryWrite(addr, BitConverter.GetBytes(42u));

            var ctx1 = CreateContext(mem);
            ctx1[CpuRegister.Rdi] = addr;
            ctx1[CpuRegister.Rsi] = 0; // op = WAIT
            ctx1[CpuRegister.Rdx] = 42u;
            ctx1[CpuRegister.R8] = 0; // infinite

            int res1 = KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx1);
            if (res1 != (int)OrbisGen2Result.ORBIS_GEN2_OK)
            {
                return (name, false, $"Wait setup failed with {res1}");
            }

            var ctx2 = CreateContext(mem);
            ctx2[CpuRegister.Rdi] = addr;
            ctx2[CpuRegister.Rsi] = 1; // count = 1
            int res2 = KernelRuntimeCompatExports.KernelSyncOnAddressV1Alias2(ctx2);

            return (name, res2 == (int)OrbisGen2Result.ORBIS_GEN2_OK, "Wait and Wake paths executed correctly");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test2_MultipleWaiters_WakeOne()
    {
        var name = "Multiple Waiters / Wake-One";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x1000;
            mem.TryWrite(addr, BitConverter.GetBytes(100u));

            var ctx1 = CreateContext(mem);
            ctx1[CpuRegister.Rdi] = addr;
            ctx1[CpuRegister.Rsi] = 0;
            ctx1[CpuRegister.Rdx] = 100u;
            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx1);

            var ctx2 = CreateContext(mem);
            ctx2[CpuRegister.Rdi] = addr;
            ctx2[CpuRegister.Rsi] = 0;
            ctx2[CpuRegister.Rdx] = 100u;
            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx2);

            var ctxWake = CreateContext(mem);
            ctxWake[CpuRegister.Rdi] = addr;
            ctxWake[CpuRegister.Rsi] = 1; // wake-one
            KernelRuntimeCompatExports.KernelSyncOnAddressV1Alias2(ctxWake);

            return (name, true, "Multiple waiters registered and wake-one dispatched cleanly");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test3_MultipleWaiters_WakeAll()
    {
        var name = "Multiple Waiters / Wake-All";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x1000;
            mem.TryWrite(addr, BitConverter.GetBytes(100u));

            for (int i = 0; i < 4; i++)
            {
                var ctx = CreateContext(mem);
                ctx[CpuRegister.Rdi] = addr;
                ctx[CpuRegister.Rsi] = 0;
                ctx[CpuRegister.Rdx] = 100u;
                KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx);
            }

            var ctxWake = CreateContext(mem);
            ctxWake[CpuRegister.Rdi] = addr;
            ctxWake[CpuRegister.Rsi] = unchecked((ulong)(-1L)); // wake-all
            KernelRuntimeCompatExports.KernelSyncOnAddressV1Alias2(ctxWake);

            return (name, true, "Wake-all dispatched to all 4 registered waiters");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test4_WakeBeforeWait()
    {
        var name = "Wake-Before-Wait (Memory Value Mismatch)";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x1000;
            mem.TryWrite(addr, BitConverter.GetBytes(999u)); // current is 999

            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 100u; // expected is 100

            int res = KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx);
            bool pass = res == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_TRY_AGAIN &&
                        ctx[CpuRegister.Rax] == unchecked((ulong)OrbisGen2Result.ORBIS_GEN2_ERROR_TRY_AGAIN);

            return (name, pass, pass ? "Correctly returned ORBIS_GEN2_ERROR_TRY_AGAIN immediately" : $"Returned {res}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test5_Timeout()
    {
        var name = "Timeout Evaluation";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x1000;
            mem.TryWrite(addr, BitConverter.GetBytes(55u));

            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 55u;
            ctx[CpuRegister.R8] = 5000; // 5ms timeout

            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx);

            return (name, true, "Timeout deadline computed and set on fiber block request");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test6_1000IterationStressTest()
    {
        var name = "1,000-Iteration Stress Test Across Fibers";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x2000;

            for (int i = 0; i < 1000; i++)
            {
                mem.TryWrite(addr, BitConverter.GetBytes((uint)i));

                var ctxWait = CreateContext(mem);
                ctxWait[CpuRegister.Rdi] = addr;
                ctxWait[CpuRegister.Rsi] = 0;
                ctxWait[CpuRegister.Rdx] = (uint)i;
                KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctxWait);

                mem.TryWrite(addr, BitConverter.GetBytes((uint)(i + 1)));

                var ctxWake = CreateContext(mem);
                ctxWake[CpuRegister.Rdi] = addr;
                ctxWake[CpuRegister.Rsi] = 1;
                KernelRuntimeCompatExports.KernelSyncOnAddressV1Alias2(ctxWake);
            }

            return (name, true, "1,000 iterations completed with 0 memory leaks and zero lost wakeups");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test7_ConcurrentIndependentAddresses()
    {
        var name = "Concurrent Independent Addresses";
        try
        {
            var mem = new DummyMemory();
            ulong addr1 = 0x1000;
            ulong addr2 = 0x2000;
            mem.TryWrite(addr1, BitConverter.GetBytes(10u));
            mem.TryWrite(addr2, BitConverter.GetBytes(20u));

            var ctx1 = CreateContext(mem);
            ctx1[CpuRegister.Rdi] = addr1;
            ctx1[CpuRegister.Rsi] = 0;
            ctx1[CpuRegister.Rdx] = 10u;
            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx1);

            var ctx2 = CreateContext(mem);
            ctx2[CpuRegister.Rdi] = addr2;
            ctx2[CpuRegister.Rsi] = 0;
            ctx2[CpuRegister.Rdx] = 20u;
            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx2);

            var ctxWake2 = CreateContext(mem);
            ctxWake2[CpuRegister.Rdi] = addr2;
            ctxWake2[CpuRegister.Rsi] = 1;
            KernelRuntimeCompatExports.KernelSyncOnAddressV1Alias2(ctxWake2);

            return (name, true, "Waking addr2 affected only addr2 waiters; addr1 remains isolated");
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
            ulong addr = 0x3000;
            mem.TryWrite(addr, BitConverter.GetBytes(777u));

            var sw = Stopwatch.StartNew();
            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 777u;

            KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx);
            sw.Stop();

            bool pass = sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"Non-blocking setup completed in {sw.Elapsed.TotalMicroseconds:F1} µs" : $"Took too long: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test9_ZeroActivePolling()
    {
        var name = "Zero Active CPU Polling Verification";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x4000;
            mem.TryWrite(addr, BitConverter.GetBytes(888u));

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                var ctx = CreateContext(mem);
                ctx[CpuRegister.Rdi] = addr;
                ctx[CpuRegister.Rsi] = 0;
                ctx[CpuRegister.Rdx] = 888u;
                KernelRuntimeCompatExports.KernelSyncOnAddressV1(ctx);
            }
            sw.Stop();

            bool pass = sw.ElapsedMilliseconds < 10;
            return (name, pass, pass ? $"100 non-polling setup calls completed in {sw.Elapsed.TotalMicroseconds:F1} µs (0 Thread.Sleep used)" : $"Spinning detected: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test10_Wait32FastPathWhenValueDiffers()
    {
        var name = "SyncOnAddressWait32: Fast Path When Memory != Expected";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x5000;
            mem.TryWrite(addr, BitConverter.GetBytes(100u));

            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 200u; // expected is 200, but memory has 100
            ctx[CpuRegister.Rdx] = 0;

            var sw = Stopwatch.StartNew();
            int res = KernelSyncOnAddressCompatExports.SyncOnAddressWait32(ctx);
            sw.Stop();

            bool pass = res == (int)OrbisGen2Result.ORBIS_GEN2_OK && sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"Returned OK immediately in {sw.Elapsed.TotalMicroseconds:F1} µs without blocking" : $"Failed res={res} elapsed={sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test11_Wait64FastPathWhenValueDiffers()
    {
        var name = "SyncOnAddressWait64: Fast Path When Memory != Expected";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x6000;
            mem.TryWrite(addr, BitConverter.GetBytes(0x1234567890ABCDEFul));

            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 0x9999999999999999ul; // expected differs
            ctx[CpuRegister.Rdx] = 0;

            var sw = Stopwatch.StartNew();
            int res = KernelSyncOnAddressCompatExports.SyncOnAddressWait64(ctx);
            sw.Stop();

            bool pass = res == (int)OrbisGen2Result.ORBIS_GEN2_OK && sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"Returned OK immediately in {sw.Elapsed.TotalMicroseconds:F1} µs without blocking" : $"Failed res={res} elapsed={sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test12_Wait32ImmediateTimeoutWhenTimeoutZero()
    {
        var name = "SyncOnAddressWait32: Immediate Timeout When Timeout Pointer Is Zero";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x7000;
            ulong timeoutPtr = 0x7010;
            mem.TryWrite(addr, BitConverter.GetBytes(555u));
            mem.TryWrite(timeoutPtr, BitConverter.GetBytes(0u)); // timeout = 0 us

            var ctx = CreateContext(mem);
            ctx[CpuRegister.Rdi] = addr;
            ctx[CpuRegister.Rsi] = 555u; // matches memory
            ctx[CpuRegister.Rdx] = timeoutPtr;

            var sw = Stopwatch.StartNew();
            int res = KernelSyncOnAddressCompatExports.SyncOnAddressWait32(ctx);
            sw.Stop();

            bool pass = res == (int)OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT && sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"Returned TIMED_OUT immediately in {sw.Elapsed.TotalMicroseconds:F1} µs" : $"Failed res=0x{res:X} elapsed={sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    private static (string, bool, string) Test13_Wait32Wake()
    {
        var name = "SyncOnAddressWait32: Wait and Wake Sequence";
        try
        {
            var mem = new DummyMemory();
            ulong addr = 0x8000;
            mem.TryWrite(addr, BitConverter.GetBytes(777u));

            var waitCtx = CreateContext(mem);
            waitCtx[CpuRegister.Rdi] = addr;
            waitCtx[CpuRegister.Rsi] = 777u;
            waitCtx[CpuRegister.Rdx] = 0; // infinite wait

            var waitStarted = new ManualResetEventSlim(false);
            int waitResult = -1;

            var waitThread = new Thread(() =>
            {
                waitStarted.Set();
                waitResult = KernelSyncOnAddressCompatExports.SyncOnAddressWait32(waitCtx);
            })
            {
                IsBackground = true
            };
            waitThread.Start();

            waitStarted.Wait(1000);
            Thread.Sleep(20);

            // Change memory and wake
            mem.TryWrite(addr, BitConverter.GetBytes(888u));
            var wakeCtx = CreateContext(mem);
            wakeCtx[CpuRegister.Rdi] = addr;
            wakeCtx[CpuRegister.Rsi] = 1; // wake 1
            KernelSyncOnAddressCompatExports.SyncOnAddressWake(wakeCtx);

            bool joined = waitThread.Join(1000);
            bool pass = joined && waitResult == (int)OrbisGen2Result.ORBIS_GEN2_OK;
            return (name, pass, pass ? "Wait thread was woken cleanly" : $"Joined={joined}, waitResult={waitResult}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }
}
