// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Ult;

public static class UltExports
{
    public const int OK = 0;

    public const int ULT_ERROR_NULL      = unchecked((int)0x80810001);
    public const int ULT_ERROR_ALIGNMENT = unchecked((int)0x80810002);
    public const int ULT_ERROR_RANGE     = unchecked((int)0x80810003);
    public const int ULT_ERROR_INVALID   = unchecked((int)0x80810004);
    public const int ULT_ERROR_STATE     = unchecked((int)0x80810006);
    public const int ULT_ERROR_BUSY      = unchecked((int)0x80810007);
    public const int ULT_ERROR_AGAIN     = unchecked((int)0x80810008);

    private static readonly object _ultGlobalGate = new();

    private static readonly ConcurrentDictionary<ulong, UltRuntimeState> _runtimes = new();
    private static readonly ConcurrentDictionary<ulong, UltThreadState> _ulthreads = new();
    private static readonly ConcurrentDictionary<ulong, UltResourcePoolState> _resourcePools = new();
    private static readonly ConcurrentDictionary<ulong, UltQueueDataPoolState> _queueDataPools = new();
    private static readonly ConcurrentDictionary<ulong, UltQueueState> _queues = new();
    private static readonly ConcurrentDictionary<ulong, UltMutexState> _mutexes = new();
    private static readonly ConcurrentDictionary<ulong, UltSemaphoreState> _semaphores = new();

    private sealed class UltRuntimeState
    {
        public ulong RuntimePtr { get; init; }
        public uint MaxUlthreads { get; init; }
        public uint WorkerThreads { get; init; }
        public ulong WorkAreaPtr { get; init; }
    }

    private sealed class UltThreadState
    {
        public ulong Handle { get; init; }
        public ulong EntryPoint { get; init; }
        public ulong Argument { get; init; }
        public ulong ContextPtr { get; init; }
        public ulong ContextSize { get; init; }
        public ulong RuntimePtr { get; init; }
        public bool Finished { get; set; }
        public int ExitCode { get; set; }
        public ManualResetEventSlim JoinEvent { get; } = new(false);
    }

    private sealed class UltResourcePoolState
    {
        public ulong PoolPtr { get; init; }
        public uint NumThreads { get; init; }
        public uint NumSyncObjects { get; init; }
        public ulong WorkAreaPtr { get; init; }
    }

    private sealed class UltQueueDataPoolState
    {
        public ulong PoolPtr { get; init; }
        public uint NumData { get; init; }
        public ulong DataSize { get; init; }
        public uint NumQueueObject { get; init; }
        public ulong WaitingPoolPtr { get; init; }
        public ulong WorkAreaPtr { get; init; }
    }

    private sealed class UltQueueState
    {
        public ulong QueuePtr { get; init; }
        public ulong DataSize { get; init; }
        public uint Capacity { get; init; }
        public ulong WaitingPoolPtr { get; init; }
        public ulong DataPoolPtr { get; init; }
        public object LockGate { get; } = new();
        public Queue<byte[]> Items { get; } = new();
    }

    private sealed class UltMutexState
    {
        public ulong MutexPtr { get; init; }
        public uint Attribute { get; init; }
        public object LockGate { get; } = new();
        public ulong OwnerThreadId { get; set; }
        public int LockCount { get; set; }
    }

    private sealed class UltSemaphoreState
    {
        public ulong SemaphorePtr { get; init; }
        public object LockGate { get; } = new();
        public int Resources { get; set; }
        public int ActiveWaiters { get; set; }
        public bool Alive { get; set; } = true;
        public LinkedList<SemaphoreWaiter> WaitingQueue { get; } = new();
    }

    private sealed class SemaphoreWaiter
    {
        public required ulong ThreadId { get; init; }
        public required int RequestedResources { get; init; }
        public bool Signaled { get; set; }
    }

