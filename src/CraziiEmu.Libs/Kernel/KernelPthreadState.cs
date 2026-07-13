// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Kernel;

internal static class KernelPthreadState
{
    private const int ThreadObjectSize = 0x1000;

    private static readonly ConcurrentDictionary<ulong, ThreadIdentity> Threads = new();
    private static readonly byte[] ZeroThreadObject = new byte[ThreadObjectSize];
    private static long _nextUniqueThreadId = 1;

    [ThreadStatic]
    private static ulong _currentThreadHandle;

    [ThreadStatic]
    private static ulong _currentThreadUniqueId;

    internal readonly record struct ThreadIdentity(ulong UniqueId, string Name);

    internal static ulong GetCurrentThreadHandle(CpuContext ctx)
    {
        var guestThreadHandle = GuestThreadExecution.CurrentGuestThreadHandle;
        if (guestThreadHandle != 0 && TryGetThreadIdentity(guestThreadHandle, out _))
        {
            return guestThreadHandle;
        }

        EnsureCurrentThreadRegistered(ctx);
        return _currentThreadHandle;
    }

    internal static ulong GetCurrentThreadUniqueId(CpuContext ctx)
    {
        var guestThreadHandle = GuestThreadExecution.CurrentGuestThreadHandle;
        if (guestThreadHandle != 0 && TryGetThreadIdentity(guestThreadHandle, out var identity))
        {
            return identity.UniqueId;
        }

        EnsureCurrentThreadRegistered(ctx);
        return _currentThreadUniqueId;
    }

    internal static ulong CreateThreadHandle(CpuContext ctx, string name)
    {
        var uniqueId = unchecked((ulong)Interlocked.Increment(ref _nextUniqueThreadId));
        return AllocateThreadHandle(ctx, uniqueId, name);
    }

    internal static bool TryGetThreadIdentity(ulong handle, out ThreadIdentity identity)
    {
        return Threads.TryGetValue(handle, out identity);
    }

    private static void EnsureCurrentThreadRegistered(CpuContext ctx)
    {
        if (_currentThreadHandle != 0)
        {
            return;
        }

        var uniqueId = unchecked((ulong)Interlocked.Increment(ref _nextUniqueThreadId));
        var name = $"Thread-{uniqueId:X}";
        _currentThreadHandle = AllocateThreadHandle(ctx, uniqueId, name);
        _currentThreadUniqueId = uniqueId;
    }

    private static ulong AllocateThreadHandle(CpuContext ctx, ulong uniqueId, string name)
    {
        ulong handle = 0;
        if (ctx.Memory is IGuestMemoryAllocator allocator &&
            allocator.TryAllocateGuestMemory(ThreadObjectSize, 16, out var guestAddr))
        {
            ctx.Memory.TryWrite(guestAddr, ZeroThreadObject);
            handle = guestAddr;
        }

        Threads[handle] = new ThreadIdentity(uniqueId, string.IsNullOrWhiteSpace(name) ? $"Thread-{uniqueId:X}" : name);

        return handle;
    }
}
