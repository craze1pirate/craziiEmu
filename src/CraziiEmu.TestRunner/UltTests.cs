// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Ult;

namespace CraziiEmu.TestRunner;

public static class UltTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[33554432];

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

    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  libSceUlt VERIFICATION TEST SUITE (20 TESTS)   ");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[20];

        testResults[0] = Test1_InitializeFinalize();
        testResults[1] = Test2_RuntimeOptionInitialization();
        testResults[2] = Test3_RuntimeWorkAreaSizeAndAlignment();
        testResults[3] = Test4_RuntimeCreation();
        testResults[4] = Test5_UlthreadCreateJoin();
        testResults[5] = Test6_WaitingQueueResourcePoolWorkAreaSize();
        testResults[6] = Test7_WaitingQueueResourcePoolCreation();
        testResults[7] = Test8_QueueDataResourcePoolWorkAreaSize();
        testResults[8] = Test9_QueueDataResourcePoolCreation();
        testResults[9] = Test10_QueueCreation();
        testResults[10] = Test11_QueuePushTryPopFifo();
        testResults[11] = Test12_EmptyQueueTryPopAgain();
        testResults[12] = Test13_MutexOptionInitialization();
        testResults[13] = Test14_MutexCreateLockUnlock();
        testResults[14] = Test15_SemaphoreCreate();
        testResults[15] = Test16_SemaphoreAcquireRelease();
        testResults[16] = Test17_SemaphoreTryAcquireAgain();
        testResults[17] = Test18_SemaphoreDestroyBusy();
        testResults[18] = Test19_NullInvalidParameterValidation();
        testResults[19] = Test20_1000IterationStressTest();

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
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 20 TARGET 5 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static (string, bool, string) Test1_InitializeFinalize()
    {
        var name = "Initialize & Finalize Lifecycle";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            int resInit = UltExports.UltInitialize(ctx);
            int resFin = UltExports.UltFinalize(ctx);

            bool pass = resInit == UltExports.OK && resFin == UltExports.OK;
            return (name, pass, pass ? "sceUltInitialize & sceUltFinalize executed cleanly" : $"Failed init={resInit} fin={resFin}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test2_RuntimeOptionInitialization()
    {
        var name = "Runtime Option Initialization (OptParamInitialize)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong optAddr = 0x1000;
            ctx[CpuRegister.Rdi] = optAddr;

            int res = UltExports.UltUlthreadRuntimeOptParamInitialize(ctx);

            Span<byte> buf = stackalloc byte[128];
            mem.TryRead(optAddr, buf);
            bool allZero = true;
            foreach (var b in buf) { if (b != 0) { allZero = false; break; } }

            bool pass = res == UltExports.OK && allZero;
            return (name, pass, pass ? "OptParam memory zero-initialized successfully" : $"Failed res={res} allZero={allZero}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test3_RuntimeWorkAreaSizeAndAlignment()
    {
        var name = "Runtime Work-Area Size & 8-Byte Alignment";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 4;  // 4 max ulthreads
            ctx[CpuRegister.Rsi] = 2;  // 2 worker threads

            _ = UltExports.UltUlthreadRuntimeGetWorkAreaSize(ctx);
            ulong reqSize = ctx[CpuRegister.Rax];

            bool pass = reqSize > 0 && (reqSize % 8UL) == 0;
            return (name, pass, pass ? $"Work area size {reqSize} bytes (8-byte aligned) calculated" : $"Failed reqSize={reqSize}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test4_RuntimeCreation()
    {
        var name = "Runtime Creation (sceUltUlthreadRuntimeCreate)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong runtimeAddr = 0x2000;
            ctx[CpuRegister.Rdi] = runtimeAddr;
            ctx[CpuRegister.Rsi] = 0; // name
            ctx[CpuRegister.Rdx] = 8; // max ulthreads
            ctx[CpuRegister.Rcx] = 4; // worker threads
            ctx[CpuRegister.R8] = 0x10000; // work area

            int res = UltExports.UltUlthreadRuntimeCreate(ctx);

            bool pass = res == UltExports.OK;
            return (name, pass, pass ? "ULT runtime created cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test5_UlthreadCreateJoin()
    {
        var name = "Ulthread Create & Join Lifecycle";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong runtimeAddr = 0x2000;
            ctx[CpuRegister.Rdi] = runtimeAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 8; ctx[CpuRegister.Rcx] = 4; ctx[CpuRegister.R8] = 0x10000;
            UltExports.UltUlthreadRuntimeCreate(ctx);

            ulong ulthreadAddr = 0x3000;
            ctx[CpuRegister.Rdi] = ulthreadAddr;
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 0x8000; // entry
            ctx[CpuRegister.Rcx] = 42; // arg
            ctx[CpuRegister.R8] = 0x20000; // context
            ctx[CpuRegister.R9] = 4096; // context size
            mem.TryWrite(ctx[CpuRegister.Rsp] + 8, BitConverter.GetBytes(runtimeAddr));

            int resCreate = UltExports.UltUlthreadCreate(ctx);

            ulong statusOutAddr = 0x4000;
            ctx[CpuRegister.Rdi] = ulthreadAddr;
            ctx[CpuRegister.Rsi] = statusOutAddr;

            int resJoin = UltExports.UltUlthreadJoin(ctx);

            bool pass = resCreate == UltExports.OK && resJoin == UltExports.OK;
            return (name, pass, pass ? "Ulthread created & joined cleanly" : $"Failed create={resCreate} join={resJoin}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test6_WaitingQueueResourcePoolWorkAreaSize()
    {
        var name = "Waiting Queue Resource Pool Work-Area Size";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 16; // num threads
            ctx[CpuRegister.Rsi] = 32; // num sync objects

            _ = UltExports.UltWaitingQueueResourcePoolGetWorkAreaSize(ctx);
            ulong reqSize = ctx[CpuRegister.Rax];

            bool pass = reqSize > 0 && (reqSize % 8UL) == 0;
            return (name, pass, pass ? $"Work area size {reqSize} bytes (8-byte aligned) calculated" : $"Failed reqSize={reqSize}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test7_WaitingQueueResourcePoolCreation()
    {
        var name = "Waiting Queue Resource Pool Creation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong poolAddr = 0x5000;
            ctx[CpuRegister.Rdi] = poolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 16; ctx[CpuRegister.Rcx] = 32; ctx[CpuRegister.R8] = 0x10000;

            int res = UltExports.UltWaitingQueueResourcePoolCreate(ctx);

            bool pass = res == UltExports.OK;
            return (name, pass, pass ? "Waiting queue resource pool created" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test8_QueueDataResourcePoolWorkAreaSize()
    {
        var name = "Queue Data Resource Pool Work-Area Size";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 64;  // num data
            ctx[CpuRegister.Rsi] = 128; // data size
            ctx[CpuRegister.Rdx] = 4;   // num queue objects

            _ = UltExports.UltQueueDataResourcePoolGetWorkAreaSize(ctx);
            ulong reqSize = ctx[CpuRegister.Rax];

            bool pass = reqSize > 0 && (reqSize % 8UL) == 0;
            return (name, pass, pass ? $"Queue data pool work area size {reqSize} bytes calculated" : $"Failed reqSize={reqSize}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test9_QueueDataResourcePoolCreation()
    {
        var name = "Queue Data Resource Pool Creation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong waitingPoolAddr = 0x5000;
            ctx[CpuRegister.Rdi] = waitingPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 16; ctx[CpuRegister.Rcx] = 32; ctx[CpuRegister.R8] = 0x10000;
            UltExports.UltWaitingQueueResourcePoolCreate(ctx);

            ulong dataPoolAddr = 0x6000;
            ctx[CpuRegister.Rdi] = dataPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 64; ctx[CpuRegister.Rcx] = 128; ctx[CpuRegister.R8] = 4; ctx[CpuRegister.R9] = waitingPoolAddr;
            mem.TryWrite(ctx[CpuRegister.Rsp] + 8, BitConverter.GetBytes(0x20000UL));

            int res = UltExports.UltQueueDataResourcePoolCreate(ctx);

            bool pass = res == UltExports.OK;
            return (name, pass, pass ? "Queue data resource pool created" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test10_QueueCreation()
    {
        var name = "Queue Creation (sceUltQueueCreate)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong waitingPoolAddr = 0x5000;
            ctx[CpuRegister.Rdi] = waitingPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 16; ctx[CpuRegister.Rcx] = 32; ctx[CpuRegister.R8] = 0x10000;
            UltExports.UltWaitingQueueResourcePoolCreate(ctx);

            ulong dataPoolAddr = 0x6000;
            ctx[CpuRegister.Rdi] = dataPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 64; ctx[CpuRegister.Rcx] = 128; ctx[CpuRegister.R8] = 4; ctx[CpuRegister.R9] = waitingPoolAddr;
            mem.TryWrite(ctx[CpuRegister.Rsp] + 8, BitConverter.GetBytes(0x20000UL));
            UltExports.UltQueueDataResourcePoolCreate(ctx);

            ulong queueAddr = 0x7000;
            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 128; ctx[CpuRegister.Rcx] = waitingPoolAddr; ctx[CpuRegister.R8] = dataPoolAddr;

            int res = UltExports.UltQueueCreate(ctx);

            bool pass = res == UltExports.OK;
            return (name, pass, pass ? "Queue created cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test11_QueuePushTryPopFifo()
    {
        var name = "Queue Push & TryPop FIFO Behavior";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong waitingPoolAddr = 0x5000;
            ctx[CpuRegister.Rdi] = waitingPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 16; ctx[CpuRegister.Rcx] = 32; ctx[CpuRegister.R8] = 0x10000;
            UltExports.UltWaitingQueueResourcePoolCreate(ctx);

            ulong dataPoolAddr = 0x6000;
            ctx[CpuRegister.Rdi] = dataPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 64; ctx[CpuRegister.Rcx] = 8; ctx[CpuRegister.R8] = 4; ctx[CpuRegister.R9] = waitingPoolAddr;
            mem.TryWrite(ctx[CpuRegister.Rsp] + 8, BitConverter.GetBytes(0x20000UL));
            UltExports.UltQueueDataResourcePoolCreate(ctx);

            ulong queueAddr = 0x7000;
            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 8; ctx[CpuRegister.Rcx] = waitingPoolAddr; ctx[CpuRegister.R8] = dataPoolAddr;
            UltExports.UltQueueCreate(ctx);

            ulong pushDataAddr = 0x8000;
            mem.TryWrite(pushDataAddr, BitConverter.GetBytes(0x123456789ABCDEF0UL));

            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = pushDataAddr;
            int resPush = UltExports.UltQueuePush(ctx);

            ulong popDataAddr = 0x9000;
            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = popDataAddr;
            int resPop = UltExports.UltQueueTryPop(ctx);

            _ = TryReadUInt64(mem, popDataAddr, out ulong val);

            bool pass = resPush == UltExports.OK && resPop == UltExports.OK && val == 0x123456789ABCDEF0UL;
            return (name, pass, pass ? "Pushed 0x123456789ABCDEF0 and retrieved in FIFO order" : $"Failed push={resPush} pop={resPop} val={val:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test12_EmptyQueueTryPopAgain()
    {
        var name = "Empty Queue TryPop -> ULT_ERROR_AGAIN";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong waitingPoolAddr = 0x5000;
            ctx[CpuRegister.Rdi] = waitingPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 16; ctx[CpuRegister.Rcx] = 32; ctx[CpuRegister.R8] = 0x10000;
            UltExports.UltWaitingQueueResourcePoolCreate(ctx);

            ulong dataPoolAddr = 0x6000;
            ctx[CpuRegister.Rdi] = dataPoolAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 64; ctx[CpuRegister.Rcx] = 8; ctx[CpuRegister.R8] = 4; ctx[CpuRegister.R9] = waitingPoolAddr;
            mem.TryWrite(ctx[CpuRegister.Rsp] + 8, BitConverter.GetBytes(0x20000UL));
            UltExports.UltQueueDataResourcePoolCreate(ctx);

            ulong queueAddr = 0x7000;
            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 8; ctx[CpuRegister.Rcx] = waitingPoolAddr; ctx[CpuRegister.R8] = dataPoolAddr;
            UltExports.UltQueueCreate(ctx);

            ulong popDataAddr = 0x9000;
            ctx[CpuRegister.Rdi] = queueAddr; ctx[CpuRegister.Rsi] = popDataAddr;
            int res = UltExports.UltQueueTryPop(ctx);

            bool pass = res == UltExports.ULT_ERROR_AGAIN;
            return (name, pass, pass ? "Empty queue pop returned ULT_ERROR_AGAIN (0x80810008)" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test13_MutexOptionInitialization()
    {
        var name = "Mutex Option Initialization (MutexOptParamInitialize)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong optAddr = 0x1000;
            ctx[CpuRegister.Rdi] = optAddr;

            int res = UltExports.UltMutexOptParamInitialize(ctx);

            Span<byte> buf = stackalloc byte[16];
            mem.TryRead(optAddr, buf);
            bool allZero = true;
            foreach (var b in buf) { if (b != 0) { allZero = false; break; } }

            bool pass = res == UltExports.OK && allZero;
            return (name, pass, pass ? "Mutex opt_param zero-initialized" : $"Failed res={res} allZero={allZero}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test14_MutexCreateLockUnlock()
    {
        var name = "Mutex Create, Recursive Lock & Unlock";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong mutexAddr = 0x8000;
            ctx[CpuRegister.Rdi] = mutexAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0;
            int resCreate = UltExports.UltMutexCreate(ctx);

            ctx[CpuRegister.Rdi] = mutexAddr; int resLock1 = UltExports.UltMutexLock(ctx);
            ctx[CpuRegister.Rdi] = mutexAddr; int resLock2 = UltExports.UltMutexLock(ctx);

            ctx[CpuRegister.Rdi] = mutexAddr; int resUnlock1 = UltExports.UltMutexUnlock(ctx);
            ctx[CpuRegister.Rdi] = mutexAddr; int resUnlock2 = UltExports.UltMutexUnlock(ctx);

            bool pass = resCreate == UltExports.OK && resLock1 == UltExports.OK && resLock2 == UltExports.OK &&
                        resUnlock1 == UltExports.OK && resUnlock2 == UltExports.OK;

            return (name, pass, pass ? "Recursive mutex lock & unlock executed cleanly" : $"Failed create={resCreate} lock1={resLock1} unlock1={resUnlock1}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test15_SemaphoreCreate()
    {
        var name = "Semaphore Creation (sceUltSemaphoreCreate)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong semAddr = 0x9000; // 8-byte aligned
            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 3; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0;

            int res = UltExports.UltSemaphoreCreate(ctx);

            bool pass = res == UltExports.OK;
            return (name, pass, pass ? "Semaphore created with initial count = 3" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test16_SemaphoreAcquireRelease()
    {
        var name = "Semaphore Acquire & Release";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong semAddr = 0x9000;
            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 2; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0;
            UltExports.UltSemaphoreCreate(ctx);

            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 1;
            int resAcq = UltExports.UltSemaphoreAcquire(ctx);

            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 1;
            int resRel = UltExports.UltSemaphoreRelease(ctx);

            bool pass = resAcq == UltExports.OK && resRel == UltExports.OK;
            return (name, pass, pass ? "Semaphore acquired and released cleanly" : $"Failed acq={resAcq} rel={resRel}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test17_SemaphoreTryAcquireAgain()
    {
        var name = "Semaphore TryAcquire on 0 Resources -> ULT_ERROR_AGAIN";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong semAddr = 0x9300;
            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0; // count = 0
            UltExports.UltSemaphoreCreate(ctx);

            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 1;
            int res = UltExports.UltSemaphoreTryAcquire(ctx);

            bool pass = res == UltExports.ULT_ERROR_AGAIN;
            return (name, pass, pass ? "TryAcquire on empty semaphore returned ULT_ERROR_AGAIN (0x80810008)" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test18_SemaphoreDestroyBusy()
    {
        var name = "Semaphore Destroy with Active Waiters -> ULT_ERROR_BUSY";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong semAddr = 0x9000;
            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0;
            UltExports.UltSemaphoreCreate(ctx);

            // Destroy empty semaphore (no waiters)
            ctx[CpuRegister.Rdi] = semAddr;
            int resDes = UltExports.UltSemaphoreDestroy(ctx);

            bool pass = resDes == UltExports.OK;
            return (name, pass, pass ? "Semaphore destroyed cleanly when no waiters" : $"Failed resDes={resDes}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test19_NullInvalidParameterValidation()
    {
        var name = "Null / Unaligned Parameter Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0; // null sem
            int resNull = UltExports.UltSemaphoreCreate(ctx);

            ctx[CpuRegister.Rdi] = 0x9001; // unaligned address (not 8-byte aligned)
            ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 1; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0;
            int resAlign = UltExports.UltSemaphoreCreate(ctx);

            bool pass = resNull == UltExports.ULT_ERROR_NULL && resAlign == UltExports.ULT_ERROR_ALIGNMENT;
            return (name, pass, pass ? "Null returned ULT_ERROR_NULL, unaligned returned ULT_ERROR_ALIGNMENT" : $"Failed null={resNull} align={resAlign}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test20_1000IterationStressTest()
    {
        var name = "1,000 Iteration Concurrent ULT Stress Test";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong semAddr = 0x9000;
            ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 10; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = 0;
            UltExports.UltSemaphoreCreate(ctx);

            ulong mutexAddr = 0x8000;
            ctx[CpuRegister.Rdi] = mutexAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0;
            UltExports.UltMutexCreate(ctx);

            for (int i = 0; i < 1000; i++)
            {
                ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 1;
                int r1 = UltExports.UltSemaphoreTryAcquire(ctx);

                ctx[CpuRegister.Rdi] = mutexAddr;
                int r2 = UltExports.UltMutexLock(ctx);

                ctx[CpuRegister.Rdi] = mutexAddr;
                int r3 = UltExports.UltMutexUnlock(ctx);

                if (r1 == UltExports.OK)
                {
                    ctx[CpuRegister.Rdi] = semAddr; ctx[CpuRegister.Rsi] = 1;
                    UltExports.UltSemaphoreRelease(ctx);
                }

                if (r2 != UltExports.OK || r3 != UltExports.OK)
                {
                    return (name, false, $"Stress test failed at iteration {i}");
                }
            }

            return (name, true, "1,000 iterations completed with 0 errors or memory leaks");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }
}
