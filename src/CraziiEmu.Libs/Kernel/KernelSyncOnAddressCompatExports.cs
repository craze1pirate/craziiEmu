// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Kernel;

// libKernel's address-wait primitives (sceKernelSyncOnAddress*) are the PS5's
// futex-style wait/wake: a thread parks on a guest address until another thread
// wakes that address. Guest runtimes and game engines (including Unreal Engine 4
// and Unity) build their own spinlocks/queues on top of it.
//
// In KytyPS5 (src/kernel/syncOnAddress.cpp), if the memory value at the address
// no longer matches the expected value, the wait returns OK (0) immediately
// without blocking. Only when *address == expected does it block until woken
// or until the microsecond timeout expires.
public static class KernelSyncOnAddressCompatExports
{
    private static readonly TimeSpan WaitSelfHealTimeout = TimeSpan.FromMilliseconds(200);

    // Per-address host gate for non-cooperative threads.
    private static readonly ConcurrentDictionary<ulong, object> _hostAddressGates = new();

    // Per-address wake generation counter to track wake events across threads.
    private static readonly ConcurrentDictionary<ulong, long> _wakeGenerations = new();

    private static long CurrentGeneration(ulong address) =>
        _wakeGenerations.TryGetValue(address, out var generation) ? generation : 0;

    private static string WakeKey(ulong address) => $"sceKernelSyncOnAddress:{address:X16}";