    private static ulong AlignUp(ulong value, ulong alignment) =>
        (value + alignment - 1UL) & ~(alignment - 1UL);

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static string GetSemaphoreWakeKey(ulong semAddr) => $"ult_sema:0x{semAddr:X16}";

    [SysAbiExport(
        Nid = "hZIg1EWGsHM",
        ExportName = "sceUltInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltInitialize(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(
        Nid = "d-kSG2fLrvI",
        ExportName = "sceUltFinalize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltFinalize(CpuContext ctx)
    {
        lock (_ultGlobalGate)
        {
            foreach (var sem in _semaphores.Values)
            {
                lock (sem.LockGate)
                {
                    sem.Alive = false;
                    foreach (var waiter in sem.WaitingQueue)
                    {
                        waiter.Signaled = true;
                    }
                    sem.WaitingQueue.Clear();
                }
                _ = GuestThreadExecution.Scheduler?.WakeBlockedThreads(GetSemaphoreWakeKey(sem.SemaphorePtr));
            }
            _semaphores.Clear();
            _mutexes.Clear();
            _queues.Clear();
            _queueDataPools.Clear();
            _resourcePools.Clear();
            _runtimes.Clear();
            foreach (var t in _ulthreads.Values)
            {
                t.JoinEvent.Set();
            }
            _ulthreads.Clear();
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "7+pdaXR9jWw",
        ExportName = "sceUltUlthreadRuntimeOptParamInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltUlthreadRuntimeOptParamInitialize(CpuContext ctx)
    {
        var optParamAddr = ctx[CpuRegister.Rdi];
        if (optParamAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        Span<byte> zero = stackalloc byte[128];
        if (!ctx.Memory.TryWrite(optParamAddr, zero))
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "grs2pbc2awM",
        ExportName = "sceUltUlthreadRuntimeGetWorkAreaSize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltUlthreadRuntimeGetWorkAreaSize(CpuContext ctx)
    {
        var maxNumUlthread = (uint)ctx[CpuRegister.Rdi];
        var numWorkerThread = (uint)ctx[CpuRegister.Rsi];

        ulong required = AlignUp((ulong)maxNumUlthread * 256UL + (ulong)numWorkerThread * 16384UL, 8UL);
        ctx[CpuRegister.Rax] = required;
        return OK;
    }

    [SysAbiExport(
        Nid = "81ag9-mItVY",
        ExportName = "sceUltUlthreadRuntimeCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltUlthreadRuntimeCreate(CpuContext ctx)
    {
        var runtimeAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var maxUlthread = (uint)ctx[CpuRegister.Rdx];
        var workerThreads = (uint)ctx[CpuRegister.Rcx];
        var workAreaAddr = ctx[CpuRegister.R8];

        if (runtimeAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        Span<byte> zero = stackalloc byte[512];
        ctx.Memory.TryWrite(runtimeAddr, zero);

        var state = new UltRuntimeState
        {
            RuntimePtr = runtimeAddr,
            MaxUlthreads = maxUlthread,
            WorkerThreads = workerThreads,
            WorkAreaPtr = workAreaAddr,
        };

        _runtimes[runtimeAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "RlN-xQPtYnM",
        ExportName = "sceUltUlthreadCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltUlthreadCreate(CpuContext ctx)
    {
        var ulthreadAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var entryAddr = ctx[CpuRegister.Rdx];
        var arg = ctx[CpuRegister.Rcx];
        var contextAddr = ctx[CpuRegister.R8];
        var sizeContext = ctx[CpuRegister.R9];

        var runtimeAddr = ctx.TryReadUInt64(ctx[CpuRegister.Rsp] + 8, out var rPtr) ? rPtr : 0;

        if (ulthreadAddr == 0 || entryAddr == 0 || contextAddr == 0 || runtimeAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (!_runtimes.ContainsKey(runtimeAddr))
        {
            return SetReturn(ctx, ULT_ERROR_STATE);
        }

        if (_ulthreads.ContainsKey(ulthreadAddr))
        {
            return SetReturn(ctx, ULT_ERROR_STATE);
        }

        Span<byte> zero = stackalloc byte[512];
        ctx.Memory.TryWrite(ulthreadAddr, zero);

        var state = new UltThreadState
        {
            Handle = ulthreadAddr,
            EntryPoint = entryAddr,
            Argument = arg,
            ContextPtr = contextAddr,
            ContextSize = sizeContext,
            RuntimePtr = runtimeAddr,
        };

        _ulthreads[ulthreadAddr] = state;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                state.ExitCode = 0;
            }
            finally
            {
                state.Finished = true;
                state.JoinEvent.Set();
            }
        });

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "gCeAI57LGgI",
        ExportName = "sceUltUlthreadJoin",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltUlthreadJoin(CpuContext ctx)
    {
        var ulthreadAddr = ctx[CpuRegister.Rdi];
        var statusOutAddr = ctx[CpuRegister.Rsi];

        if (ulthreadAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (!_ulthreads.TryGetValue(ulthreadAddr, out var state))
        {
            return SetReturn(ctx, ULT_ERROR_STATE);
        }

        state.JoinEvent.Wait(1000);

        if (statusOutAddr != 0)
        {
            ctx.TryWriteInt32(statusOutAddr, state.ExitCode);
        }

        _ulthreads.TryRemove(ulthreadAddr, out _);
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "WIWV1Qd7PFU",
        ExportName = "sceUltWaitingQueueResourcePoolGetWorkAreaSize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltWaitingQueueResourcePoolGetWorkAreaSize(CpuContext ctx)
    {
        var numThreads = (uint)ctx[CpuRegister.Rdi];
        var numSyncObjects = (uint)ctx[CpuRegister.Rsi];

        ulong required = AlignUp((ulong)(numThreads + numSyncObjects) * 256UL, 8UL);
        ctx[CpuRegister.Rax] = required;
        return OK;
    }

    [SysAbiExport(
        Nid = "0OPujcRPBAE",
        ExportName = "sceUltWaitingQueueResourcePoolCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltWaitingQueueResourcePoolCreate(CpuContext ctx)
    {
        var poolAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var numThreads = (uint)ctx[CpuRegister.Rdx];
        var numSyncObjects = (uint)ctx[CpuRegister.Rcx];
        var workAreaAddr = ctx[CpuRegister.R8];

        if (poolAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        Span<byte> zero = stackalloc byte[256];
        ctx.Memory.TryWrite(poolAddr, zero);

        var state = new UltResourcePoolState
        {
            PoolPtr = poolAddr,
            NumThreads = numThreads,
            NumSyncObjects = numSyncObjects,
            WorkAreaPtr = workAreaAddr,
        };

        _resourcePools[poolAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "evj9YPkS8s4",
        ExportName = "sceUltQueueDataResourcePoolGetWorkAreaSize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltQueueDataResourcePoolGetWorkAreaSize(CpuContext ctx)
    {
        var numData = (uint)ctx[CpuRegister.Rdi];
        var dataSize = ctx[CpuRegister.Rsi];
        var numQueueObj = (uint)ctx[CpuRegister.Rdx];

        ulong dataArea = (ulong)numData * AlignUp(dataSize, 8UL);
        ulong queueArea = (ulong)numQueueObj * 512UL;
        ulong required = AlignUp(dataArea + queueArea, 8UL);

        ctx[CpuRegister.Rax] = required;
        return OK;
    }

    [SysAbiExport(
        Nid = "ojLQon68BTA",
        ExportName = "sceUltQueueDataResourcePoolCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltQueueDataResourcePoolCreate(CpuContext ctx)
    {
        var poolAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var numData = (uint)ctx[CpuRegister.Rdx];
        var dataSize = ctx[CpuRegister.Rcx];
        var numQueueObj = (uint)ctx[CpuRegister.R8];
        var waitingPoolAddr = ctx[CpuRegister.R9];
        var workAreaAddr = ctx.TryReadUInt64(ctx[CpuRegister.Rsp] + 8, out var wPtr) ? wPtr : 0;

        if (poolAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (waitingPoolAddr != 0 && !_resourcePools.ContainsKey(waitingPoolAddr))
        {
            return SetReturn(ctx, ULT_ERROR_INVALID);
        }

        Span<byte> zero = stackalloc byte[512];
        ctx.Memory.TryWrite(poolAddr, zero);

        var state = new UltQueueDataPoolState
        {
            PoolPtr = poolAddr,
            NumData = numData,
            DataSize = dataSize,
            NumQueueObject = numQueueObj,
            WaitingPoolPtr = waitingPoolAddr,
            WorkAreaPtr = workAreaAddr,
        };

        _queueDataPools[poolAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "bMusTcSC2nw",
        ExportName = "sceUltQueueCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltQueueCreate(CpuContext ctx)
    {
        var queueAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var dataSize = ctx[CpuRegister.Rdx];
        var waitingPoolAddr = ctx[CpuRegister.Rcx];
        var dataPoolAddr = ctx[CpuRegister.R8];

        if (queueAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (!_queueDataPools.TryGetValue(dataPoolAddr, out var dataPool))
        {
            return SetReturn(ctx, ULT_ERROR_INVALID);
        }

        if (waitingPoolAddr != 0 && !_resourcePools.ContainsKey(waitingPoolAddr))
        {
            return SetReturn(ctx, ULT_ERROR_INVALID);
        }

        Span<byte> zero = stackalloc byte[512];
        ctx.Memory.TryWrite(queueAddr, zero);

        var state = new UltQueueState
        {
            QueuePtr = queueAddr,
            DataSize = dataSize,
            Capacity = dataPool.NumData,
            WaitingPoolPtr = waitingPoolAddr,
            DataPoolPtr = dataPoolAddr,
        };

        _queues[queueAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "dUwpX3e5NDE",
        ExportName = "sceUltQueuePush",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltQueuePush(CpuContext ctx)
    {
        var queueAddr = ctx[CpuRegister.Rdi];
        var dataAddr = ctx[CpuRegister.Rsi];

        if (!_queues.TryGetValue(queueAddr, out var state))
        {
            var err = queueAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        if (dataAddr == 0 && state.DataSize != 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        byte[] item = new byte[state.DataSize];
        if (state.DataSize > 0 && !ctx.Memory.TryRead(dataAddr, item))
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        lock (state.LockGate)
        {
            if (state.Capacity != 0 && state.Items.Count >= state.Capacity)
            {
                return SetReturn(ctx, OK); // Queue full: drop item per KytyPS5 contract
            }
            state.Items.Enqueue(item);
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "uZz3ci7XYqc",
        ExportName = "sceUltQueueTryPop",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltQueueTryPop(CpuContext ctx)
    {
        var queueAddr = ctx[CpuRegister.Rdi];
        var dataAddr = ctx[CpuRegister.Rsi];

        if (!_queues.TryGetValue(queueAddr, out var state))
        {
            var err = queueAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        if (dataAddr == 0 && state.DataSize != 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        byte[]? item = null;
        lock (state.LockGate)
        {
            if (state.Items.Count == 0)
            {
                return SetReturn(ctx, ULT_ERROR_AGAIN);
            }
            item = state.Items.Dequeue();
        }

        if (item is not null && state.DataSize > 0)
        {
            ctx.Memory.TryWrite(dataAddr, item);
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "UOR33LVrX+k",
        ExportName = "sceUltMutexOptParamInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltMutexOptParamInitialize(CpuContext ctx)
    {
        var optParamAddr = ctx[CpuRegister.Rdi];
        if (optParamAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        Span<byte> zero = stackalloc byte[16];
        if (!ctx.Memory.TryWrite(optParamAddr, zero))
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "+bOLqU65Gg0",
        ExportName = "sceUltMutexCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltMutexCreate(CpuContext ctx)
    {
        var mutexAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var waitingPoolAddr = ctx[CpuRegister.Rdx];
        var optParamAddr = ctx[CpuRegister.Rcx];

        if (mutexAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (waitingPoolAddr != 0 && !_resourcePools.ContainsKey(waitingPoolAddr))
        {
            return SetReturn(ctx, ULT_ERROR_INVALID);
        }

        uint attribute = 0;
        if (optParamAddr != 0 && ctx.TryReadUInt32(optParamAddr + 8, out var attr))
        {
            attribute = attr;
        }

        Span<byte> zero = stackalloc byte[256];
        ctx.Memory.TryWrite(mutexAddr, zero);

        var state = new UltMutexState
        {
            MutexPtr = mutexAddr,
            Attribute = attribute,
        };

        _mutexes[mutexAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "8hEGkR1pfr8",
        ExportName = "sceUltMutexLock",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltMutexLock(CpuContext ctx)
    {
        var mutexAddr = ctx[CpuRegister.Rdi];
        if (!_mutexes.TryGetValue(mutexAddr, out var state))
        {
            var err = mutexAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        ulong threadId = (ulong)Environment.CurrentManagedThreadId;
        Monitor.Enter(state.LockGate);
        state.OwnerThreadId = threadId;
        state.LockCount++;

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "h0XebKiMBtk",
        ExportName = "sceUltMutexUnlock",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltMutexUnlock(CpuContext ctx)
    {
        var mutexAddr = ctx[CpuRegister.Rdi];
        if (!_mutexes.TryGetValue(mutexAddr, out var state))
        {
            var err = mutexAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        lock (state.LockGate)
        {
            if (state.LockCount > 0)
            {
                state.LockCount--;
                if (state.LockCount == 0)
                {
                    state.OwnerThreadId = 0;
                }
            }
        }
        Monitor.Exit(state.LockGate);

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "R-bzjZkqSEs",
        ExportName = "sceUltSemaphoreCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltSemaphoreCreate(CpuContext ctx)
    {
        var semAddr = ctx[CpuRegister.Rdi];
        var nameAddr = ctx[CpuRegister.Rsi];
        var initialResources = (int)ctx[CpuRegister.Rdx];
        var waitingPoolAddr = ctx[CpuRegister.Rcx];
        var optParamAddr = ctx[CpuRegister.R8];

        if (semAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if ((semAddr & 7UL) != 0 || (optParamAddr != 0 && (optParamAddr & 7UL) != 0))
        {
            return SetReturn(ctx, ULT_ERROR_ALIGNMENT);
        }

        if (initialResources < 0)
        {
            return SetReturn(ctx, ULT_ERROR_RANGE);
        }

        if (waitingPoolAddr != 0 && !_resourcePools.ContainsKey(waitingPoolAddr))
        {
            return SetReturn(ctx, ULT_ERROR_INVALID);
        }

        if (_semaphores.ContainsKey(semAddr))
        {
            return SetReturn(ctx, ULT_ERROR_STATE);
        }

        Span<byte> zero = stackalloc byte[256];
        ctx.Memory.TryWrite(semAddr, zero);

        var state = new UltSemaphoreState
        {
            SemaphorePtr = semAddr,
            Resources = initialResources,
        };

        _semaphores[semAddr] = state;
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "QAH1ofI97vU",
        ExportName = "sceUltSemaphoreAcquire",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltSemaphoreAcquire(CpuContext ctx)
    {
        var semAddr = ctx[CpuRegister.Rdi];
        var numResources = (int)ctx[CpuRegister.Rsi];

        if (numResources <= 0)
        {
            return SetReturn(ctx, ULT_ERROR_RANGE);
        }

        if (!_semaphores.TryGetValue(semAddr, out var state))
        {
            var err = semAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        lock (state.LockGate)
        {
            if (!state.Alive)
            {
                return SetReturn(ctx, ULT_ERROR_STATE);
            }

            if (state.Resources >= numResources)
            {
                state.Resources -= numResources;
                return SetReturn(ctx, OK);
            }

            ulong threadId = (ulong)Environment.CurrentManagedThreadId;
            var waiter = new SemaphoreWaiter
            {
                ThreadId = threadId,
                RequestedResources = numResources,
            };

            state.ActiveWaiters++;
            state.WaitingQueue.AddLast(waiter);

            var wakeKey = GetSemaphoreWakeKey(semAddr);
            if (GuestThreadExecution.RequestCurrentThreadBlock(
                    ctx,
                    "sceUltSemaphoreAcquire",
                    wakeKey,
                    () => OK,
                    () => waiter.Signaled,
                    0L))
            {
                return SetReturn(ctx, OK);
            }
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "HA1Ldbi3lPY",
        ExportName = "sceUltSemaphoreTryAcquire",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltSemaphoreTryAcquire(CpuContext ctx)
    {
        var semAddr = ctx[CpuRegister.Rdi];
        var numResources = (int)ctx[CpuRegister.Rsi];

        if (numResources <= 0)
        {
            return SetReturn(ctx, ULT_ERROR_RANGE);
        }

        if (!_semaphores.TryGetValue(semAddr, out var state))
        {
            var err = semAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        lock (state.LockGate)
        {
            if (!state.Alive)
            {
                return SetReturn(ctx, ULT_ERROR_STATE);
            }

            if (state.Resources < numResources)
            {
                return SetReturn(ctx, ULT_ERROR_AGAIN);
            }

            state.Resources -= numResources;
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "lbtk5X1mecw",
        ExportName = "sceUltSemaphoreRelease",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltSemaphoreRelease(CpuContext ctx)
    {
        var semAddr = ctx[CpuRegister.Rdi];
        var numResources = (int)ctx[CpuRegister.Rsi];

        if (numResources <= 0)
        {
            return SetReturn(ctx, ULT_ERROR_RANGE);
        }

        if (!_semaphores.TryGetValue(semAddr, out var state))
        {
            var err = semAddr == 0 ? ULT_ERROR_NULL : ULT_ERROR_STATE;
            return SetReturn(ctx, err);
        }

        lock (state.LockGate)
        {
            if (!state.Alive)
            {
                return SetReturn(ctx, ULT_ERROR_STATE);
            }

            if (state.Resources > int.MaxValue - numResources)
            {
                return SetReturn(ctx, ULT_ERROR_RANGE);
            }

            state.Resources += numResources;

            var node = state.WaitingQueue.First;
            while (node != null)
            {
                var next = node.Next;
                var waiter = node.Value;
                if (state.Resources >= waiter.RequestedResources)
                {
                    state.Resources -= waiter.RequestedResources;
                    waiter.Signaled = true;
                    state.ActiveWaiters--;
                    state.WaitingQueue.Remove(node);
                    _ = GuestThreadExecution.Scheduler?.WakeBlockedThreads(GetSemaphoreWakeKey(semAddr), 1);
                }
                node = next;
            }
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(
        Nid = "izXyehpoZGo",
        ExportName = "sceUltSemaphoreDestroy",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUlt")]
    public static int UltSemaphoreDestroy(CpuContext ctx)
    {
        var semAddr = ctx[CpuRegister.Rdi];
        if (semAddr == 0)
        {
            return SetReturn(ctx, ULT_ERROR_NULL);
        }

        if (!_semaphores.TryGetValue(semAddr, out var state))
        {
            return SetReturn(ctx, ULT_ERROR_STATE);
        }

        lock (state.LockGate)
        {
            if (state.ActiveWaiters != 0)
            {
                return SetReturn(ctx, ULT_ERROR_BUSY);
            }

            state.Alive = false;
        }

        _semaphores.TryRemove(semAddr, out _);
        return SetReturn(ctx, OK);
    }
}
