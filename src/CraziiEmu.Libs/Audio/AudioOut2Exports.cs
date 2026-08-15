// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace CraziiEmu.Libs.Audio;

public static class AudioOut2Exports
{
    // FMOD's PS5 backend allocates this ABI structure as four 16-byte lanes.
    // Clearing 0x80 bytes here overwrote the caller's stack canary immediately
    // following the 0x40-byte parameter block.
    private const int AudioOut2ContextParamSize = 0x40;
    private const int AudioOut2ContextMemorySize = 0x10000;
    private const int AudioOut2ContextMemoryAlignment = 0x10000;
    private static long _nextContextHandle = 1;
    private static long _nextUserHandle = 1;
    private static int _nextPortId;
    private static long _pushTraceCount;

    // Per-context audio parameters captured at ContextCreate so ContextAdvance
    // can pace to the real playback cadence (grain samples at the sample rate).
    private static readonly ConcurrentDictionary<ulong, ContextState> Contexts = new();

    private sealed class ContextState
    {
        private readonly object _paceGate = new();
        private long _nextAdvanceTimestamp;

        public ContextState(uint frequency, uint channels, uint grainSamples)
        {
            Frequency = frequency == 0 ? 48000 : frequency;
            Channels = channels == 0 ? 2 : channels;
            GrainSamples = grainSamples == 0 ? 256 : grainSamples;
        }

        public uint Frequency { get; }
        public uint Channels { get; }
        public uint GrainSamples { get; }

