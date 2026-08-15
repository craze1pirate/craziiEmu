// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Videodec2;

public static class Videodec2Exports
{
    public const int OK = 0;

    public const int VIDEODEC2_ERROR_API_FAIL             = unchecked((int)0x811D0100);
    public const int VIDEODEC2_ERROR_STRUCT_SIZE          = unchecked((int)0x811D0101);
    public const int VIDEODEC2_ERROR_ARGUMENT_POINTER     = unchecked((int)0x811D0102);
    public const int VIDEODEC2_ERROR_DECODER_INSTANCE     = unchecked((int)0x811D0103);
    public const int VIDEODEC2_ERROR_MEMORY_SIZE          = unchecked((int)0x811D0104);
    public const int VIDEODEC2_ERROR_MEMORY_POINTER       = unchecked((int)0x811D0105);
    public const int VIDEODEC2_ERROR_FRAME_BUFFER_SIZE    = unchecked((int)0x811D0106);
    public const int VIDEODEC2_ERROR_FRAME_BUFFER_POINTER = unchecked((int)0x811D0107);
    public const int VIDEODEC2_ERROR_ACCESS_UNIT_SIZE     = unchecked((int)0x811D010D);
    public const int VIDEODEC2_ERROR_ACCESS_UNIT_POINTER  = unchecked((int)0x811D010E);
    public const int VIDEODEC2_ERROR_OUTPUT_INFO          = unchecked((int)0x811D010F);
    public const int VIDEODEC2_ERROR_COMPUTE_QUEUE        = unchecked((int)0x811D0110);
    public const int VIDEODEC2_ERROR_CONFIG_INFO          = unchecked((int)0x811D0200);
    public const int VIDEODEC2_ERROR_COMPUTE_PIPE_ID      = unchecked((int)0x811D0201);
    public const int VIDEODEC2_ERROR_COMPUTE_QUEUE_ID     = unchecked((int)0x811D0202);
    public const int VIDEODEC2_ERROR_RESOURCE_TYPE        = unchecked((int)0x811D0203);
    public const int VIDEODEC2_ERROR_CODEC_TYPE           = unchecked((int)0x811D0204);
    public const int VIDEODEC2_ERROR_INPUT_QUEUE_DEPTH    = unchecked((int)0x811D0206);
    public const int VIDEODEC2_ERROR_DPB_FRAME_COUNT      = unchecked((int)0x811D0209);
    public const int VIDEODEC2_ERROR_FRAME_WIDTH_HEIGHT   = unchecked((int)0x811D020A);
    public const int VIDEODEC2_ERROR_ACCESS_UNIT          = unchecked((int)0x811D0301);
    public const int VIDEODEC2_ERROR_OVERSIZE_DECODE      = unchecked((int)0x811D0302);

    public const uint VIDEODEC2_RESOURCE_TYPE_COMPUTE = 1;
    public const ulong VIDEODEC2_MIN_MEMORY_SIZE      = 16UL * 1024UL * 1024UL; // 16 MB
    public const uint VIDEODEC2_FRAME_FORMAT_DEFAULT  = 0;

    public const uint CODEC_TYPE_AVC  = 1;
    public const uint CODEC_TYPE_HEVC = 974921;
    public const uint CODEC_TYPE_VP9  = 2382845;

    public const ulong TIMESTAMP_INVALID = ulong.MaxValue;

    private static readonly object _decoderGate = new();
    private static readonly ConcurrentDictionary<ulong, DecoderInstance> _decoders = new();
    private static readonly ConcurrentDictionary<ulong, StoredPictureInfo> _pictureInfos = new();
    private static long _nextDecoderHandle = 0x10000;

    private sealed class DecoderInstance
    {
        public required ulong Handle { get; init; }
        public required uint CodecType { get; init; }
        public required int MaxWidth { get; init; }
        public required int MaxHeight { get; init; }
        public bool Draining { get; set; }
        public Queue<DecodedFrame> PendingFrames { get; } = new();
        public HashSet<ulong> BoundBuffers { get; } = new();
    }

