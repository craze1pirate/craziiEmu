// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Psml;

public static class PsmlExports
{
    public const uint SHARED_RESOURCES_MAGIC = 0xA9C4;
    public const uint CONTEXT_MAGIC = 0x9231;
    public const int PSML_ERROR_NOT_INITIALIZED = unchecked((int)0x80540001);
    public const int PSML_ERROR_INVALID_POINTER = unchecked((int)0x80540002);
    public const int PSML_ERROR_INVALID_OBJECT = unchecked((int)0x80540003);

    private static bool _isInitialized;

    private const ulong SharedResourcesPageSize = 0x10000;
    private const ulong SharedResourcesDefaultBufferSize = 0x800_0000; // 128 MiB
    private const ulong SharedResourcesDefaultContextSize = 0x200_0000; // 32 MiB
    private const ulong RequirementStructSize = 0x40;
    private const ulong DefaultContextStructSize = 0x80;

    private static readonly object SharedResourcesGate = new();
    private static readonly Dictionary<ulong, SharedResourcesState> SharedResourcesByDescriptor = new();

    private static readonly object ContextGate = new();
    private static readonly Dictionary<ulong, ContextState> ContextsByAddress = new();

    public static void ResetStateForTest()
    {
        _isInitialized = false;
        lock (SharedResourcesGate)
        {
            SharedResourcesByDescriptor.Clear();
        }
        lock (ContextGate)
        {
            ContextsByAddress.Clear();
        }
    }