        // Blocks the advancing thread until one grain worth of wall-clock time
        // has elapsed since the previous advance, matching hardware timing so
        // audio-gated titles neither spin nor drift ahead.
        public void PaceAdvance()
        {
            long delay;
            lock (_paceGate)
            {
                var now = Stopwatch.GetTimestamp();
                if (_nextAdvanceTimestamp < now)
                {
                    _nextAdvanceTimestamp = now;
                }

                delay = _nextAdvanceTimestamp - now;
                _nextAdvanceTimestamp += checked(
                    (long)Math.Ceiling(Stopwatch.Frequency * (double)GrainSamples / Frequency));
            }

            if (delay > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds((double)delay / Stopwatch.Frequency));
            }
        }
    }

    [SysAbiExport(
        Nid = "g2tViFIohHE",
        ExportName = "sceAudioOut2Initialize",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2Initialize(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "t5YrizufpQc",
        ExportName = "sceAudioOut2ContextResetParam",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextResetParam(CpuContext ctx)
    {
        var paramAddress = ctx[CpuRegister.Rdi];
        if (paramAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> param = stackalloc byte[AudioOut2ContextParamSize];
        param.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(param[0x00..], AudioOut2ContextParamSize);
        BinaryPrimitives.WriteUInt32LittleEndian(param[0x04..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(param[0x08..], 48000);
        BinaryPrimitives.WriteUInt32LittleEndian(param[0x0C..], 0x400);

        return ctx.Memory.TryWrite(paramAddress, param)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "pDmme7Bgm6E",
        ExportName = "sceAudioOut2ContextQueryMemory",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextQueryMemory(CpuContext ctx)
    {
        var paramAddress = ctx[CpuRegister.Rdi];
        var memoryInfoAddress = ResolveGuestOutBuffer(ctx[CpuRegister.Rsi], ctx[CpuRegister.Rdx]);
        if (paramAddress == 0 || memoryInfoAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var contextMemorySize = (ulong)AudioOut2ContextMemorySize;
        Span<byte> param = stackalloc byte[AudioOut2ContextParamSize];
        if (ctx.Memory.TryRead(paramAddress, param))
        {
            var queueDepth = BinaryPrimitives.ReadUInt32LittleEndian(param[0x0C..]);
            if (queueDepth == 0)
            {
                queueDepth = 4;
            }

            contextMemorySize = checked(0x10000UL + (queueDepth * 0x590UL));
        }

        if (IsGuestStackAddress(memoryInfoAddress))
        {
            Span<byte> sizeOnly = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(sizeOnly, contextMemorySize);
            if (!ctx.Memory.TryWrite(memoryInfoAddress, sizeOnly))
            {
                return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            TraceAudioOut2(
                $"context-query-mem stack-size-only size=0x{contextMemorySize:X} out=0x{memoryInfoAddress:X}");
            return SetReturn(ctx, 0);
        }

        Span<byte> memoryInfo = stackalloc byte[0x10];
        memoryInfo.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(memoryInfo[0x00..], contextMemorySize);
        BinaryPrimitives.WriteUInt64LittleEndian(memoryInfo[0x08..], AudioOut2ContextMemoryAlignment);

        if (!ctx.Memory.TryWrite(memoryInfoAddress, memoryInfo))
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TraceAudioOut2(
            $"context-query-mem heap size=0x{contextMemorySize:X} align=0x{AudioOut2ContextMemoryAlignment:X} out=0x{memoryInfoAddress:X}");
        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "0x6o1VVAYSY",
        ExportName = "sceAudioOut2ContextCreate",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextCreate(CpuContext ctx)
    {
        var paramAddress = ctx[CpuRegister.Rdi];
        var memoryAddress = ctx[CpuRegister.Rsi];
        var memorySize = ctx[CpuRegister.Rdx];
        var outContextAddress = ctx[CpuRegister.Rcx];
        if (paramAddress == 0 || memoryAddress == 0 || memorySize == 0 || outContextAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // Read channels/frequency/grain from the reset-param blob so the
        // context can pace advances to the real audio cadence.
        uint channels = 2;
        uint frequency = 48000;
        uint grain = 256;
        Span<byte> param = stackalloc byte[AudioOut2ContextParamSize];
        if (ctx.Memory.TryRead(paramAddress, param))
        {
            var pc = BinaryPrimitives.ReadUInt32LittleEndian(param[0x04..]);
            var pf = BinaryPrimitives.ReadUInt32LittleEndian(param[0x08..]);
            var pg = BinaryPrimitives.ReadUInt32LittleEndian(param[0x0C..]);
            if (pc is > 0 and <= 8) channels = pc;
            if (pf is >= 8000 and <= 192000) frequency = pf;
            // Values below one cache line are flags/counts in observed PS5
            // callers, not audio grains. Keep the hardware-sized default.
            if (pg is >= 64 and <= 0x4000) grain = pg;
            TraceAudioOut2($"context-param address=0x{paramAddress:X} bytes={Convert.ToHexString(param)}");
        }

        var handle = (ulong)Interlocked.Increment(ref _nextContextHandle);
        Contexts[handle] = new ContextState(frequency, channels, grain);
        TraceAudioOut2($"context-create handle=0x{handle:X} frequency={frequency} channels={channels} grain={grain} memory=0x{memoryAddress:X} size=0x{memorySize:X}");
        return TryWriteUInt64(ctx, outContextAddress, handle)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "on6ZH7Abo10",
        ExportName = "sceAudioOut2ContextDestroy",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextDestroy(CpuContext ctx)
    {
        Contexts.TryRemove(ctx[CpuRegister.Rdi], out _);
        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "DxGyV8dtOR8",
        ExportName = "sceAudioOut2ContextBedWrite",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextBedWrite(CpuContext ctx) => SetReturn(ctx, 0);

    [SysAbiExport(
        Nid = "aII9h5nli9U",
        ExportName = "sceAudioOut2ContextPush",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextPush(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        var traceCount = Interlocked.Increment(ref _pushTraceCount);
        if (traceCount <= 16)
        {
            TraceAudioOut2($"context-push count={traceCount} rdi=0x{handle:X} rsi=0x{ctx[CpuRegister.Rsi]:X} rdx=0x{ctx[CpuRegister.Rdx]:X} rcx=0x{ctx[CpuRegister.Rcx]:X}");
        }

        if (Contexts.TryGetValue(handle, out var context))
        {
            // FMOD's PS5 output path uses ContextPush as the submission clock
            // and does not call ContextAdvance. Pace pushes to one hardware
            // grain so the feeder cannot outrun playback and starve the game.
            context.PaceAdvance();
        }

        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "PE2zHMqLSHs",
        ExportName = "sceAudioOut2ContextAdvance",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextAdvance(CpuContext ctx)
    {
        // Advancing renders one grain of audio on hardware; pace it to the same
        // wall-clock cadence so the guest audio thread runs at the right speed.
        if (Contexts.TryGetValue(ctx[CpuRegister.Rdi], out var context))
        {
            context.PaceAdvance();
        }

        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "R7d0F1g2qsU",
        ExportName = "sceAudioOut2ContextGetQueueLevel",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2ContextGetQueueLevel(CpuContext ctx)
    {
        // The advance path paces synchronously, so the queue is always drained.
        var levelAddress = ctx[CpuRegister.Rsi];
        if (levelAddress != 0)
        {
            _ = TryWriteUInt64(ctx, levelAddress, 0);
        }

        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "JK2wamZPzwM",
        ExportName = "sceAudioOut2PortCreate",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2PortCreate(CpuContext ctx)
    {
        var type = unchecked((int)ctx[CpuRegister.Rdi]);
        var paramAddress = ctx[CpuRegister.Rsi];
        var outPortAddress = ctx[CpuRegister.Rdx];
        var contextAddress = ctx[CpuRegister.Rcx];
        if (type < 0 || type > 255 || paramAddress == 0 || outPortAddress == 0 || contextAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var portId = unchecked((uint)Interlocked.Increment(ref _nextPortId)) & 0xFF;
        var handle = 0x2000_0000UL | ((ulong)(uint)type << 16) | portId;
        return TryWriteUInt64(ctx, outPortAddress, handle)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "8XTArSPyWHk",
        ExportName = "sceAudioOut2PortSetAttributes",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2PortSetAttributes(CpuContext ctx) => SetReturn(ctx, 0);

    [SysAbiExport(
        Nid = "gatEUKG+Ea4",
        ExportName = "sceAudioOut2PortGetState",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2PortGetState(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        var stateAddress = ResolveGuestOutBuffer(ctx[CpuRegister.Rsi], ctx[CpuRegister.Rdx]);
        if (handle == 0 || stateAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var type = (int)((handle >> 16) & 0xFF);
        Span<byte> state = stackalloc byte[0x20];
        state.Clear();
        var output = type == 2 ? 0x40 : 0x01;
        var channels = type == 2 ? 1 : 2;
        BinaryPrimitives.WriteUInt16LittleEndian(state[0x00..], unchecked((ushort)output));
        state[0x02] = unchecked((byte)channels);
        BinaryPrimitives.WriteInt16LittleEndian(state[0x04..], -1);

        return ctx.Memory.TryWrite(stateAddress, state)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "DImz2Ft9E2g",
        ExportName = "sceAudioOut2GetSpeakerInfo",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2GetSpeakerInfo(CpuContext ctx)
    {
        var infoAddress = ResolveGuestOutBuffer(ctx[CpuRegister.Rdi], ctx[CpuRegister.Rdx]);
        if (infoAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> info = stackalloc byte[0x20];
        info.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(info[0x00..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(info[0x04..], 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(info[0x08..], 0x01);

        return ctx.Memory.TryWrite(infoAddress, info)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "cd+Rtw+D1x8",
        ExportName = "sceAudioOut2PortDestroy",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2PortDestroy(CpuContext ctx) => SetReturn(ctx, 0);

    [SysAbiExport(
        Nid = "IaZXJ9M79uo",
        ExportName = "sceAudioOut2UserDestroy",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2UserDestroy(CpuContext ctx) => SetReturn(ctx, 0);

    [SysAbiExport(
        Nid = "4BlZurolOAo",
        ExportName = "sceAudioOut2GetSpeakerArrayCoefficients",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2GetSpeakerArrayCoefficients(CpuContext ctx) =>
        WriteZeroSpeakerArrayCoefficients(ctx, "coefficients");

    [SysAbiExport(
        Nid = "28QqMnuuJ9Y",
        ExportName = "sceAudioOut2GetSpeakerArrayAmbisonicsCoefficients",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2GetSpeakerArrayAmbisonicsCoefficients(CpuContext ctx) =>
        WriteZeroSpeakerArrayCoefficients(ctx, "ambisonics-coefficients");

    [SysAbiExport(
        Nid = "G1YOKDJYX2Y",
        ExportName = "sceAudioOut2GetSpeakerArrayMemorySize",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2GetSpeakerArrayMemorySize(CpuContext ctx)
    {
        var numChannels = (uint)ctx[CpuRegister.Rdi];
        if (numChannels == 0 || numChannels > SpeakerArrayMaxChannels)
        {
            numChannels = SpeakerArrayDefaultChannels;
        }

        var size = ComputeSpeakerArrayBytes(numChannels);
        TraceAudioOut2($"speaker-array-get-size rdi=0x{ctx[CpuRegister.Rdi]:X} -> 0x{size:X}");
        ctx[CpuRegister.Rax] = unchecked((ulong)size);
        return size;
    }

    [SysAbiExport(
        Nid = "+k91hoTuoA8",
        ExportName = "sceAudioOut2SpeakerArrayCreate",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2SpeakerArrayCreate(CpuContext ctx)
    {
        var param = ctx[CpuRegister.Rdi];
        var outHandleAddress = ctx[CpuRegister.Rsi];
        var outReservedAddress = ctx[CpuRegister.Rdx];
        var channels = (uint)ctx[CpuRegister.Rcx];
        if (channels == 0 || channels > SpeakerArrayMaxChannels)
        {
            channels = SpeakerArrayDefaultChannels;
        }

        if (outHandleAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var bytes = ComputeSpeakerArrayBytes(channels);
        if (!TryAllocateSpeakerArrayMemory(ctx, (ulong)bytes, out var memory) ||
            !InitializeSpeakerArrayObject(ctx, memory, channels))
        {
            Console.Error.WriteLine(
                $"[LOADER][ERROR] audio_out2.speaker-array-create alloc-failed bytes=0x{bytes:X} channels={channels}");
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        SpeakerArrays[memory] = 0;
        if (!TryWriteUInt64(ctx, outHandleAddress, memory))
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TraceAudioOut2(
            $"speaker-array-create object=0x{memory:X} bytes=0x{bytes:X} channels={channels} param=0x{param:X} out=0x{outHandleAddress:X}");

        ctx[CpuRegister.Rax] = memory;
        return 0;
    }

    [SysAbiExport(
        Nid = "erCWQR5eKiQ",
        ExportName = "sceAudioOut2SpeakerArrayDestroy",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2SpeakerArrayDestroy(CpuContext ctx)
    {
        SpeakerArrays.TryRemove(ctx[CpuRegister.Rdi], out _);
        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "xywYcRB7nbQ",
        ExportName = "sceAudioOut2UserCreate",
        Target = Generation.Gen5,
        LibraryName = "libSceAudioOut2")]
    public static int AudioOut2UserCreate(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var outUserAddress = ctx[CpuRegister.Rsi];
        if ((userId != 0 && userId != 1 && userId != 1000 && userId != 0x10000000 && userId != 255) ||
            outUserAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        var handle = (ulong)Interlocked.Increment(ref _nextUserHandle);
        return TryWriteUInt64(ctx, outUserAddress, handle)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    private const int SpeakerArrayHeaderSize = 0x40;
    private const int SpeakerArrayEntrySize = 0x100;
    private const int SpeakerArrayScratchBytes = 0x400;
    private const uint SpeakerArrayDefaultChannels = 8;
    private const uint SpeakerArrayMaxChannels = 32;
    private const int SpeakerArrayDivisorFieldOffset = 0x34;
    private const int SpeakerArrayResultFieldOffset = 0x3C;
    private const uint SpeakerArrayDefaultDivisor = 1;
    private const int SpeakerArrayCoefficientBytes = 0x400;

    private static readonly ConcurrentDictionary<ulong, byte> SpeakerArrays = new();

    private static int ComputeSpeakerArrayBytes(uint channels) =>
        SpeakerArrayHeaderSize + (int)(channels * SpeakerArrayEntrySize) + SpeakerArrayScratchBytes;

    private static bool InitializeSpeakerArrayObject(CpuContext ctx, ulong memory, uint channels)
    {
        Span<byte> body = stackalloc byte[SpeakerArrayHeaderSize];
        body.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x00..], (uint)SpeakerArrayHeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(body[0x04..], channels);
        BinaryPrimitives.WriteUInt32LittleEndian(body[SpeakerArrayDivisorFieldOffset..], SpeakerArrayDefaultDivisor);
        BinaryPrimitives.WriteUInt32LittleEndian(body[SpeakerArrayResultFieldOffset..], 0);
        return ctx.Memory.TryWrite(memory, body);
    }

    private static bool TryAllocateSpeakerArrayMemory(CpuContext ctx, ulong bytes, out ulong memory)
    {
        memory = 0;
        var length = Math.Max(bytes, 0x1000UL);

        if (TryAllocateViaGuestAllocator(ctx, length, 0x1000, out memory) &&
            IsSafeSpeakerArrayAddress(memory))
        {
            return true;
        }

        if (Kernel.KernelMemoryCompatExports.TryAllocateHleData(ctx, length, 0x1000, out memory) &&
            IsSafeSpeakerArrayAddress(memory))
        {
            return true;
        }

        memory = 0;
        return false;
    }

    private static bool TryAllocateViaGuestAllocator(CpuContext ctx, ulong length, ulong alignment, out ulong memory)
    {
        memory = 0;
        var allocator = ctx.Memory as IGuestMemoryAllocator;
        if (allocator is null && ctx.Memory is ICpuMemoryWrapper { Inner: IGuestMemoryAllocator inner })
        {
            allocator = inner;
        }

        return allocator is not null && allocator.TryAllocateGuestMemory(length, alignment, out memory);
    }

    private static bool IsSafeSpeakerArrayAddress(ulong value) =>
        IsPlausibleGuestObjectPointer(value) &&
        !IsGuestStackAddress(value) &&
        !IsDirectMemoryWindowAddress(value);

    private static bool IsDirectMemoryWindowAddress(ulong value) =>
        value >= 0x0000_1400_0000_0000UL && value < 0x0000_1800_0000_0000UL;

    private static bool IsPlausibleGuestObjectPointer(ulong value) =>
        value >= 0x1000_0000UL &&
        value != 0x10000UL &&
        value < 0x0000_8000_0000_0000UL;

    private static bool IsGuestStackAddress(ulong value) =>
        value >= 0x0000_7FF0_0000_0000UL && value <= 0x0000_7FFF_FFFF_FFFFUL;

    private static ulong ResolveGuestOutBuffer(ulong primary, ulong secondary)
    {
        if (IsWritableOutBuffer(primary))
        {
            return primary;
        }

        if (IsWritableOutBuffer(secondary))
        {
            return secondary;
        }

        return 0;
    }

    private static bool IsWritableOutBuffer(ulong value) =>
        value != 0 &&
        value != 0x10000UL &&
        value >= 0x1000UL &&
        (IsPlausibleGuestObjectPointer(value) || IsGuestStackAddress(value));

    private static int WriteZeroSpeakerArrayCoefficients(CpuContext ctx, string label)
    {
        var destination = ctx[CpuRegister.Rsi];
        if (destination == 0)
        {
            destination = ctx[CpuRegister.Rdx];
        }

        if (destination != 0 &&
            IsPlausibleGuestObjectPointer(destination) &&
            !IsGuestStackAddress(destination))
        {
            Span<byte> zeros = stackalloc byte[SpeakerArrayCoefficientBytes];
            zeros.Clear();
            if (!ctx.Memory.TryWrite(destination, zeros))
            {
                TraceAudioOut2($"{label} write-failed dest=0x{destination:X}");
                return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }

        TraceAudioOut2($"{label} ok dest=0x{destination:X}");
        return SetReturn(ctx, 0);
    }

    private static bool TryWriteUInt64(CpuContext ctx, ulong address, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        return ctx.Memory.TryWrite(address, buffer);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static void TraceAudioOut2(string message)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_AUDIO_OUT2") ?? Environment.GetEnvironmentVariable("SHARPEMU_LOG_AUDIO_OUT2"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[LOADER][TRACE] audio_out2.{message}");
        }
    }
}