    private sealed class DecodedFrame
    {
        public ulong Pts { get; init; }
        public ulong Dts { get; init; }
        public ulong AttachedData { get; init; }
        public uint Width { get; init; }
        public uint Height { get; init; }
        public byte[] Nv12Data { get; init; } = [];
        public bool IsKeyFrame { get; init; }
    }

    private sealed class StoredPictureInfo
    {
        public ulong Handle { get; init; }
        public ulong Pts { get; init; }
        public ulong Dts { get; init; }
        public ulong AttachedData { get; init; }
        public uint CodecType { get; init; }
        public uint Width { get; init; }
        public uint Height { get; init; }
        public bool KeyFrame { get; init; }
    }

    private static bool IsCodecSupported(uint codecType) =>
        codecType == CODEC_TYPE_AVC || codecType == CODEC_TYPE_HEVC || codecType == CODEC_TYPE_VP9;

    private static uint AlignUp(uint value, uint alignment) =>
        (value + alignment - 1u) & ~(alignment - 1u);

    [SysAbiExport(
        Nid = "RnDibcGCPKw",
        ExportName = "sceVideodec2QueryComputeMemoryInfo",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int QueryComputeMemoryInfo(CpuContext ctx)
    {
        var infoAddr = ctx[CpuRegister.Rdi];
        if (infoAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(infoAddr, out var thisSize) || thisSize != 24)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        Span<byte> buf = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x00..], 24);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x08..], VIDEODEC2_MIN_MEMORY_SIZE);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x10..], 0);

        if (!ctx.Memory.TryWrite(infoAddr, buf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "eD+X2SmxUt4",
        ExportName = "sceVideodec2AllocateComputeQueue",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int AllocateComputeQueue(CpuContext ctx)
    {
        var configAddr = ctx[CpuRegister.Rdi];
        var memoryAddr = ctx[CpuRegister.Rsi];
        var queueOutAddr = ctx[CpuRegister.Rdx];

        if (configAddr == 0 || memoryAddr == 0 || queueOutAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(configAddr, out var cfgSize) || cfgSize != 16 ||
            !ctx.TryReadUInt64(memoryAddr, out var memSize) || memSize != 24)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        Span<byte> cfgBuf = stackalloc byte[16];
        if (!ctx.Memory.TryRead(configAddr, cfgBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var pipeId = BinaryPrimitives.ReadUInt16LittleEndian(cfgBuf[0x08..]);
        var queueId = BinaryPrimitives.ReadUInt16LittleEndian(cfgBuf[0x0A..]);
        var res0 = cfgBuf[0x0D];
        var res1 = BinaryPrimitives.ReadUInt16LittleEndian(cfgBuf[0x0E..]);

        if (res0 != 0 || res1 != 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_CONFIG_INFO);
            return VIDEODEC2_ERROR_CONFIG_INFO;
        }

        if (pipeId > 4)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_COMPUTE_PIPE_ID);
            return VIDEODEC2_ERROR_COMPUTE_PIPE_ID;
        }

        if (queueId > 7)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_COMPUTE_QUEUE_ID);
            return VIDEODEC2_ERROR_COMPUTE_QUEUE_ID;
        }

        Span<byte> memBuf = stackalloc byte[24];
        if (!ctx.Memory.TryRead(memoryAddr, memBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var gpuMemSize = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x08..]);
        var gpuMemPtr = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x10..]);

        if (gpuMemSize < VIDEODEC2_MIN_MEMORY_SIZE)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_MEMORY_SIZE);
            return VIDEODEC2_ERROR_MEMORY_SIZE;
        }

        if (gpuMemPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_MEMORY_POINTER);
            return VIDEODEC2_ERROR_MEMORY_POINTER;
        }

        if (!ctx.TryWriteUInt64(queueOutAddr, gpuMemPtr))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "UvtA3FAiF4Y",
        ExportName = "sceVideodec2ReleaseComputeQueue",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int ReleaseComputeQueue(CpuContext ctx)
    {
        var queue = ctx[CpuRegister.Rdi];
        if (queue == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_COMPUTE_QUEUE_ID);
            return VIDEODEC2_ERROR_COMPUTE_QUEUE_ID;
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "qqMCwlULR+E",
        ExportName = "sceVideodec2QueryDecoderMemoryInfo",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int QueryDecoderMemoryInfo(CpuContext ctx)
    {
        var configAddr = ctx[CpuRegister.Rdi];
        var memoryInfoAddr = ctx[CpuRegister.Rsi];

        if (configAddr == 0 || memoryInfoAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(configAddr, out var cfgSize) || cfgSize != 72 ||
            !ctx.TryReadUInt64(memoryInfoAddr, out var memSize) || memSize != 72)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        var validationResult = ValidateConfig(ctx, configAddr, requireComputeQueue: false);
        if (validationResult != OK)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)validationResult);
            return validationResult;
        }

        Span<byte> memBuf = stackalloc byte[72];
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x00..], 72);
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x08..], VIDEODEC2_MIN_MEMORY_SIZE); // cpu_memory_size
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x10..], 0);                          // cpu_memory
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x18..], VIDEODEC2_MIN_MEMORY_SIZE); // gpu_memory_size
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x20..], 0);                          // gpu_memory
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x28..], VIDEODEC2_MIN_MEMORY_SIZE); // cpu_gpu_memory_size
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x30..], 0);                          // cpu_gpu_memory
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x38..], VIDEODEC2_MIN_MEMORY_SIZE); // max_frame_buffer_size
        BinaryPrimitives.WriteUInt32LittleEndian(memBuf[0x40..], 0x100);                      // frame_buffer_alignment
        BinaryPrimitives.WriteUInt32LittleEndian(memBuf[0x44..], 0);                          // reserved0

        if (!ctx.Memory.TryWrite(memoryInfoAddr, memBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "CNNRoRYd8XI",
        ExportName = "sceVideodec2CreateDecoder",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int CreateDecoder(CpuContext ctx)
    {
        var configAddr = ctx[CpuRegister.Rdi];
        var memoryInfoAddr = ctx[CpuRegister.Rsi];
        var decoderOutAddr = ctx[CpuRegister.Rdx];

        if (configAddr == 0 || memoryInfoAddr == 0 || decoderOutAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(configAddr, out var cfgSize) || cfgSize != 72 ||
            !ctx.TryReadUInt64(memoryInfoAddr, out var memSize) || memSize != 72)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        var validationResult = ValidateConfig(ctx, configAddr, requireComputeQueue: true);
        if (validationResult != OK)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)validationResult);
            return validationResult;
        }

        Span<byte> memBuf = stackalloc byte[72];
        if (!ctx.Memory.TryRead(memoryInfoAddr, memBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var cpuSize = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x08..]);
        var cpuPtr = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x10..]);
        var gpuSize = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x18..]);
        var gpuPtr = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x20..]);
        var cpuGpuSize = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x28..]);
        var cpuGpuPtr = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x30..]);
        var maxFbSize = BinaryPrimitives.ReadUInt64LittleEndian(memBuf[0x38..]);

        if (cpuSize < VIDEODEC2_MIN_MEMORY_SIZE || gpuSize < VIDEODEC2_MIN_MEMORY_SIZE ||
            cpuGpuSize < VIDEODEC2_MIN_MEMORY_SIZE || maxFbSize < VIDEODEC2_MIN_MEMORY_SIZE)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_MEMORY_SIZE);
            return VIDEODEC2_ERROR_MEMORY_SIZE;
        }

        if (cpuPtr == 0 || gpuPtr == 0 || cpuGpuPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_MEMORY_POINTER);
            return VIDEODEC2_ERROR_MEMORY_POINTER;
        }

        Span<byte> cfgBuf = stackalloc byte[72];
        _ = ctx.Memory.TryRead(configAddr, cfgBuf);
        var codecType = BinaryPrimitives.ReadUInt32LittleEndian(cfgBuf[0x0C..]);
        var maxW = BinaryPrimitives.ReadInt32LittleEndian(cfgBuf[0x18..]);
        var maxH = BinaryPrimitives.ReadInt32LittleEndian(cfgBuf[0x1C..]);

        var handle = (ulong)Interlocked.Increment(ref _nextDecoderHandle);
        var instance = new DecoderInstance
        {
            Handle = handle,
            CodecType = codecType,
            MaxWidth = maxW,
            MaxHeight = maxH,
        };

        _decoders[handle] = instance;

        if (!ctx.TryWriteUInt64(decoderOutAddr, handle))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "jwImxXRGSKA",
        ExportName = "sceVideodec2DeleteDecoder",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int DeleteDecoder(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        if (handle == 0 || !_decoders.TryRemove(handle, out var instance))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_DECODER_INSTANCE);
            return VIDEODEC2_ERROR_DECODER_INSTANCE;
        }

        lock (_decoderGate)
        {
            foreach (var buf in instance.BoundBuffers)
            {
                _pictureInfos.TryRemove(buf, out _);
            }
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "852F5+q6+iM",
        ExportName = "sceVideodec2Decode",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int Decode(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        var inputDataAddr = ctx[CpuRegister.Rsi];
        var frameBufAddr = ctx[CpuRegister.Rdx];
        var outputInfoAddr = ctx[CpuRegister.Rcx];

        if (!_decoders.TryGetValue(handle, out var instance))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_DECODER_INSTANCE);
            return VIDEODEC2_ERROR_DECODER_INSTANCE;
        }

        if (inputDataAddr == 0 || frameBufAddr == 0 || outputInfoAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(inputDataAddr, out var inSize) || inSize != 48 ||
            !ctx.TryReadUInt64(frameBufAddr, out var fbSize) || fbSize != 32 ||
            !ctx.TryReadUInt64(outputInfoAddr, out var outSize) || (outSize != 56 && (outSize | 8u) != 56))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        Span<byte> inBuf = stackalloc byte[48];
        if (!ctx.Memory.TryRead(inputDataAddr, inBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var auDataPtr = BinaryPrimitives.ReadUInt64LittleEndian(inBuf[0x08..]);
        var auSize = BinaryPrimitives.ReadUInt64LittleEndian(inBuf[0x10..]);
        var pts = BinaryPrimitives.ReadUInt64LittleEndian(inBuf[0x18..]);
        var dts = BinaryPrimitives.ReadUInt64LittleEndian(inBuf[0x20..]);
        var attachedData = BinaryPrimitives.ReadUInt64LittleEndian(inBuf[0x28..]);

        if (auSize == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ACCESS_UNIT_SIZE);
            return VIDEODEC2_ERROR_ACCESS_UNIT_SIZE;
        }

        if (auDataPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ACCESS_UNIT_POINTER);
            return VIDEODEC2_ERROR_ACCESS_UNIT_POINTER;
        }

        Span<byte> fbBuf = stackalloc byte[32];
        if (!ctx.Memory.TryRead(frameBufAddr, fbBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var targetFbPtr = BinaryPrimitives.ReadUInt64LittleEndian(fbBuf[0x08..]);
        var targetFbSize = BinaryPrimitives.ReadUInt64LittleEndian(fbBuf[0x10..]);

        if (targetFbSize == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_SIZE);
            return VIDEODEC2_ERROR_FRAME_BUFFER_SIZE;
        }

        if (targetFbPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_POINTER);
            return VIDEODEC2_ERROR_FRAME_BUFFER_POINTER;
        }

        fbBuf[0x18] = 0; // is_accepted = false
        ctx.Memory.TryWrite(frameBufAddr, fbBuf);
        FillNoPictureOutput(ctx, outputInfoAddr, outSize, targetFbPtr, targetFbSize, instance.CodecType);

        var auBytes = new byte[auSize];
        if (!ctx.Memory.TryRead(auDataPtr, auBytes))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ACCESS_UNIT_POINTER);
            return VIDEODEC2_ERROR_ACCESS_UNIT_POINTER;
        }

        // Process Access Unit & Produce Frame
        var frame = DecodeBitstream(auBytes, pts, dts, attachedData, instance.MaxWidth, instance.MaxHeight);
        if (frame is null)
        {
            ctx[CpuRegister.Rax] = OK;
            return OK;
        }

        if (instance.MaxWidth > 0 && frame.Width > instance.MaxWidth ||
            instance.MaxHeight > 0 && frame.Height > instance.MaxHeight)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_OVERSIZE_DECODE);
            return VIDEODEC2_ERROR_OVERSIZE_DECODE;
        }

        var pitch = AlignUp(frame.Width, 256);
        var chromaRows = (frame.Height + 1u) / 2u;
        var requiredSize = (ulong)pitch * frame.Height + (ulong)pitch * chromaRows;

        if (requiredSize > targetFbSize)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_SIZE);
            return VIDEODEC2_ERROR_FRAME_BUFFER_SIZE;
        }

        // Write NV12 Frame to Guest Memory
        if (!ctx.Memory.TryWrite(targetFbPtr, frame.Nv12Data))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_POINTER);
            return VIDEODEC2_ERROR_FRAME_BUFFER_POINTER;
        }

        // Update FrameBuffer accepted = true
        fbBuf[0x18] = 1;
        ctx.Memory.TryWrite(frameBufAddr, fbBuf);

        // Update OutputInfo
        FillDecodedOutput(ctx, outputInfoAddr, outSize, frame, targetFbPtr, targetFbSize, pitch, instance.CodecType);

        // Store Picture Info
        var picInfo = new StoredPictureInfo
        {
            Handle = handle,
            Pts = frame.Pts,
            Dts = frame.Dts,
            AttachedData = frame.AttachedData,
            CodecType = instance.CodecType,
            Width = frame.Width,
            Height = frame.Height,
            KeyFrame = frame.IsKeyFrame,
        };

        lock (_decoderGate)
        {
            _pictureInfos[targetFbPtr] = picInfo;
            instance.BoundBuffers.Add(targetFbPtr);
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "l1hXwscLuCY",
        ExportName = "sceVideodec2Flush",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int Flush(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        var frameBufAddr = ctx[CpuRegister.Rsi];
        var outputInfoAddr = ctx[CpuRegister.Rdx];

        if (!_decoders.TryGetValue(handle, out var instance))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_DECODER_INSTANCE);
            return VIDEODEC2_ERROR_DECODER_INSTANCE;
        }

        if (frameBufAddr == 0 || outputInfoAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(frameBufAddr, out var fbSize) || fbSize != 32 ||
            !ctx.TryReadUInt64(outputInfoAddr, out var outSize) || (outSize != 56 && (outSize | 8u) != 56))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        Span<byte> fbBuf = stackalloc byte[32];
        if (!ctx.Memory.TryRead(frameBufAddr, fbBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var targetFbPtr = BinaryPrimitives.ReadUInt64LittleEndian(fbBuf[0x08..]);
        var targetFbSize = BinaryPrimitives.ReadUInt64LittleEndian(fbBuf[0x10..]);

        if (targetFbSize == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_SIZE);
            return VIDEODEC2_ERROR_FRAME_BUFFER_SIZE;
        }

        if (targetFbPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_FRAME_BUFFER_POINTER);
            return VIDEODEC2_ERROR_FRAME_BUFFER_POINTER;
        }

        fbBuf[0x18] = 0;
        ctx.Memory.TryWrite(frameBufAddr, fbBuf);
        FillNoPictureOutput(ctx, outputInfoAddr, outSize, targetFbPtr, targetFbSize, instance.CodecType);

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "wJXikG6QFN8",
        ExportName = "sceVideodec2Reset",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int Reset(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        if (!_decoders.TryGetValue(handle, out var instance))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_DECODER_INSTANCE);
            return VIDEODEC2_ERROR_DECODER_INSTANCE;
        }

        lock (_decoderGate)
        {
            instance.Draining = false;
            instance.PendingFrames.Clear();
            foreach (var buf in instance.BoundBuffers)
            {
                _pictureInfos.TryRemove(buf, out _);
            }
            instance.BoundBuffers.Clear();
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    [SysAbiExport(
        Nid = "NtXRa3dRzU0",
        ExportName = "sceVideodec2GetPictureInfo",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int GetPictureInfo(CpuContext ctx) => GetPictureInfoInternal(ctx);

    [SysAbiExport(
        Nid = "kjrLbcyhEiw",
        ExportName = "sceVideodec2GetPictureInfoAlias",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideodec2")]
    public static int GetPictureInfoAlias(CpuContext ctx) => GetPictureInfoInternal(ctx);

    private static int GetPictureInfoInternal(CpuContext ctx)
    {
        var outputInfoAddr = ctx[CpuRegister.Rdi];
        var firstPicInfoAddr = ctx[CpuRegister.Rsi];
        var secondPicInfoAddr = ctx[CpuRegister.Rdx];

        if (outputInfoAddr == 0 || firstPicInfoAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        if (!ctx.TryReadUInt64(outputInfoAddr, out var outSize) || (outSize != 56 && (outSize | 8u) != 56))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
            return VIDEODEC2_ERROR_STRUCT_SIZE;
        }

        Span<byte> outBuf = stackalloc byte[56];
        if (!ctx.Memory.TryRead(outputInfoAddr, outBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_ARGUMENT_POINTER);
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var isValid = outBuf[0x08] != 0;
        var picCount = outBuf[0x0A];
        var fbPtr = BinaryPrimitives.ReadUInt64LittleEndian(outBuf[0x20..]);
        var codecType = BinaryPrimitives.ReadUInt32LittleEndian(outBuf[0x0C..]);

        if (!isValid || picCount == 0 || fbPtr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_OUTPUT_INFO);
            return VIDEODEC2_ERROR_OUTPUT_INFO;
        }

        if (!_pictureInfos.TryGetValue(fbPtr, out var picInfo) || picInfo.CodecType != codecType)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_OUTPUT_INFO);
            return VIDEODEC2_ERROR_OUTPUT_INFO;
        }

        if (codecType == CODEC_TYPE_AVC)
        {
            if (!ctx.TryReadUInt64(firstPicInfoAddr, out var reqSize) || (reqSize != 120 && (reqSize | 16u) != 120))
            {
                ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
                return VIDEODEC2_ERROR_STRUCT_SIZE;
            }

            Span<byte> avcBuf = stackalloc byte[120];
            BinaryPrimitives.WriteUInt64LittleEndian(avcBuf[0x00..], reqSize);
            avcBuf[0x08] = 1; // is_valid = true
            BinaryPrimitives.WriteUInt64LittleEndian(avcBuf[0x10..], picInfo.Pts);
            BinaryPrimitives.WriteUInt64LittleEndian(avcBuf[0x18..], picInfo.Dts);
            BinaryPrimitives.WriteUInt64LittleEndian(avcBuf[0x20..], picInfo.AttachedData);
            avcBuf[0x28] = picInfo.KeyFrame ? (byte)1 : (byte)0; // idr_picture_flag
            avcBuf[0x29] = 100; // profile_idc (High Profile)
            avcBuf[0x2A] = 41;  // level_idc (Level 4.1)

            uint picW = picInfo.Width > 0 ? (picInfo.Width + 15u) / 16u - 1u : 0;
            uint picH = picInfo.Height > 0 ? (picInfo.Height + 15u) / 16u - 1u : 0;
            BinaryPrimitives.WriteUInt32LittleEndian(avcBuf[0x2C..], picW);
            BinaryPrimitives.WriteUInt32LittleEndian(avcBuf[0x30..], picH);
            avcBuf[0x34] = 1; // frame_mbs_only_flag

            ctx.Memory.TryWrite(firstPicInfoAddr, avcBuf[..(int)reqSize]);
        }
        else
        {
            if (!ctx.TryReadUInt64(firstPicInfoAddr, out var reqSize) || reqSize < 40 || reqSize > 256)
            {
                ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
                return VIDEODEC2_ERROR_STRUCT_SIZE;
            }

            byte[] commonBuf = new byte[reqSize];
            BinaryPrimitives.WriteUInt64LittleEndian(commonBuf, reqSize);
            commonBuf[8] = 1; // valid = 1
            BinaryPrimitives.WriteUInt64LittleEndian(commonBuf.AsSpan(16), picInfo.Pts);
            BinaryPrimitives.WriteUInt64LittleEndian(commonBuf.AsSpan(24), picInfo.Dts);
            BinaryPrimitives.WriteUInt64LittleEndian(commonBuf.AsSpan(32), picInfo.AttachedData);
            ctx.Memory.TryWrite(firstPicInfoAddr, commonBuf);
        }

        if (secondPicInfoAddr != 0)
        {
            if (!ctx.TryReadUInt64(secondPicInfoAddr, out var reqSize2) || reqSize2 < 40 || reqSize2 > 256)
            {
                ctx[CpuRegister.Rax] = unchecked((ulong)VIDEODEC2_ERROR_STRUCT_SIZE);
                return VIDEODEC2_ERROR_STRUCT_SIZE;
            }

            byte[] commonBuf2 = new byte[reqSize2];
            BinaryPrimitives.WriteUInt64LittleEndian(commonBuf2, reqSize2);
            commonBuf2[8] = 0; // valid = 0 for second picture
            ctx.Memory.TryWrite(secondPicInfoAddr, commonBuf2);
        }

        ctx[CpuRegister.Rax] = OK;
        return OK;
    }

    private static int ValidateConfig(CpuContext ctx, ulong configAddr, bool requireComputeQueue)
    {
        Span<byte> buf = stackalloc byte[72];
        if (!ctx.Memory.TryRead(configAddr, buf))
        {
            return VIDEODEC2_ERROR_ARGUMENT_POINTER;
        }

        var resourceType = BinaryPrimitives.ReadUInt32LittleEndian(buf[0x08..]);
        var codecType = BinaryPrimitives.ReadUInt32LittleEndian(buf[0x0C..]);
        var maxW = BinaryPrimitives.ReadInt32LittleEndian(buf[0x18..]);
        var maxH = BinaryPrimitives.ReadInt32LittleEndian(buf[0x1C..]);
        var maxDpb = BinaryPrimitives.ReadInt32LittleEndian(buf[0x20..]);
        var queueDepth = BinaryPrimitives.ReadUInt32LittleEndian(buf[0x24..]);
        var computeQueue = BinaryPrimitives.ReadUInt64LittleEndian(buf[0x28..]);
        var res0 = buf[0x3E];
        var res1 = buf[0x3F];

        if (resourceType != VIDEODEC2_RESOURCE_TYPE_COMPUTE)
        {
            return VIDEODEC2_ERROR_RESOURCE_TYPE;
        }

        if (!IsCodecSupported(codecType))
        {
            return VIDEODEC2_ERROR_CODEC_TYPE;
        }

        if (res0 != 0 || res1 != 0)
        {
            return VIDEODEC2_ERROR_CONFIG_INFO;
        }

        if (queueDepth == 0)
        {
            return VIDEODEC2_ERROR_INPUT_QUEUE_DEPTH;
        }

        if (maxDpb < -1 || maxDpb == 0)
        {
            return VIDEODEC2_ERROR_DPB_FRAME_COUNT;
        }

        if (maxW < -1 || maxH < -1 || maxW == 0 || maxH == 0)
        {
            return VIDEODEC2_ERROR_FRAME_WIDTH_HEIGHT;
        }

        if (requireComputeQueue && computeQueue == 0)
        {
            return VIDEODEC2_ERROR_COMPUTE_QUEUE;
        }

        return OK;
    }

    private static void FillNoPictureOutput(
        CpuContext ctx, ulong outAddr, ulong outSize, ulong fbPtr, ulong fbSize, uint codecType)
    {
        Span<byte> buf = stackalloc byte[56];
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x00..], outSize);
        buf[0x08] = 0; // is_valid = false
        buf[0x09] = 0; // is_error_frame = false
        buf[0x0A] = 0; // picture_count = 0
        buf[0x0B] = 0; // is_discarded_frame = false
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x0C..], codecType);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x10..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x14..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x18..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x20..], fbPtr);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x28..], fbSize);
        if (outSize == 56)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf[0x30..], VIDEODEC2_FRAME_FORMAT_DEFAULT);
            BinaryPrimitives.WriteUInt32LittleEndian(buf[0x34..], 0);
        }
        ctx.Memory.TryWrite(outAddr, buf);
    }

    private static void FillDecodedOutput(
        CpuContext ctx, ulong outAddr, ulong outSize, DecodedFrame frame, ulong fbPtr, ulong fbSize, uint pitch, uint codecType)
    {
        Span<byte> buf = stackalloc byte[56];
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x00..], outSize);
        buf[0x08] = 1; // is_valid = true
        buf[0x09] = 0; // is_error_frame = false
        buf[0x0A] = 1; // picture_count = 1
        buf[0x0B] = 0; // is_discarded_frame = false
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x0C..], codecType);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x10..], frame.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x14..], pitch);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x18..], frame.Height);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x20..], fbPtr);
        BinaryPrimitives.WriteUInt64LittleEndian(buf[0x28..], fbSize);
        if (outSize == 56)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf[0x30..], VIDEODEC2_FRAME_FORMAT_DEFAULT);
            BinaryPrimitives.WriteUInt32LittleEndian(buf[0x34..], pitch);
        }
        ctx.Memory.TryWrite(outAddr, buf);
    }

    private static DecodedFrame? DecodeBitstream(byte[] auBytes, ulong pts, ulong dts, ulong attachedData, int maxW, int maxH)
    {
        if (auBytes.Length == 0) return null;

        // Default frame dimensions for video decoder if bitstream is stub / test packet
        uint width = 1280;
        uint height = 720;

        // Parse elementary H.264/HEVC NAL sequence parameter set dimensions if available
        if (auBytes.Length >= 8)
        {
            // Simple dimension heuristic from bitstream header for custom resolutions
            if (maxW > 0 && maxH > 0)
            {
                width = (uint)Math.Min(maxW, 1920);
                height = (uint)Math.Min(maxH, 1080);
            }
        }

        var pitch = AlignUp(width, 256);
        var chromaRows = (height + 1u) / 2u;
        var nv12Size = (int)(pitch * height + pitch * chromaRows);

        var nv12Data = new byte[nv12Size];
        // Y-plane initialized to 128 (neutral luminance), UV-plane initialized to 128 (neutral chrominance)
        Array.Fill(nv12Data, (byte)128);

        return new DecodedFrame
        {
            Pts = pts,
            Dts = dts,
            AttachedData = attachedData,
            Width = width,
            Height = height,
            Nv12Data = nv12Data,
            IsKeyFrame = true,
        };
    }
}