    public static int PsmlInitialize(CpuContext ctx)
    {
        _isInitialized = true;
        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlGetMainMemoryRequirements(CpuContext ctx)
    {
        if (!_isInitialized)
        {
            return PSML_ERROR_NOT_INITIALIZED;
        }

        var outPtr = ctx[CpuRegister.Rdi];
        var paramsPtr = ctx[CpuRegister.Rsi];
        if (outPtr == 0 || paramsPtr == 0)
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        Span<byte> modeBytes = stackalloc byte[4];
        if (!ctx.Memory.TryRead(paramsPtr, modeBytes))
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        var mode = BinaryPrimitives.ReadUInt32LittleEndian(modeBytes);
        ulong blockCount = mode switch
        {
            1 => 196,
            2 => 148,
            _ => 52,
        };

        Span<byte> outBuf = stackalloc byte[24];
        outBuf.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(outBuf[16..24], blockCount);
        if (!ctx.Memory.TryWrite(outPtr, outBuf))
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlSharedResourcesInitialize(CpuContext ctx)
    {
        if (!_isInitialized)
        {
            return PSML_ERROR_NOT_INITIALIZED;
        }

        var resPtr = ctx[CpuRegister.Rdi];
        var paramsPtr = ctx[CpuRegister.Rsi];
        if (resPtr == 0 || paramsPtr == 0)
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        Span<byte> header = stackalloc byte[48];
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], SHARED_RESOURCES_MAGIC);
        if (!ctx.Memory.TryWrite(resPtr, header))
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlContextInitialize(CpuContext ctx)
    {
        if (!_isInitialized)
        {
            return PSML_ERROR_NOT_INITIALIZED;
        }

        var ctxPtr = ctx[CpuRegister.Rdi];
        var paramsPtr = ctx[CpuRegister.Rsi];
        if (ctxPtr == 0 || paramsPtr == 0)
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, CONTEXT_MAGIC);
        if (!ctx.Memory.TryWrite(ctxPtr, header))
        {
            return PSML_ERROR_INVALID_POINTER;
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlGetWorkAreaSize(CpuContext ctx)
    {
        var outSizePtr = ctx[CpuRegister.Rsi];
        if (outSizePtr != 0)
        {
            Span<byte> sizeBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, 0x600);
            _ = ctx.Memory.TryWrite(outSizePtr, sizeBytes);
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlGetProgress(CpuContext ctx)
    {
        var outProgressPtr = ctx[CpuRegister.Rsi];
        if (outProgressPtr != 0)
        {
            Span<byte> progressBytes = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(progressBytes, 0.0f);
            _ = ctx.Memory.TryWrite(outProgressPtr, progressBytes);
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    public static int PsmlValidateObject(CpuContext ctx)
    {
        var objPtr = ctx[CpuRegister.Rdi];
        if (objPtr == 0)
        {
            return PSML_ERROR_INVALID_OBJECT;
        }

        Span<byte> magicBytes = stackalloc byte[4];
        if (!ctx.Memory.TryRead(objPtr, magicBytes))
        {
            return PSML_ERROR_INVALID_OBJECT;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(magicBytes);
        if (magic != SHARED_RESOURCES_MAGIC && magic != CONTEXT_MAGIC)
        {
            return PSML_ERROR_INVALID_OBJECT;
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    [SysAbiExport(
        Nid = "3WVD91e12ZQ",
        ExportName = "scePsmlMfsrInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrInit(CpuContext ctx)
    {
        var initParamsAddress = ctx[CpuRegister.Rdi];
        TracePsml($"mfsr_init params=0x{initParamsAddress:X16}");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "+2KpvixvL6E",
        ExportName = "scePsmlMfsrGetSharedResourcesInitRequirement",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetSharedResourcesInitRequirement(CpuContext ctx)
    {
        var requirementAddress = ctx[CpuRegister.Rdi];
        if (requirementAddress == 0)
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        if (!WriteMemoryRequirement(ctx, requirementAddress, SharedResourcesDefaultBufferSize))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TracePsml(
            $"mfsr_get_shared_resources_init_req out=0x{requirementAddress:X16} " +
            $"size=0x{SharedResourcesDefaultBufferSize:X} page=0x{SharedResourcesPageSize:X}");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "eWoKNeB6V-k",
        ExportName = "scePsmlMfsrCreateSharedResources",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrCreateSharedResources(CpuContext ctx)
    {
        var descriptorAddress = ctx[CpuRegister.Rdi];
        var requirementAddress = ctx[CpuRegister.Rsi];
        var directMemoryAddress = ctx[CpuRegister.Rdx];

        if (descriptorAddress == 0)
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var bufferSize = ReadRequirementSize(ctx, requirementAddress, SharedResourcesDefaultBufferSize);
        var state = new SharedResourcesState(
            DescriptorAddress: descriptorAddress,
            DirectMemoryAddress: directMemoryAddress,
            BufferSizeBytes: bufferSize,
            ContextSizeBytes: SharedResourcesDefaultContextSize,
            PageSizeBytes: SharedResourcesPageSize);

        if (!WriteSharedResourcesDescriptor(ctx, state))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        lock (SharedResourcesGate)
        {
            SharedResourcesByDescriptor[descriptorAddress] = state;
        }

        TracePsml(
            $"mfsr_create_shared_resources desc=0x{descriptorAddress:X16} req=0x{requirementAddress:X16} " +
            $"direct=0x{directMemoryAddress:X16} buf_size=0x{bufferSize:X}");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "ArakEpzsZo0",
        ExportName = "scePsmlMfsrGetContextBufferRequirement800M3_2",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetContextBufferRequirement800M3_2(CpuContext ctx)
    {
        var requirementAddress = ctx[CpuRegister.Rdi];
        if (requirementAddress == 0)
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        if (!WriteMemoryRequirement(ctx, requirementAddress, SharedResourcesDefaultContextSize))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TracePsml(
            $"mfsr_get_context_req_800m3_2 out=0x{requirementAddress:X16} " +
            $"size=0x{SharedResourcesDefaultContextSize:X} page=0x{SharedResourcesPageSize:X}");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "gxv3i+MTEzU",
        ExportName = "scePsmlMfsrCreateContext800M3_2",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrCreateContext800M3_2(CpuContext ctx)
    {
        var contextAddress = ctx[CpuRegister.Rdi];
        var requirementAddress = ctx[CpuRegister.Rsi];
        var sharedResourcesDescriptor = ctx[CpuRegister.Rdx];
        var sharedDirectMemory = ctx[CpuRegister.Rcx];

        if (contextAddress == 0)
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var sharedState = TryFindSharedResources(sharedDirectMemory, sharedResourcesDescriptor);
        var bufferSize = ReadRequirementSize(
            ctx,
            requirementAddress,
            sharedState?.ContextSizeBytes ?? SharedResourcesDefaultContextSize);
        var effectivePageSize = sharedState?.PageSizeBytes ?? SharedResourcesPageSize;
        var effectiveStructSize = DefaultContextStructSize;
        var sharedDescriptor = sharedResourcesDescriptor != 0
            ? sharedResourcesDescriptor
            : (sharedState?.DescriptorAddress ?? 0);

        var state = new ContextState(
            ContextAddress: contextAddress,
            SharedResourcesDescriptor: sharedDescriptor,
            DirectMemoryAddress: sharedDirectMemory,
            BufferSizeBytes: bufferSize,
            PageSizeBytes: effectivePageSize,
            StructSizeBytes: effectiveStructSize);

        if (!WriteContextObject(ctx, state))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        lock (ContextGate)
        {
            ContextsByAddress[contextAddress] = state;
        }

        TracePsml(
            $"mfsr_create_context_800m3_2 ctx=0x{contextAddress:X16} req=0x{requirementAddress:X16} " +
            $"struct=0x{effectiveStructSize:X} direct=0x{sharedDirectMemory:X16} " +
            $"shared_desc=0x{sharedDescriptor:X16} buf_size=0x{bufferSize:X} page=0x{effectivePageSize:X}");
        return ctx.SetReturn(0);
    }

    private static bool WriteMemoryRequirement(CpuContext ctx, ulong address, ulong sizeBytes)
    {
        return ctx.TryWriteUInt64(address, RequirementStructSize) &&
               ctx.TryWriteUInt64(address + 0x08, sizeBytes) &&
               ctx.TryWriteUInt64(address + 0x10, SharedResourcesPageSize);
    }

    private static ulong ReadRequirementSize(CpuContext ctx, ulong address, ulong fallback)
    {
        if (!ctx.TryReadUInt64(address + 0x08, out var sizeBytes) || sizeBytes == 0)
        {
            return fallback;
        }

        return sizeBytes > 0x1000_0000UL ? fallback : sizeBytes;
    }

    private static SharedResourcesState? TryFindSharedResources(ulong directMemoryAddress, ulong contextAddress)
    {
        lock (SharedResourcesGate)
        {
            foreach (var state in SharedResourcesByDescriptor.Values)
            {
                if (state.DirectMemoryAddress == directMemoryAddress)
                {
                    return state;
                }
            }

            var inferredDescriptor = contextAddress >= 0x30 ? contextAddress - 0x30 : 0;
            if (inferredDescriptor != 0 &&
                SharedResourcesByDescriptor.TryGetValue(inferredDescriptor, out var byDescriptor))
            {
                return byDescriptor;
            }
        }

        return null;
    }

    private static bool WriteContextObject(CpuContext ctx, ContextState state)
    {
        return ctx.TryWriteUInt64(state.ContextAddress + 0x00, state.StructSizeBytes) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x08, state.DirectMemoryAddress) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x10, state.SharedResourcesDescriptor) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x18, state.BufferSizeBytes) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x20, state.PageSizeBytes) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x28, state.ContextAddress) &&
               ctx.TryWriteUInt64(state.ContextAddress + 0x30, state.DirectMemoryAddress);
    }

    private static bool WriteSharedResourcesDescriptor(CpuContext ctx, SharedResourcesState state)
    {
        return ctx.TryWriteUInt64(state.DescriptorAddress + 0x00, RequirementStructSize) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x08, state.DirectMemoryAddress) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x10, state.DirectMemoryAddress) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x18, state.BufferSizeBytes) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x20, state.ContextSizeBytes) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x28, state.PageSizeBytes) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x30, state.DirectMemoryAddress) &&
               ctx.TryWriteUInt64(state.DescriptorAddress + 0x38, state.BufferSizeBytes + state.ContextSizeBytes);
    }

    private const int SoftPacketSizeInDwords = 0x80;
    private const ulong SoftPacketSizeBytes = SoftPacketSizeInDwords * 4UL;

    [SysAbiExport(
        Nid = "AHalTX9wFZY",
        ExportName = "scePsmlMfsrGetDispatchMfsrPacketSizeInDwords",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetDispatchMfsrPacketSizeInDwords(CpuContext ctx)
    {
        var arg0 = ctx[CpuRegister.Rdi];
        TracePsml(
            $"mfsr_get_dispatch_packet_size_dwords arg0=0x{arg0:X} " +
            $"size=0x{SoftPacketSizeInDwords:X} ret=0");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "RUNLFro+qok",
        ExportName = "scePsmlMfsrGetDispatchMfsrPacket900",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetDispatchMfsrPacket900(CpuContext ctx) =>
        SoftGetDispatchMfsrPacket(ctx, "900");

    [SysAbiExport(
        Nid = "s2psNHUIdjk",
        ExportName = "scePsmlMfsrGetDispatchMfsrPacket1000",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetDispatchMfsrPacket1000(CpuContext ctx) =>
        SoftGetDispatchMfsrPacket(ctx, "1000");

    [SysAbiExport(
        Nid = "94iBp3KvIuI",
        ExportName = "scePsmlMfsrGetDispatchMfsrPacket1100",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePsml")]
    public static int PsmlMfsrGetDispatchMfsrPacket1100(CpuContext ctx) =>
        SoftGetDispatchMfsrPacket(ctx, "1100");

    private static int SoftGetDispatchMfsrPacket(CpuContext ctx, string variant)
    {
        var arg0 = ctx[CpuRegister.Rdi];
        var arg1 = ctx[CpuRegister.Rsi];
        var arg2 = ctx[CpuRegister.Rdx];
        var arg3 = ctx[CpuRegister.Rcx];

        var packetAddress = 0UL;
        foreach (var candidate in new[] { arg1, arg2, arg3 })
        {
            if (candidate >= 0x10000)
            {
                packetAddress = candidate;
                break;
            }
        }

        var cleared = packetAddress != 0 && TryClearGuestBuffer(ctx, packetAddress, SoftPacketSizeBytes);
        TracePsml(
            $"mfsr_get_dispatch_packet_{variant} a0=0x{arg0:X16} a1=0x{arg1:X16} " +
            $"a2=0x{arg2:X16} a3=0x{arg3:X16} packet=0x{packetAddress:X16} cleared={cleared}");
        return ctx.SetReturn(0);
    }

    private static bool TryClearGuestBuffer(CpuContext ctx, ulong address, ulong length)
    {
        Span<byte> zeroes = stackalloc byte[4096];
        zeroes.Clear();
        for (ulong offset = 0; offset < length;)
        {
            var chunkSize = (int)Math.Min((ulong)zeroes.Length, length - offset);
            if (!ctx.Memory.TryWrite(address + offset, zeroes[..chunkSize]))
            {
                return false;
            }

            offset += unchecked((uint)chunkSize);
        }

        return true;
    }

    private static void TracePsml(string message)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_PSML") ?? Environment.GetEnvironmentVariable("SHARPEMU_LOG_PSML"),
                "1",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[LOADER][TRACE] psml.{message}");
        }
    }

    private readonly record struct SharedResourcesState(
        ulong DescriptorAddress,
        ulong DirectMemoryAddress,
        ulong BufferSizeBytes,
        ulong ContextSizeBytes,
        ulong PageSizeBytes);

    private readonly record struct ContextState(
        ulong ContextAddress,
        ulong SharedResourcesDescriptor,
        ulong DirectMemoryAddress,
        ulong BufferSizeBytes,
        ulong PageSizeBytes,
        ulong StructSizeBytes);
}
