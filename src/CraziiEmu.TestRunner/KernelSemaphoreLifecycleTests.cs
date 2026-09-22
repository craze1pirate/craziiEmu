// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Threading;
using System.Threading.Tasks;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.VideoOut;

namespace CraziiEmu.TestRunner;

public static class KernelSemaphoreLifecycleTests
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
        Console.WriteLine("[TEST] Starting KernelSemaphoreLifecycleTests...");

        TestSignalSema_WakesMatchingWaiters();
        TestCancelSema_WakesWaitersWithCanceledError();
        TestDeleteSema_WakesWaitersWithDeletedError();
        TestCompleteFlip_DoesNotWakeUnrelatedSemaphores();
        TestWaitSema_ZeroTimeout_FailsImmediately();

        Console.WriteLine("[TEST] KernelSemaphoreLifecycleTests PASSED cleanly.");
    }

    private static uint CreateSemaphore(DummyMemory mem, CpuContext ctx, string name, int initialCount, int maxCount)
    {
        ulong semaHandleAddr = 0x800;
        ulong nameAddr = 0x1000;
        mem.TryWrite(nameAddr, System.Text.Encoding.UTF8.GetBytes(name + "\0"));
        ctx[CpuRegister.Rdi] = semaHandleAddr;
        ctx[CpuRegister.Rsi] = nameAddr;
        ctx[CpuRegister.Rdx] = 0; // attr = 0
        ctx[CpuRegister.Rcx] = unchecked((ulong)initialCount);
        ctx[CpuRegister.R8] = unchecked((ulong)maxCount);
        ctx[CpuRegister.R9] = 0;

        int res = KernelSemaphoreCompatExports.KernelCreateSema(ctx);
        if (res != 0) throw new InvalidOperationException($"KernelCreateSema failed: {res}");

        Span<byte> handleBytes = stackalloc byte[4];
        mem.TryRead(semaHandleAddr, handleBytes);
        return BitConverter.ToUInt32(handleBytes);
    }

    private static void TestSignalSema_WakesMatchingWaiters()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        var handle = CreateSemaphore(mem, ctx, "SigTestSema", 0, 10);

        int result1 = -1;
        int result2 = -1;
        var t1Started = new ManualResetEventSlim(false);
        var t2Started = new ManualResetEventSlim(false);

        var t1 = Task.Run(() =>
        {
            var threadMem = new DummyMemory();
            var threadCtx = new CpuContext(threadMem, Generation.Gen5);
            threadCtx[CpuRegister.Rdi] = handle;
            threadCtx[CpuRegister.Rsi] = 3; // need 3
            threadCtx[CpuRegister.Rdx] = 0; // infinite
            t1Started.Set();
            result1 = KernelSemaphoreCompatExports.KernelWaitSema(threadCtx);
        });

        var t2 = Task.Run(() =>
        {
            var threadMem = new DummyMemory();
            var threadCtx = new CpuContext(threadMem, Generation.Gen5);
            threadCtx[CpuRegister.Rdi] = handle;
            threadCtx[CpuRegister.Rsi] = 2; // need 2
            threadCtx[CpuRegister.Rdx] = 0; // infinite
            t2Started.Set();
            result2 = KernelSemaphoreCompatExports.KernelWaitSema(threadCtx);
        });

        t1Started.Wait(1000);
        t2Started.Wait(1000);
        Thread.Sleep(50);

        // Signal 2 tokens: only thread 2 (which needs 2 tokens) should wake; thread 1 needs 3 tokens
        int sigRes = KernelSemaphoreCompatExports.KernelSignalSema(ctx, handle, 2);
        if (sigRes != 0) throw new InvalidOperationException($"KernelSignalSema failed: {sigRes}");

        bool t2Finished = t2.Wait(1000);
        if (!t2Finished || result2 != 0)
        {
            throw new InvalidOperationException($"Thread 2 failed to wake with 0: finished={t2Finished}, result={result2}");
        }

        // Thread 1 must still be waiting
        if (t1.IsCompleted)
        {
            throw new InvalidOperationException("Thread 1 should not have woken up when only 2 tokens were signaled!");
        }

        // Now signal 3 tokens for thread 1
        sigRes = KernelSemaphoreCompatExports.KernelSignalSema(ctx, handle, 3);
        if (sigRes != 0) throw new InvalidOperationException($"KernelSignalSema failed: {sigRes}");

        bool t1Finished = t1.Wait(1000);
        if (!t1Finished || result1 != 0)
        {
            throw new InvalidOperationException($"Thread 1 failed to wake with 0: finished={t1Finished}, result={result1}");
        }

        Console.WriteLine("  [PASS] SignalSema wakes matching waiters and decrements count accurately");
    }

    private static void TestCancelSema_WakesWaitersWithCanceledError()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        var handle = CreateSemaphore(mem, ctx, "CancelTestSema", 0, 10);

        int waitResult = 0;
        var waiterStarted = new ManualResetEventSlim(false);

        var waitTask = Task.Run(() =>
        {
            var threadMem = new DummyMemory();
            var threadCtx = new CpuContext(threadMem, Generation.Gen5);
            threadCtx[CpuRegister.Rdi] = handle;
            threadCtx[CpuRegister.Rsi] = 1;
            threadCtx[CpuRegister.Rdx] = 0;
            waiterStarted.Set();
            waitResult = KernelSemaphoreCompatExports.KernelWaitSema(threadCtx);
        });

        waiterStarted.Wait(1000);
        Thread.Sleep(50);

        ulong numWaitersAddr = 0x3000;
        int cancelRes = KernelSemaphoreCompatExports.KernelCancelSema(ctx, handle, setCount: 7, waitingThreadsAddress: numWaitersAddr);
        if (cancelRes != 0) throw new InvalidOperationException($"KernelCancelSema failed: {cancelRes}");

        Span<byte> numWaitersBytes = stackalloc byte[4];
        mem.TryRead(numWaitersAddr, numWaitersBytes);
        uint reportedWaiters = BitConverter.ToUInt32(numWaitersBytes);
        if (reportedWaiters != 1)
        {
            throw new InvalidOperationException($"KernelCancelSema reported {reportedWaiters} waiters, expected 1");
        }

        bool waitFinished = waitTask.Wait(1000);
        if (!waitFinished)
        {
            throw new InvalidOperationException("Waiter thread failed to wake after CancelSema");
        }

        const int expectedCanceled = unchecked((int)0x80020055); // ORBIS_GEN2_ERROR_CANCELED
        if (waitResult != expectedCanceled)
        {
            throw new InvalidOperationException($"Waiter result was 0x{waitResult:X8}, expected 0x{expectedCanceled:X8} (ECANCELED)");
        }

        // Verify semaphore count is now 7 and poll for 7 succeeds
        int pollRes = KernelSemaphoreCompatExports.KernelPollSema(ctx, handle, 7);
        if (pollRes != 0)
        {
            throw new InvalidOperationException($"KernelPollSema for setCount=7 failed with {pollRes}");
        }

        Console.WriteLine("  [PASS] CancelSema wakes waiters with ORBIS_GEN2_ERROR_CANCELED and preserves setCount");
    }

    private static void TestDeleteSema_WakesWaitersWithDeletedError()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        var handle = CreateSemaphore(mem, ctx, "DeleteTestSema", 0, 10);

        int waitResult = 0;
        var waiterStarted = new ManualResetEventSlim(false);

        var waitTask = Task.Run(() =>
        {
            var threadMem = new DummyMemory();
            var threadCtx = new CpuContext(threadMem, Generation.Gen5);
            threadCtx[CpuRegister.Rdi] = handle;
            threadCtx[CpuRegister.Rsi] = 1;
            threadCtx[CpuRegister.Rdx] = 0;
            waiterStarted.Set();
            waitResult = KernelSemaphoreCompatExports.KernelWaitSema(threadCtx);
        });

        waiterStarted.Wait(1000);
        Thread.Sleep(50);

        ctx[CpuRegister.Rdi] = handle;
        int deleteRes = KernelSemaphoreCompatExports.KernelDeleteSema(ctx);
        if (deleteRes != 0) throw new InvalidOperationException($"KernelDeleteSema failed: {deleteRes}");

        bool waitFinished = waitTask.Wait(1000);
        if (!waitFinished)
        {
            throw new InvalidOperationException("Waiter thread failed to wake after DeleteSema");
        }

        const int expectedDeleted = unchecked((int)0x8002000D); // ORBIS_GEN2_ERROR_DELETED
        if (waitResult != expectedDeleted)
        {
            throw new InvalidOperationException($"Waiter result was 0x{waitResult:X8}, expected 0x{expectedDeleted:X8} (EDELETED)");
        }

        Console.WriteLine("  [PASS] DeleteSema wakes waiters with ORBIS_GEN2_ERROR_DELETED");
    }

    private static void TestCompleteFlip_DoesNotWakeUnrelatedSemaphores()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        var handle = CreateSemaphore(mem, ctx, "FlipSemaGuard", 0, 10);

        int waitResult = 0;
        var waiterStarted = new ManualResetEventSlim(false);

        var waitTask = Task.Run(() =>
        {
            var threadMem = new DummyMemory();
            var threadCtx = new CpuContext(threadMem, Generation.Gen5);
            threadCtx[CpuRegister.Rdi] = handle;
            threadCtx[CpuRegister.Rsi] = 1;
            // Timeout 150ms (150,000 microseconds) at 0x2000
            ulong timeoutAddr = 0x2000;
            Span<byte> timeoutBytes = stackalloc byte[4];
            BitConverter.GetBytes(150_000u).CopyTo(timeoutBytes);
            threadMem.TryWrite(timeoutAddr, timeoutBytes);
            threadCtx[CpuRegister.Rdx] = timeoutAddr;

            waiterStarted.Set();
            waitResult = KernelSemaphoreCompatExports.KernelWaitSema(threadCtx);
        });

        waiterStarted.Wait(1000);
        Thread.Sleep(30);

        // Call CompleteFlip: must NOT wake or signal the semaphore!
        VideoOutExports.CompleteFlip(0, 999);

        // Wait for thread to complete (via its 150ms timeout)
        bool waitFinished = waitTask.Wait(1000);
        if (!waitFinished)
        {
            throw new InvalidOperationException("Waiter thread did not complete");
        }

        const int expectedTimedOut = unchecked((int)0x8002003C); // ORBIS_GEN2_ERROR_TIMED_OUT
        if (waitResult != expectedTimedOut)
        {
            throw new InvalidOperationException(
                $"Waiter was falsely signaled! Result was 0x{waitResult:X8}, expected 0x{expectedTimedOut:X8} (TIMED_OUT)");
        }

        Console.WriteLine("  [PASS] CompleteFlip does not wake or corrupt unrelated semaphores");
    }

    private static void TestWaitSema_ZeroTimeout_FailsImmediately()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        var handle = CreateSemaphore(mem, ctx, "ZeroTimeoutSema", 0, 10);

        ulong timeoutAddr = 0x2000;
        Span<byte> timeoutBytes = stackalloc byte[4];
        BitConverter.GetBytes(0u).CopyTo(timeoutBytes); // timeout = 0
        mem.TryWrite(timeoutAddr, timeoutBytes);

        ctx[CpuRegister.Rdi] = handle;
        ctx[CpuRegister.Rsi] = 1;
        ctx[CpuRegister.Rdx] = timeoutAddr;

        int res = KernelSemaphoreCompatExports.KernelWaitSema(ctx);
        const int expectedTimedOut = unchecked((int)0x8002003C);
        if (res != expectedTimedOut)
        {
            throw new InvalidOperationException($"Expected immediate TIMED_OUT (0x{expectedTimedOut:X8}), got 0x{res:X8}");
        }

        Console.WriteLine("  [PASS] WaitSema with timeout=0 returns immediately without blocking");
    }
}