    [SysAbiExport(
        Nid = "Hc4CaR6JBL0",
        ExportName = "sceKernelSyncOnAddressWait",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int SyncOnAddressWait(CpuContext ctx)
    {
        return SyncOnAddressWait32Core(ctx, "sceKernelSyncOnAddressWait");
    }

    [SysAbiExport(
        Nid = "B2n8aDorSH4",
        ExportName = "sceKernelSyncOnAddressWait32",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int SyncOnAddressWait32(CpuContext ctx)
    {
        return SyncOnAddressWait32Core(ctx, "sceKernelSyncOnAddressWait32");
    }

    [SysAbiExport(
        Nid = "PZQhiiLXRFs",
        ExportName = "sceKernelSyncOnAddressWait64",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int SyncOnAddressWait64(CpuContext ctx)
    {
        return SyncOnAddressWait64Core(ctx, "sceKernelSyncOnAddressWait64");
    }

    private static int SyncOnAddressWait32Core(CpuContext ctx, string opName)
    {
        var address = ctx[CpuRegister.Rdi];
        if (address == 0 || (address & 3) != 0)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var expected = (uint)ctx[CpuRegister.Rsi];
        var timeoutPtr = ctx[CpuRegister.Rdx];

        if (!ctx.TryReadUInt32(address, out var currentVal))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        // Fast path: if the value already changed, return OK immediately (no blocking)
        if (currentVal != expected)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        long deadline = 0;
        var hasFiniteTimeout = false;
        uint timeoutUs = 0;
        if (timeoutPtr != 0)
        {
            if (!ctx.TryReadUInt32(timeoutPtr, out timeoutUs))
            {
                return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            if (timeoutUs == 0)
            {
                return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT);
            }

            hasFiniteTimeout = true;
            deadline = GuestThreadExecution.ComputeDeadlineTimestamp(TimeSpan.FromTicks((long)timeoutUs * 10));
        }
        else
        {
            deadline = GuestThreadExecution.ComputeDeadlineTimestamp(WaitSelfHealTimeout);
        }

        var observedGen = CurrentGeneration(address);

        // Cooperative scheduler path
        if (GuestThreadExecution.RequestCurrentThreadBlock(
                ctx,
                opName,
                WakeKey(address),
                resumeHandler: () =>
                {
                    if (ctx.TryReadUInt32(address, out var val) && val != expected)
                    {
                        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
                    }
                    if (CurrentGeneration(address) != observedGen)
                    {
                        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
                    }
                    return hasFiniteTimeout
                        ? (int)OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT
                        : (int)OrbisGen2Result.ORBIS_GEN2_OK;
                },
                wakeHandler: () =>
                {
                    if (ctx.TryReadUInt32(address, out var val) && val != expected)
                    {
                        return true;
                    }
                    return CurrentGeneration(address) != observedGen;
                },
                deadline))
        {
            return (int)ctx[CpuRegister.Rax];
        }

        // Non-cooperative host thread fallback
        var gate = _hostAddressGates.GetOrAdd(address, static _ => new object());
        lock (gate)
        {
            var waitDuration = hasFiniteTimeout
                ? TimeSpan.FromTicks((long)timeoutUs * 10)
                : WaitSelfHealTimeout;

            if (ctx.TryReadUInt32(address, out var val) && val == expected &&
                CurrentGeneration(address) == observedGen)
            {
                Monitor.Wait(gate, waitDuration);
            }
        }

        if (ctx.TryReadUInt32(address, out var finalVal) && finalVal != expected)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        if (CurrentGeneration(address) != observedGen)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        return SetReturn(ctx, hasFiniteTimeout
            ? OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT
            : OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int SyncOnAddressWait64Core(CpuContext ctx, string opName)
    {
        var address = ctx[CpuRegister.Rdi];
        if (address == 0 || (address & 7) != 0)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var expected = ctx[CpuRegister.Rsi];
        var timeoutPtr = ctx[CpuRegister.Rdx];

        if (!ctx.TryReadUInt64(address, out var currentVal))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        // Fast path: if the value already changed, return OK immediately (no blocking)
        if (currentVal != expected)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        long deadline = 0;
        var hasFiniteTimeout = false;
        uint timeoutUs = 0;
        if (timeoutPtr != 0)
        {
            if (!ctx.TryReadUInt32(timeoutPtr, out timeoutUs))
            {
                return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            if (timeoutUs == 0)
            {
                return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT);
            }

            hasFiniteTimeout = true;
            deadline = GuestThreadExecution.ComputeDeadlineTimestamp(TimeSpan.FromTicks((long)timeoutUs * 10));
        }
        else
        {
            deadline = GuestThreadExecution.ComputeDeadlineTimestamp(WaitSelfHealTimeout);
        }

        var observedGen = CurrentGeneration(address);

        // Cooperative scheduler path
        if (GuestThreadExecution.RequestCurrentThreadBlock(
                ctx,
                opName,
                WakeKey(address),
                resumeHandler: () =>
                {
                    if (ctx.TryReadUInt64(address, out var val) && val != expected)
                    {
                        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
                    }
                    if (CurrentGeneration(address) != observedGen)
                    {
                        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
                    }
                    return hasFiniteTimeout
                        ? (int)OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT
                        : (int)OrbisGen2Result.ORBIS_GEN2_OK;
                },
                wakeHandler: () =>
                {
                    if (ctx.TryReadUInt64(address, out var val) && val != expected)
                    {
                        return true;
                    }
                    return CurrentGeneration(address) != observedGen;
                },
                deadline))
        {
            return (int)ctx[CpuRegister.Rax];
        }

        // Non-cooperative host thread fallback
        var gate = _hostAddressGates.GetOrAdd(address, static _ => new object());
        lock (gate)
        {
            var waitDuration = hasFiniteTimeout
                ? TimeSpan.FromTicks((long)timeoutUs * 10)
                : WaitSelfHealTimeout;

            if (ctx.TryReadUInt64(address, out var val) && val == expected &&
                CurrentGeneration(address) == observedGen)
            {
                Monitor.Wait(gate, waitDuration);
            }
        }

        if (ctx.TryReadUInt64(address, out var finalVal) && finalVal != expected)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        if (CurrentGeneration(address) != observedGen)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
        }

        return SetReturn(ctx, hasFiniteTimeout
            ? OrbisGen2Result.ORBIS_GEN2_ERROR_TIMED_OUT
            : OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "q2y-wDIVWZA",
        ExportName = "sceKernelSyncOnAddressWake",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int SyncOnAddressWake(CpuContext ctx)
    {
        var address = ctx[CpuRegister.Rdi];
        if (address == 0)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // rsi carries the number of waiters to release (1 = wake-one, a large
        // value = wake-all); default to all if it looks unset.
        var requested = unchecked((long)ctx[CpuRegister.Rsi]);
        var wakeCount = requested is > 0 and < int.MaxValue ? (int)requested : int.MaxValue;

        // Bump the generation first so a wait that has registered but not yet
        // parked sees the change and resumes instead of missing this wake.
        _wakeGenerations.AddOrUpdate(address, 1, static (_, current) => current + 1);

        GuestThreadExecution.Scheduler?.WakeBlockedThreads(WakeKey(address), wakeCount);

        if (_hostAddressGates.TryGetValue(address, out var gate))
        {
            lock (gate)
            {
                Monitor.PulseAll(gate);
            }
        }

        return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int SetReturn(CpuContext ctx, OrbisGen2Result result)
    {
        var value = (int)result;
        ctx[CpuRegister.Rax] = unchecked((ulong)value);
        return value;
    }
}

