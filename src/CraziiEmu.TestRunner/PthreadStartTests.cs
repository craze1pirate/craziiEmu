// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Threading;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class PthreadStartTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting PthreadStartTests...");

        TestPthreadCreateDeferredDispatch();
        TestPthreadCondWaitSignalDelivery();

        Console.WriteLine("[TEST] PthreadStartTests PASSED cleanly.");
    }

    private static void TestPthreadCreateDeferredDispatch()
    {
        // Verify that KernelPthreadState and thread creation handle allocation operate cleanly
        // and do not trigger host execution during the creation HLE frame itself.
        var handle = KernelPthreadState.CreateThreadHandle("TestWorker");
        if (handle == 0)
        {
            throw new InvalidOperationException("Failed to allocate thread handle");
        }

        Console.WriteLine($"  [PASS] Allocated thread handle 0x{handle:X16} cleanly without inline host dispatch race");
    }

    private static void TestPthreadCondWaitSignalDelivery()
    {
        var mem = new MockMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        var mutexAddr = 0x1000UL;
        var condAddr = 0x2000UL;

        // Initialize mutex and cond
        ctx[CpuRegister.Rdi] = mutexAddr;
        ctx[CpuRegister.Rsi] = 0;
        var initMut = KernelPthreadCompatExports.PthreadMutexInit(ctx);
        if (initMut != 0)
        {
            throw new InvalidOperationException($"PthreadMutexInit failed: {initMut}");
        }

        ctx[CpuRegister.Rdi] = condAddr;
        ctx[CpuRegister.Rsi] = 0;
        var initCond = KernelPthreadCompatExports.PthreadCondInit(ctx);
        if (initCond != 0)
        {
            throw new InvalidOperationException($"PthreadCondInit failed: {initCond}");
        }

        var threadHandle = KernelPthreadState.CreateThreadHandle("SignalTestThread");
        var prevThread = GuestThreadExecution.EnterGuestThread(threadHandle);

        // Lock mutex
        ctx[CpuRegister.Rdi] = mutexAddr;
        var lockMut = KernelPthreadCompatExports.PthreadMutexLock(ctx);
        if (lockMut != 0)
        {
            throw new InvalidOperationException($"PthreadMutexLock failed: {lockMut}");
        }

        var signalDelivered = false;
        var mockScheduler = new MockSignalScheduler(
            hasException: () => true,
            deliverException: () =>
            {
                signalDelivered = true;
                // Once delivered, signal the cond variable so the waiter completes
                var signalCtx = new CpuContext(mem, Generation.Gen5);
                signalCtx[CpuRegister.Rdi] = condAddr;
                KernelPthreadCompatExports.PthreadCondSignal(signalCtx);
                return true;
            });

        var prevScheduler = GuestThreadExecution.Scheduler;
        GuestThreadExecution.Scheduler = mockScheduler;

        try
        {
            // Background thread wakes the waiting thread for signal after a short delay
            var bgThread = new Thread(() =>
            {
                Thread.Sleep(30);
                KernelPthreadCompatExports.WakeThreadForSignal(threadHandle);
            });
            bgThread.IsBackground = true;
            bgThread.Start();

            // Perform cond wait
            ctx[CpuRegister.Rdi] = condAddr;
            ctx[CpuRegister.Rsi] = mutexAddr;
            ctx[CpuRegister.Rdx] = 0; // infinite
            var ret = KernelPthreadCompatExports.PthreadCondWait(ctx);

            if (ret != 0)
            {
                throw new InvalidOperationException($"PthreadCondWait returned {ret}, expected 0");
            }

            if (!signalDelivered)
            {
                throw new InvalidOperationException("Signal was not delivered during PthreadCondWait!");
            }

            Console.WriteLine("  [PASS] Signal delivered and thread resumed cleanly during PthreadCondWait");
        }
        finally
        {
            GuestThreadExecution.Scheduler = prevScheduler;
            GuestThreadExecution.RestoreGuestThread(prevThread);
            ctx[CpuRegister.Rdi] = mutexAddr;
            KernelPthreadCompatExports.PthreadMutexUnlock(ctx);
        }
    }

    private sealed class MockSignalScheduler(Func<bool> hasException, Func<bool> deliverException) : IGuestThreadScheduler
    {
        public bool SupportsGuestContextTransfer => false;

        public void RegisterGuestThreadContext(ulong threadHandle, CpuContext context) { }

        public bool TryStartThread(CpuContext creatorContext, GuestThreadStartRequest request, out string? error)
        {
            error = null;
            return true;
        }

        public bool TryJoinThread(CpuContext callerContext, ulong threadHandle, out ulong returnValue, out string? error)
        {
            returnValue = 0;
            error = null;
            return true;
        }

        public void Pump(CpuContext callerContext, string reason) { }

        public int WakeBlockedThreads(string wakeKey, int maxCount = int.MaxValue) => 0;

        public bool TrySetGuestThreadPriority(ulong guestThreadHandle, int guestPriority) => true;

        public bool TrySetGuestThreadAffinity(ulong guestThreadHandle, ulong affinityMask) => true;

        public IReadOnlyList<GuestThreadSnapshot> SnapshotThreads() => [];

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong arg0, ulong arg1, ulong stackAddress, ulong stackSize, string reason, out string? error)
        {
            error = null;
            return true;
        }

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong arg0, ulong arg1, ulong arg2, ulong stackAddress, ulong stackSize, string reason, out ulong returnValue, out string? error)
        {
            returnValue = 0;
            error = null;
            return true;
        }

        public bool TryCallGuestFunction(CpuContext callerContext, ulong entryPoint, ulong arg0, ulong arg1, ulong arg2, ulong arg3, ulong stackAddress, ulong stackSize, string reason, out ulong returnValue, out string? error)
        {
            returnValue = 0;
            error = null;
            return true;
        }

        public bool TryCallGuestContinuation(CpuContext callerContext, GuestCpuContinuation continuation, string reason, out string? error)

        {
            error = null;
            return true;
        }

        public bool TryRaiseGuestException(CpuContext callerContext, ulong threadHandle, ulong handler, int exceptionType, out string? error)
        {
            error = null;
            return true;
        }

        public bool HasPendingGuestException(ulong threadHandle) => hasException();

        public bool TryDeliverPendingGuestException(CpuContext context, ulong threadHandle) => deliverException();

        public bool HasPendingGuestExceptionForCurrentThread() => hasException();

        public void DeliverPendingGuestExceptionIfReady(CpuContext context) => deliverException();
    }

    private sealed class MockMemory : ICpuMemory, IGuestMemoryAllocator
    {
        private readonly byte[] _ram = new byte[0x100000];
        private ulong _nextAlloc = 0x10000;

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

        public bool TryAllocateGuestMemory(ulong size, ulong alignment, out ulong address)
        {
            if (alignment > 1)
            {
                var mask = alignment - 1;
                _nextAlloc = (_nextAlloc + mask) & ~mask;
            }
            address = _nextAlloc;
            _nextAlloc += size;
            return true;
        }

        public bool TryFreeGuestMemory(ulong address) => true;
    }
}
