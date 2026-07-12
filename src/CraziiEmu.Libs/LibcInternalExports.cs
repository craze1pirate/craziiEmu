// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.LibcInternal;

public static class LibcInternalExports
{
    private const ulong HeapTraceInfoSize = 32;
    private const int HeapTraceTableEntryCount = 64;
    private const int HeapTraceMaskOffset = 0;
    private const int HeapTraceTableOffset = HeapTraceMaskOffset + sizeof(ulong);
    private const int HeapTraceStorageSize = HeapTraceTableOffset + (HeapTraceTableEntryCount * sizeof(ulong));

    private static readonly object _heapTraceGate = new();
    private static nint _heapTraceStorage;

    [SysAbiExport(
        Nid = "NWtTN10cJzE",
        ExportName = "LibcHeapGetTraceInfo",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "LibcInternalExt")]
    public static int LibcHeapGetTraceInfo(CpuContext ctx)
    {
        var infoAddress = ctx[CpuRegister.Rdi];
        if (infoAddress == 0 || !ctx.TryReadUInt64(infoAddress, out var size) || size != HeapTraceInfoSize)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
        }

        var storage = EnsureHeapTraceStorage(ctx);
        if (storage == 0)
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }

        var maskAddress = unchecked((ulong)(storage + HeapTraceMaskOffset));
        var tableAddress = unchecked((ulong)(storage + HeapTraceTableOffset));
        if (!ctx.TryWriteUInt64(infoAddress + 16, maskAddress) ||
            !ctx.TryWriteUInt64(infoAddress + 24, tableAddress))
        {
            ctx[CpuRegister.Rax] = 0;
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }

        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    private static nint EnsureHeapTraceStorage(CpuContext ctx)
    {
        lock (_heapTraceGate)
        {
            if (_heapTraceStorage != 0)
            {
                return _heapTraceStorage;
            }

            if (ctx.Memory is IGuestMemoryAllocator allocator &&
                allocator.TryAllocateGuestMemory(HeapTraceStorageSize, 16, out var storage))
            {
                ctx.Memory.TryWrite(storage, new byte[HeapTraceStorageSize]);
                _heapTraceStorage = unchecked((nint)storage);
                return _heapTraceStorage;
            }

            return 0;
        }
    }
}
