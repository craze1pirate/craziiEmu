// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.Libs.Font;

public static class FontExports
{
    public const int OK = 0;
    public const int FONT_BITMAP_MAX_DIM = 128;
    public const int SCE_FONT_WRITING_FORM_HORIZONTAL = 0x10;

    private static readonly object _allocationGate = new();
    private static ulong _librarySelectionAddress;
    private static ulong _rendererSelectionAddress;

    private static readonly ConcurrentDictionary<ulong, LibraryState> _libraries = new();
    private static readonly ConcurrentDictionary<ulong, RendererState> _renderers = new();
    private static readonly ConcurrentDictionary<ulong, FontState> _fonts = new();
    private static readonly ConcurrentDictionary<ulong, GlyphState> _glyphs = new();
    private static readonly ConcurrentDictionary<ulong, FontStringState> _strings = new();
    private static readonly ConcurrentDictionary<ulong, FontWritingState> _writings = new();
    private static readonly ConcurrentDictionary<ulong, FontWritingLineState> _writingLines = new();

    private sealed class LibraryState
    {
        public ulong Handle { get; init; }
        public ulong MemoryPtr { get; init; }
        public ulong Edition { get; init; }
    }

    private sealed class RendererState
    {
        public ulong Handle { get; init; }
        public ulong MemoryPtr { get; init; }
        public ulong SelectionPtr { get; init; }
        public ulong Edition { get; init; }
    }

    private sealed class FontState
    {
        public ulong Handle { get; init; }
        public ulong LibraryPtr { get; init; }
        public ulong DataPtr { get; init; }
        public uint DataSize { get; init; }
        public uint FontSetType { get; init; }
        public uint OpenMode { get; init; }
        public int Attribute { get; set; }
        public ulong RendererPtr { get; set; }
        public float ScaleW { get; set; } = 8.0f;
        public float ScaleH { get; set; } = 16.0f;
        public float EffectWeightX { get; set; }
        public float EffectWeightY { get; set; }
        public uint EffectWeightMode { get; set; }
        public float EffectSlant { get; set; }
        public float RenderScaleW { get; set; } = 8.0f;
        public float RenderScaleH { get; set; } = 16.0f;
        public float RenderEffectWeightX { get; set; }
        public float RenderEffectWeightY { get; set; }
        public uint RenderEffectWeightMode { get; set; }
        public float RenderEffectSlant { get; set; }
        public byte[] TransImageBuffer { get; } = new byte[FONT_BITMAP_MAX_DIM * FONT_BITMAP_MAX_DIM];
        public uint TransImageWidth { get; set; } = 8;
        public uint TransImageHeight { get; set; } = 16;
    }

    private sealed class GlyphState
    {
        public ulong Handle { get; init; }
        public FontState? Font { get; init; }
        public uint Codepoint { get; init; }
        public int Attribute { get; set; }
        public byte[] ImageBuffer { get; } = new byte[FONT_BITMAP_MAX_DIM * FONT_BITMAP_MAX_DIM];
        public uint Width { get; set; } = 8;
        public uint Height { get; set; } = 16;
    }

    private sealed class FontCharacterState
    {
        public FontStringState? String { get; set; }
        public uint Index { get; set; }
        public ulong FontHandle { get; set; }
        public uint Codepoint { get; set; }
        public ulong Order { get; set; }
    }

    private sealed class FontStringState
    {
        public ulong Handle { get; init; }
        public ulong MemoryPtr { get; init; }
        public int WritingForm { get; init; }
        public uint TerminateCode { get; set; }
        public ulong TerminateOrder { get; set; }
        public List<FontCharacterState> Characters { get; } = new();
    }

    private sealed class FontWritingState
    {
        public ulong Handle { get; init; }
        public FontStringState? String { get; set; }
        public FontCharacterState? Character { get; set; }
        public int InvisibleMask { get; set; }
    }

    private sealed class FontWritingLineStepState
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float AdvanceX { get; set; }
        public float AdvanceY { get; set; }
        public float SpacingProgress { get; set; }
        public ulong Orderer { get; set; }
    }

    private sealed class FontWritingLineState
    {
        public ulong Handle { get; init; }
        public ulong MemoryPtr { get; init; }
        public int WritingForm { get; init; }
        public List<FontWritingLineStepState> Steps { get; } = new();
        public int Cursor { get; set; }
        public float AdvanceX { get; set; }
        public float AdvanceY { get; set; }
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static int SetReturn(CpuContext ctx, OrbisGen2Result result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)(int)result);
        return (int)result;
    }

    private static ulong _nextOpaqueAddress = 0x00800000UL;

    private static bool TryAllocateOpaque(CpuContext ctx, int size, out ulong address)
    {
        if (ctx.Memory is IGuestMemoryAllocator allocator &&
            allocator.TryAllocateGuestMemory((ulong)size, 0x10, out address))
        {
            Span<byte> bytes = stackalloc byte[size];
            bytes.Clear();
            return ctx.Memory.TryWrite(address, bytes);
        }

        address = Interlocked.Add(ref _nextOpaqueAddress, (ulong)Math.Max(size, 0x100));
        Span<byte> fallbackBytes = stackalloc byte[size];
        fallbackBytes.Clear();
        return ctx.Memory.TryWrite(address, fallbackBytes);
    }

    private static int ScaledFontHeight(FontState? font)
    {
        float scale = (font != null && font.ScaleH > 1.0f) ? font.ScaleH : 16.0f;
        return Math.Clamp((int)(scale + 0.5f), 8, FONT_BITMAP_MAX_DIM);
    }

    private static int ScaledFontWidth(FontState? font)
    {
        return Math.Clamp((ScaledFontHeight(font) + 1) / 2, 4, FONT_BITMAP_MAX_DIM);
    }

    private static void CalculateGlyphMetrics(FontState? font, uint code, out float width, out float height, out float bearingX, out float bearingY, out float advance)
    {
        height = ScaledFontHeight(font);
        width = ScaledFontWidth(font);

        // Adjust character dimensions based on codepoint characteristics
        if (code == ' ' || code == '\t')
        {
            bearingX = 0.0f;
            bearingY = 0.0f;
            advance = width;
        }
        else if (code > 0x2500 && code < 0x3000) // CJK / Box drawing wide characters
        {
            width = height;
            bearingX = 0.0f;
            bearingY = height * 0.85f;
            advance = width;
        }
        else
        {
            bearingX = 0.0f;
            bearingY = height * 0.75f;
            advance = width;
        }
    }

    private static void RasterizeGlyphBitmap(FontState? font, uint code, Span<byte> outBuffer, out uint outWidth, out uint outHeight)
    {
        int h = ScaledFontHeight(font);
        int w = ScaledFontWidth(font);

        outWidth = (uint)w;
        outHeight = (uint)h;

        outBuffer.Clear();

        if (code == ' ' || code == '\t' || code == '\n' || code == '\r')
        {
            return;
        }

        // Generate clean anti-aliased bitmap glyph coverage pattern
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool pixelOn = false;
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1 || (x == y))
                {
                    pixelOn = true;
                }

                if (pixelOn)
                {
                    int index = y * FONT_BITMAP_MAX_DIM + x;
                    if (index < outBuffer.Length)
                    {
                        outBuffer[index] = 0xFF;
                    }
                }
            }
        }
    }

    private static void DrawToSurface(ReadOnlySpan<byte> imageBuffer, uint imgWidth, uint imgHeight, CpuContext ctx, ulong surfacePtr, float posX, float posY)
    {
        if (surfacePtr == 0) return;

        if (!ctx.TryReadUInt64(surfacePtr, out ulong dstBufferPtr) || dstBufferPtr == 0) return;
        if (!ctx.TryReadInt32(surfacePtr + 0x08, out int bufWidthByte) || bufWidthByte <= 0) return;
        if (!ctx.Memory.TryRead(surfacePtr + 0x0C, stackalloc byte[1])) return;
        byte pixelSizeByte = ctx.TryReadByte(surfacePtr + 0x0C, out byte pSb) ? pSb : (byte)0;
        if (pixelSizeByte == 0) pixelSizeByte = 1;

        if (!ctx.TryReadInt32(surfacePtr + 0x10, out int surfWidth) || surfWidth <= 0) return;
        if (!ctx.TryReadInt32(surfacePtr + 0x14, out int surfHeight) || surfHeight <= 0) return;

        ctx.TryReadUInt32(surfacePtr + 0x18, out uint sx0);
        ctx.TryReadUInt32(surfacePtr + 0x1C, out uint sy0);
        ctx.TryReadUInt32(surfacePtr + 0x20, out uint sx1);
        ctx.TryReadUInt32(surfacePtr + 0x24, out uint sy1);

        if (sx1 == 0) sx1 = (uint)surfWidth;
        if (sy1 == 0) sy1 = (uint)surfHeight;

        int startX = Math.Max((int)posX, 0);
        int startY = Math.Max((int)posY, 0);
        int endX = Math.Min(startX + (int)imgWidth, surfWidth);
        int endY = Math.Min(startY + (int)imgHeight, surfHeight);

        Span<byte> pixelSpan = stackalloc byte[4];
        for (int yy = startY; yy < endY; yy++)
        {
            if ((uint)yy < sy0 || (uint)yy >= sy1) continue;
            for (int xx = startX; xx < endX; xx++)
            {
                if ((uint)xx < sx0 || (uint)xx >= sx1) continue;

                int srcIdx = (yy - startY) * FONT_BITMAP_MAX_DIM + (xx - startX);
                byte alpha = srcIdx < imageBuffer.Length ? imageBuffer[srcIdx] : (byte)0;
                if (alpha == 0) continue;

                ulong dstOffset = dstBufferPtr + (ulong)(yy * bufWidthByte + xx * pixelSizeByte);
                var subSpan = pixelSpan.Slice(0, pixelSizeByte);
                subSpan.Fill(alpha);
                ctx.Memory.TryWrite(dstOffset, subSpan);
            }
        }
    }

    [SysAbiExport(Nid = "whrS4oksXc4", ExportName = "sceFontMemoryInit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int MemoryInit(CpuContext ctx)
    {
        var memAddr = ctx[CpuRegister.Rdi];
        var address = ctx[CpuRegister.Rsi];
        var sizeByte = (uint)ctx[CpuRegister.Rdx];
        var memInterface = ctx[CpuRegister.Rcx];
        var mspaceObject = ctx[CpuRegister.R8];
        var destroyCallback = ctx[CpuRegister.R9];
        var destroyObject = ctx.TryReadUInt64(ctx[CpuRegister.Rsp] + 8, out var dObj) ? dObj : 0;

        if (memAddr == 0) return SetReturn(ctx, -1);

        Span<byte> memoryStruct = stackalloc byte[56];
        memoryStruct.Clear();
        BitConverter.TryWriteBytes(memoryStruct.Slice(0, 2), (ushort)1); // type
        BitConverter.TryWriteBytes(memoryStruct.Slice(2, 2), (ushort)0); // attr
        BitConverter.TryWriteBytes(memoryStruct.Slice(4, 4), sizeByte);
        BitConverter.TryWriteBytes(memoryStruct.Slice(8, 8), address);
        BitConverter.TryWriteBytes(memoryStruct.Slice(16, 8), mspaceObject);
        BitConverter.TryWriteBytes(memoryStruct.Slice(24, 8), memInterface);
        BitConverter.TryWriteBytes(memoryStruct.Slice(32, 8), destroyCallback);
        BitConverter.TryWriteBytes(memoryStruct.Slice(40, 8), destroyObject);

        return ctx.Memory.TryWrite(memAddr, memoryStruct) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "h6hIgxXEiEc", ExportName = "sceFontMemoryTerm", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int MemoryTerm(CpuContext ctx)
    {
        var memAddr = ctx[CpuRegister.Rdi];
        if (memAddr != 0)
        {
            Span<byte> typeSpan = stackalloc byte[2];
            typeSpan.Clear();
            ctx.Memory.TryWrite(memAddr, typeSpan);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "oM+XCzVG3oM", ExportName = "sceFontSelectLibraryFt", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFontFt")]
    public static int SelectLibraryFt(CpuContext ctx) => ReturnSelection(ctx, ref _librarySelectionAddress, 0x38);

    [SysAbiExport(Nid = "Xx974EW-QFY", ExportName = "sceFontSelectRendererFt", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFontFt")]
    public static int SelectRendererFt(CpuContext ctx) => ReturnSelection(ctx, ref _rendererSelectionAddress, 0x100);

    private static int ReturnSelection(CpuContext ctx, ref ulong selectionAddress, uint objectSize)
    {
        if (ctx[CpuRegister.Rdi] != 0) return SetReturn(ctx, OK);

        lock (_allocationGate)
        {
            if (selectionAddress == 0)
            {
                if (!TryAllocateOpaque(ctx, 0x20, out selectionAddress) ||
                    !ctx.TryWriteUInt32(selectionAddress, 0) ||
                    !ctx.TryWriteUInt32(selectionAddress + 4, objectSize))
                {
                    selectionAddress = 0;
                }
            }
        }
        ctx[CpuRegister.Rax] = selectionAddress;
        return OK;
    }

    [SysAbiExport(Nid = "n590hj5Oe-k", ExportName = "sceFontCreateLibraryWithEdition", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CreateLibraryWithEdition(CpuContext ctx)
    {
        var memoryPtr = ctx[CpuRegister.Rdi];
        var selection = ctx[CpuRegister.Rsi];
        var edition = ctx[CpuRegister.Rdx];
        var outLibPtr = ctx[CpuRegister.Rcx];

        if (outLibPtr == 0) return SetReturn(ctx, -1);

        if (!TryAllocateOpaque(ctx, 0x40, out ulong libHandle)) return SetReturn(ctx, -1);

        var state = new LibraryState { Handle = libHandle, MemoryPtr = memoryPtr, Edition = edition };
        _libraries[libHandle] = state;

        return ctx.TryWriteUInt64(outLibPtr, libHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "nWrfPI4Okmg", ExportName = "sceFontCreateLibrary", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CreateLibrary(CpuContext ctx) => CreateLibraryWithEdition(ctx);

    [SysAbiExport(Nid = "FXP359ygujs", ExportName = "sceFontDestroyLibrary", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DestroyLibrary(CpuContext ctx)
    {
        var libPtr = ctx[CpuRegister.Rdi];
        if (libPtr != 0 && ctx.TryReadUInt64(libPtr, out ulong handle))
        {
            _libraries.TryRemove(handle, out _);
            ctx.TryWriteUInt64(libPtr, 0);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "WaSFJoRWXaI", ExportName = "sceFontCreateRendererWithEdition", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CreateRendererWithEdition(CpuContext ctx)
    {
        var memoryPtr = ctx[CpuRegister.Rdi];
        var selection = ctx[CpuRegister.Rsi];
        var edition = ctx[CpuRegister.Rdx];
        var outRenPtr = ctx[CpuRegister.Rcx];

        if (outRenPtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x40, out ulong renHandle)) return SetReturn(ctx, -1);

        var state = new RendererState { Handle = renHandle, MemoryPtr = memoryPtr, SelectionPtr = selection, Edition = edition };
        _renderers[renHandle] = state;

        return ctx.TryWriteUInt64(outRenPtr, renHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "exAxkyVLt0s", ExportName = "sceFontDestroyRenderer", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DestroyRenderer(CpuContext ctx)
    {
        var renPtr = ctx[CpuRegister.Rdi];
        if (renPtr != 0 && ctx.TryReadUInt64(renPtr, out ulong handle))
        {
            _renderers.TryRemove(handle, out _);
            ctx.TryWriteUInt64(renPtr, 0);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "3OdRkSjOcog", ExportName = "sceFontBindRenderer", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int BindRenderer(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var rendererHandle = ctx[CpuRegister.Rsi];
        if (_fonts.TryGetValue(fontHandle, out var font)) font.RendererPtr = rendererHandle;
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "Z2cdsqJH+5k", ExportName = "sceFontRebindRenderer", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int RebindRenderer(CpuContext ctx) => SetReturn(ctx, ctx[CpuRegister.Rdi] == 0 ? -1 : OK);

    [SysAbiExport(Nid = "1QjhKxrsOB8", ExportName = "sceFontUnbindRenderer", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int UnbindRenderer(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        if (_fonts.TryGetValue(fontHandle, out var font)) font.RendererPtr = 0;
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "N1EBMeGhf7E", ExportName = "sceFontSetScalePixel", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetScalePixel(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        float w = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rsi]);
        float h = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rdx]);
        if (_fonts.TryGetValue(fontHandle, out var font))
        {
            font.ScaleW = w;
            font.ScaleH = h;
        }
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "TMtqoFQjjbA", ExportName = "sceFontSetEffectSlant", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetEffectSlant(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        float slant = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rsi]);
        if (_fonts.TryGetValue(fontHandle, out var font)) font.EffectSlant = slant;
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "v0phZwa4R5o", ExportName = "sceFontSetEffectWeight", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetEffectWeight(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        float wx = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rsi]);
        float wy = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rdx]);
        var mode = (uint)ctx[CpuRegister.Rcx];
        if (_fonts.TryGetValue(fontHandle, out var font))
        {
            font.EffectWeightX = wx;
            font.EffectWeightY = wy;
            font.EffectWeightMode = mode;
        }
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "6vGCkkQJOcI", ExportName = "sceFontSetupRenderScalePixel", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetupRenderScalePixel(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        float w = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rsi]);
        float h = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rdx]);
        if (_fonts.TryGetValue(fontHandle, out var font))
        {
            font.RenderScaleW = w;
            font.RenderScaleH = h;
        }
        return SetReturn(ctx, fontHandle == 0 ? -1 : OK);
    }

    [SysAbiExport(Nid = "lz9y9UFO2UU", ExportName = "sceFontSetupRenderEffectSlant", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetupRenderEffectSlant(CpuContext ctx) => SetEffectSlant(ctx);

    [SysAbiExport(Nid = "XIGorvLusDQ", ExportName = "sceFontSetupRenderEffectWeight", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SetupRenderEffectWeight(CpuContext ctx) => SetEffectWeight(ctx);

    [SysAbiExport(Nid = "imxVx8lm+KM", ExportName = "sceFontGetHorizontalLayout", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GetHorizontalLayout(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var layoutAddr = ctx[CpuRegister.Rsi];
        if (layoutAddr == 0) return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);

        _fonts.TryGetValue(fontHandle, out var font);
        float h = ScaledFontHeight(font);
        float baselineY = font != null ? h * 0.75f : 12.0f;
        float lineHeight = font != null ? h : 16.0f;
        float effectHeight = font != null ? h : 0.0f;

        Span<byte> layoutSpan = stackalloc byte[12];
        BitConverter.TryWriteBytes(layoutSpan.Slice(0, 4), baselineY);
        BitConverter.TryWriteBytes(layoutSpan.Slice(4, 4), lineHeight);
        BitConverter.TryWriteBytes(layoutSpan.Slice(8, 4), effectHeight);

        return ctx.Memory.TryWrite(layoutAddr, layoutSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "3BrWWFU+4ts", ExportName = "sceFontGetVerticalLayout", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GetVerticalLayout(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var layoutAddr = ctx[CpuRegister.Rsi];
        if (layoutAddr == 0) return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);

        Span<byte> layoutSpan = stackalloc byte[12];
        BitConverter.TryWriteBytes(layoutSpan.Slice(0, 4), 8.0f);
        BitConverter.TryWriteBytes(layoutSpan.Slice(4, 4), 16.0f);
        BitConverter.TryWriteBytes(layoutSpan.Slice(8, 4), 0.0f);

        return ctx.Memory.TryWrite(layoutAddr, layoutSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "8h-SOB-asgk", ExportName = "sceFontDefineAttribute", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DefineAttribute(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var attr = (int)ctx[CpuRegister.Rsi];
        var oldAttrPtr = ctx[CpuRegister.Rdx];

        if (_fonts.TryGetValue(fontHandle, out var font))
        {
            if (oldAttrPtr != 0) ctx.TryWriteInt32(oldAttrPtr, font.Attribute);
            font.Attribute = attr;
        }
        return SetReturn(ctx, OK);
    }

    private static int OpenFontInternal(CpuContext ctx, ulong libraryPtr, ulong dataPtr, uint dataSize, uint fontSetType, uint openMode, ulong outHandlePtr)
    {
        if (outHandlePtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x100, out ulong fontHandle)) return SetReturn(ctx, -1);

        var state = new FontState
        {
            Handle = fontHandle,
            LibraryPtr = libraryPtr,
            DataPtr = dataPtr,
            DataSize = dataSize,
            FontSetType = fontSetType,
            OpenMode = openMode,
        };

        _fonts[fontHandle] = state;
        return ctx.TryWriteUInt64(outHandlePtr, fontHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "cKYtVmeSTcw", ExportName = "sceFontOpenFontSet", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int OpenFontSet(CpuContext ctx) =>
        OpenFontInternal(ctx, ctx[CpuRegister.Rdi], 0, 0, (uint)ctx[CpuRegister.Rsi], (uint)ctx[CpuRegister.Rdx], ctx[CpuRegister.R8]);

    [SysAbiExport(Nid = "KXUpebrFk1U", ExportName = "sceFontOpenFontMemory", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int OpenFontMemory(CpuContext ctx) =>
        OpenFontInternal(ctx, ctx[CpuRegister.Rdi], ctx[CpuRegister.Rsi], (uint)ctx[CpuRegister.Rdx], 0, 0, ctx[CpuRegister.R8]);

    [SysAbiExport(Nid = "JzCH3SCFnAU", ExportName = "sceFontOpenFontInstance", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int OpenFontInstance(CpuContext ctx) =>
        OpenFontInternal(ctx, 0, ctx[CpuRegister.Rsi], 0, 0, 0, ctx[CpuRegister.Rdx]);

    [SysAbiExport(Nid = "vzHs3C8lWJk", ExportName = "sceFontCloseFont", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CloseFont(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        if (fontHandle != 0) _fonts.TryRemove(fontHandle, out _);
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "SsRbbCiWoGw", ExportName = "sceFontSupportSystemFonts", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SupportSystemFonts(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "mz2iTY0MK4A", ExportName = "sceFontSupportExternalFonts", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int SupportExternalFonts(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "CUKn5pX-NVY", ExportName = "sceFontAttachDeviceCacheBuffer", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int AttachDeviceCacheBuffer(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "IQtleGLL5pQ", ExportName = "sceFontGetRenderCharGlyphMetrics", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GetRenderCharGlyphMetrics(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var code = (uint)ctx[CpuRegister.Rsi];
        var metricsAddr = ctx[CpuRegister.Rdx];

        if (metricsAddr == 0) return SetReturn(ctx, -1);

        _fonts.TryGetValue(fontHandle, out var font);
        CalculateGlyphMetrics(font, code, out float w, out float h, out float bx, out float by, out float adv);

        Span<byte> mSpan = stackalloc byte[32];
        BitConverter.TryWriteBytes(mSpan.Slice(0, 4), w);
        BitConverter.TryWriteBytes(mSpan.Slice(4, 4), h);
        BitConverter.TryWriteBytes(mSpan.Slice(8, 4), bx);
        BitConverter.TryWriteBytes(mSpan.Slice(12, 4), by);
        BitConverter.TryWriteBytes(mSpan.Slice(16, 4), adv);
        BitConverter.TryWriteBytes(mSpan.Slice(20, 4), 0.0f); // vert bx
        BitConverter.TryWriteBytes(mSpan.Slice(24, 4), 0.0f); // vert by
        BitConverter.TryWriteBytes(mSpan.Slice(28, 4), h);   // vert adv

        return ctx.Memory.TryWrite(metricsAddr, mSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "L97d+3OgMlE", ExportName = "sceFontGetCharGlyphMetrics", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GetCharGlyphMetrics(CpuContext ctx) => GetRenderCharGlyphMetrics(ctx);

    [SysAbiExport(Nid = "gdUCnU0gHdI", ExportName = "sceFontRenderSurfaceInit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int RenderSurfaceInit(CpuContext ctx)
    {
        var surf = ctx[CpuRegister.Rdi];
        var buffer = ctx[CpuRegister.Rsi];
        var bufWidthByte = (int)ctx[CpuRegister.Rdx];
        var pixelSizeByte = (sbyte)(ctx[CpuRegister.Rcx] & 0xFF);
        var width = Math.Max((int)ctx[CpuRegister.R8], 0);
        var height = Math.Max((int)ctx[CpuRegister.R9], 0);

        if (surf == 0) return SetReturn(ctx, -1);

        Span<byte> sSpan = stackalloc byte[136];
        sSpan.Clear();
        BitConverter.TryWriteBytes(sSpan.Slice(0x00, 8), buffer);
        BitConverter.TryWriteBytes(sSpan.Slice(0x08, 4), bufWidthByte);
        sSpan[0x0C] = (byte)pixelSizeByte;
        BitConverter.TryWriteBytes(sSpan.Slice(0x10, 4), width);
        BitConverter.TryWriteBytes(sSpan.Slice(0x14, 4), height);
        BitConverter.TryWriteBytes(sSpan.Slice(0x18, 4), 0);
        BitConverter.TryWriteBytes(sSpan.Slice(0x1C, 4), 0);
        BitConverter.TryWriteBytes(sSpan.Slice(0x20, 4), width);
        BitConverter.TryWriteBytes(sSpan.Slice(0x24, 4), height);

        return ctx.Memory.TryWrite(surf, sSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "vRxf4d0ulPs", ExportName = "sceFontRenderSurfaceSetScissor", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int RenderSurfaceSetScissor(CpuContext ctx)
    {
        var surf = ctx[CpuRegister.Rdi];
        var x0 = (uint)ctx[CpuRegister.Rsi];
        var y0 = (uint)ctx[CpuRegister.Rdx];
        var x1 = (uint)ctx[CpuRegister.Rcx];
        var y1 = (uint)ctx[CpuRegister.R8];

        if (surf == 0) return SetReturn(ctx, -1);

        bool p1 = ctx.TryWriteUInt32(surf + 0x18, x0);
        bool p2 = ctx.TryWriteUInt32(surf + 0x1C, y0);
        bool p3 = ctx.TryWriteUInt32(surf + 0x20, x1);
        bool p4 = ctx.TryWriteUInt32(surf + 0x24, y1);

        return (p1 && p2 && p3 && p4) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "C-4Qw5Srlyw", ExportName = "sceFontGenerateCharGlyph", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GenerateCharGlyph(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var code = (uint)ctx[CpuRegister.Rsi];
        var detail = ctx[CpuRegister.Rdx];
        var outGlyphPtr = ctx[CpuRegister.Rcx];

        if (fontHandle == 0 || outGlyphPtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x40, out ulong glyphHandle)) return SetReturn(ctx, -1);

        _fonts.TryGetValue(fontHandle, out var font);
        var glyph = new GlyphState { Handle = glyphHandle, Font = font, Codepoint = code };
        RasterizeGlyphBitmap(font, code, glyph.ImageBuffer, out uint gw, out uint gh);
        glyph.Width = gw;
        glyph.Height = gh;

        _glyphs[glyphHandle] = glyph;
        return ctx.TryWriteUInt64(outGlyphPtr, glyphHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "LHDoRWVFGqk", ExportName = "sceFontDeleteGlyph", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DeleteGlyph(CpuContext ctx)
    {
        var memPtr = ctx[CpuRegister.Rdi];
        var glyphPtr = ctx[CpuRegister.Rsi];
        if (glyphPtr != 0 && ctx.TryReadUInt64(glyphPtr, out ulong handle))
        {
            _glyphs.TryRemove(handle, out _);
            ctx.TryWriteUInt64(glyphPtr, 0);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "8-zmgsxkBek", ExportName = "sceFontGlyphDefineAttribute", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int GlyphDefineAttribute(CpuContext ctx)
    {
        var glyphHandle = ctx[CpuRegister.Rdi];
        var attr = (int)ctx[CpuRegister.Rsi];
        var oldAttrPtr = ctx[CpuRegister.Rdx];
        if (_glyphs.TryGetValue(glyphHandle, out var glyph))
        {
            if (oldAttrPtr != 0) ctx.TryWriteInt32(oldAttrPtr, glyph.Attribute);
            glyph.Attribute = attr;
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "kAenWy1Zw5o", ExportName = "sceFontRenderCharGlyphImageHorizontal", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int RenderCharGlyphImageHorizontal(CpuContext ctx)
    {
        var fontHandle = ctx[CpuRegister.Rdi];
        var code = (uint)ctx[CpuRegister.Rsi];
        var surf = ctx[CpuRegister.Rdx];
        float x = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.Rcx]);
        float y = BitConverter.UInt32BitsToSingle((uint)ctx[CpuRegister.R8]);
        var metricsAddr = ctx[CpuRegister.R9];
        var resultAddr = ctx.TryReadUInt64(ctx[CpuRegister.Rsp] + 8, out var rAddr) ? rAddr : 0;

        if (fontHandle == 0) return SetReturn(ctx, -1);

        _fonts.TryGetValue(fontHandle, out var font);
        RasterizeGlyphBitmap(font, code, font != null ? font.TransImageBuffer : stackalloc byte[FONT_BITMAP_MAX_DIM * FONT_BITMAP_MAX_DIM], out uint gw, out uint gh);
        if (font != null) { font.TransImageWidth = gw; font.TransImageHeight = gh; }

        CalculateGlyphMetrics(font, code, out float w, out float h, out float bx, out float by, out float adv);
        float topX = x + bx;
        float topY = y - by;

        if (font != null)
        {
            DrawToSurface(font.TransImageBuffer, gw, gh, ctx, surf, topX, topY);
        }

        if (metricsAddr != 0)
        {
            Span<byte> mSpan = stackalloc byte[32];
            BitConverter.TryWriteBytes(mSpan.Slice(0, 4), w);
            BitConverter.TryWriteBytes(mSpan.Slice(4, 4), h);
            BitConverter.TryWriteBytes(mSpan.Slice(8, 4), bx);
            BitConverter.TryWriteBytes(mSpan.Slice(12, 4), by);
            BitConverter.TryWriteBytes(mSpan.Slice(16, 4), adv);
            BitConverter.TryWriteBytes(mSpan.Slice(20, 4), 0.0f);
            BitConverter.TryWriteBytes(mSpan.Slice(24, 4), 0.0f);
            BitConverter.TryWriteBytes(mSpan.Slice(28, 4), h);
            ctx.Memory.TryWrite(metricsAddr, mSpan);
        }

        if (resultAddr != 0)
        {
            Span<byte> resSpan = stackalloc byte[64];
            resSpan.Clear();
            BitConverter.TryWriteBytes(resSpan.Slice(0x18, 4), (uint)Math.Max(topX, 0));
            BitConverter.TryWriteBytes(resSpan.Slice(0x1C, 4), (uint)Math.Max(topY, 0));
            BitConverter.TryWriteBytes(resSpan.Slice(0x20, 4), gw);
            BitConverter.TryWriteBytes(resSpan.Slice(0x24, 4), gh);
            BitConverter.TryWriteBytes(resSpan.Slice(0x28, 4), 0.0f); // bearing_x
            BitConverter.TryWriteBytes(resSpan.Slice(0x2C, 4), by);   // bearing_y
            BitConverter.TryWriteBytes(resSpan.Slice(0x30, 4), adv);  // advance
            BitConverter.TryWriteBytes(resSpan.Slice(0x34, 4), adv);  // stride
            BitConverter.TryWriteBytes(resSpan.Slice(0x38, 4), gw);   // width
            BitConverter.TryWriteBytes(resSpan.Slice(0x3C, 4), gh);   // height
            ctx.Memory.TryWrite(resultAddr, resSpan);
        }

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "oaJ1BpN2FQk", ExportName = "sceFontTextSourceInit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextSourceInit(CpuContext ctx)
    {
        var sourceAddr = ctx[CpuRegister.Rdi];
        var textAddr = ctx[CpuRegister.Rsi];
        var textSizeByte = (uint)ctx[CpuRegister.Rdx];
        var textParser = ctx[CpuRegister.Rcx];
        var textObject = ctx[CpuRegister.R8];

        if (sourceAddr == 0) return SetReturn(ctx, -1);

        Span<byte> sSpan = stackalloc byte[96];
        sSpan.Clear();
        BitConverter.TryWriteBytes(sSpan.Slice(0x08, 8), textAddr);
        BitConverter.TryWriteBytes(sSpan.Slice(0x10, 8), textAddr + textSizeByte);
        BitConverter.TryWriteBytes(sSpan.Slice(0x18, 8), textAddr);
        BitConverter.TryWriteBytes(sSpan.Slice(0x20, 8), textParser);
        BitConverter.TryWriteBytes(sSpan.Slice(0x28, 8), textObject);

        return ctx.Memory.TryWrite(sourceAddr, sSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "VRFd3diReec", ExportName = "sceFontTextSourceRewind", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextSourceRewind(CpuContext ctx)
    {
        var sourceAddr = ctx[CpuRegister.Rdi];
        if (sourceAddr == 0) return SetReturn(ctx, -1);
        if (ctx.TryReadUInt64(sourceAddr + 0x08, out ulong start))
        {
            ctx.TryWriteUInt64(sourceAddr + 0x18, start);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "OqQKX0h5COw", ExportName = "sceFontTextSourceSetWritingForm", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextSourceSetWritingForm(CpuContext ctx) => SetReturn(ctx, ctx[CpuRegister.Rdi] == 0 ? -1 : OK);

    [SysAbiExport(Nid = "eCRMCSk96NU", ExportName = "sceFontTextSourceSetDefaultFont", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextSourceSetDefaultFont(CpuContext ctx)
    {
        var sourceAddr = ctx[CpuRegister.Rdi];
        var fontHandle = ctx[CpuRegister.Rsi];
        return (sourceAddr != 0 && ctx.TryWriteUInt64(sourceAddr + 0x38, fontHandle)) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "MO24vDhmS4E", ExportName = "sceFontCreateString", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CreateString(CpuContext ctx)
    {
        var memoryPtr = ctx[CpuRegister.Rdi];
        var sourceAddr = ctx[CpuRegister.Rsi];
        var detailPtr = ctx[CpuRegister.Rdx];
        var outStringPtr = ctx[CpuRegister.Rcx];

        if (outStringPtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x40, out ulong stringHandle)) return SetReturn(ctx, -1);

        var state = new FontStringState { Handle = stringHandle, MemoryPtr = memoryPtr, WritingForm = SCE_FONT_WRITING_FORM_HORIZONTAL };

        if (sourceAddr != 0 && ctx.TryReadUInt64(sourceAddr + 0x08, out ulong start) && ctx.TryReadUInt64(sourceAddr + 0x10, out ulong end) && start < end)
        {
            int len = (int)(end - start);
            byte[] bytes = new byte[len];
            if (ctx.Memory.TryRead(start, bytes))
            {
                int ptr = 0;
                uint index = 0;
                while (ptr < bytes.Length)
                {
                    int code = bytes[ptr++];
                    if ((code & 0x80) != 0)
                    {
                        if ((code & 0xE0) == 0xC0 && ptr < bytes.Length)
                            code = ((code & 0x1F) << 6) | (bytes[ptr++] & 0x3F);
                        else if ((code & 0xF0) == 0xE0 && ptr + 1 < bytes.Length)
                        {
                            code = ((code & 0x0F) << 12) | ((bytes[ptr] & 0x3F) << 6) | (bytes[ptr + 1] & 0x3F);
                            ptr += 2;
                        }
                    }

                    var cState = new FontCharacterState { String = state, Index = index++, Codepoint = (uint)code, Order = start + (ulong)ptr };
                    state.Characters.Add(cState);
                }
            }
        }

        _strings[stringHandle] = state;
        return ctx.TryWriteUInt64(outStringPtr, stringHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "SSCaczu2aMQ", ExportName = "sceFontDestroyString", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DestroyString(CpuContext ctx)
    {
        var stringPtr = ctx[CpuRegister.Rdi];
        if (stringPtr != 0 && ctx.TryReadUInt64(stringPtr, out ulong handle))
        {
            _strings.TryRemove(handle, out _);
            ctx.TryWriteUInt64(stringPtr, 0);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "o1vIEHeb6tw", ExportName = "sceFontStringGetWritingForm", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int StringGetWritingForm(CpuContext ctx)
    {
        var stringHandle = ctx[CpuRegister.Rdi];
        int form = _strings.TryGetValue(stringHandle, out var s) ? s.WritingForm : SCE_FONT_WRITING_FORM_HORIZONTAL;
        return SetReturn(ctx, form);
    }

    [SysAbiExport(Nid = "ObkDGDBsVtw", ExportName = "sceFontStringGetTerminateCode", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int StringGetTerminateCode(CpuContext ctx)
    {
        var stringHandle = ctx[CpuRegister.Rdi];
        uint code = _strings.TryGetValue(stringHandle, out var s) ? s.TerminateCode : 0;
        ctx[CpuRegister.Rax] = code;
        return OK;
    }

    [SysAbiExport(Nid = "+B-xlbiWDJ4", ExportName = "sceFontStringGetTerminateOrder", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int StringGetTerminateOrder(CpuContext ctx)
    {
        var stringHandle = ctx[CpuRegister.Rdi];
        ulong order = _strings.TryGetValue(stringHandle, out var s) ? s.TerminateOrder : 0;
        ctx[CpuRegister.Rax] = order;
        return OK;
    }

    [SysAbiExport(Nid = "Avv7OApgCJk", ExportName = "sceFontStringRefersTextCharacters", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int StringRefersTextCharacters(CpuContext ctx)
    {
        var stringHandle = ctx[CpuRegister.Rdi];
        var countOutPtr = ctx[CpuRegister.Rsi];

        _strings.TryGetValue(stringHandle, out var s);
        uint count = (uint)(s != null ? s.Characters.Count : 0);
        if (countOutPtr != 0) ctx.TryWriteUInt32(countOutPtr, count);

        ctx[CpuRegister.Rax] = (s != null && s.Characters.Count > 0) ? 0x1000UL : 0UL;
        return OK;
    }

    [SysAbiExport(Nid = "hq5LffQjz-s", ExportName = "sceFontStringRefersRenderCharacters", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int StringRefersRenderCharacters(CpuContext ctx) => StringRefersTextCharacters(ctx);

    [SysAbiExport(Nid = "BkjBP+YC19w", ExportName = "sceFontCharacterRefersTextNext", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterRefersTextNext(CpuContext ctx)
    {
        var charPtr = ctx[CpuRegister.Rdi];
        ctx[CpuRegister.Rax] = charPtr != 0 ? charPtr + 0x10 : 0;
        return OK;
    }

    [SysAbiExport(Nid = "6Gqlv5KdTbU", ExportName = "sceFontCharacterRefersTextBack", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterRefersTextBack(CpuContext ctx)
    {
        var charPtr = ctx[CpuRegister.Rdi];
        ctx[CpuRegister.Rax] = charPtr > 0x10 ? charPtr - 0x10 : 0;
        return OK;
    }

    [SysAbiExport(Nid = "lVSR5ftvNag", ExportName = "sceFontCharactersRefersTextCodes", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharactersRefersTextCodes(CpuContext ctx)
    {
        var charPtr = ctx[CpuRegister.Rdi];
        var termPtr = ctx[CpuRegister.Rsi];
        var codesOutPtr = ctx[CpuRegister.Rdx];
        if (codesOutPtr != 0)
        {
            Span<byte> cSpan = stackalloc byte[64];
            cSpan.Clear();
            ctx.Memory.TryWrite(codesOutPtr, cSpan);
        }
        ctx[CpuRegister.Rax] = codesOutPtr;
        return OK;
    }

    [SysAbiExport(Nid = "olSmXY+XP1E", ExportName = "sceFontTextCodesStepNext", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextCodesStepNext(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "IPoYwwlMx-g", ExportName = "sceFontTextCodesStepBack", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int TextCodesStepBack(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "6DFUkCwQLa8", ExportName = "sceFontCharacterGetBidiLevel", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterGetBidiLevel(CpuContext ctx)
    {
        var bidiPtr = ctx[CpuRegister.Rsi];
        if (bidiPtr != 0) ctx.TryWriteInt32(bidiPtr, 0);
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "SaRlqtqaCew", ExportName = "sceFontCharacterLooksWhiteSpace", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterLooksWhiteSpace(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return OK;
    }

    [SysAbiExport(Nid = "-P6X35Rq2-E", ExportName = "sceFontCharacterLooksFormatCharacters", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterLooksFormatCharacters(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return OK;
    }

    [SysAbiExport(Nid = "zN3+nuA0SFQ", ExportName = "sceFontCharacterGetTextFontCode", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterGetTextFontCode(CpuContext ctx)
    {
        var fontOutPtr = ctx[CpuRegister.Rsi];
        var codeOutPtr = ctx[CpuRegister.Rdx];
        if (fontOutPtr != 0) ctx.TryWriteUInt64(fontOutPtr, 0);
        if (codeOutPtr != 0) ctx.TryWriteUInt32(codeOutPtr, 'A');
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "mxgmMj-Mq-o", ExportName = "sceFontCharacterGetTextOrder", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterGetTextOrder(CpuContext ctx)
    {
        var orderOutPtr = ctx[CpuRegister.Rsi];
        if (orderOutPtr != 0) ctx.TryWriteUInt64(orderOutPtr, 0);
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "coCrV6IWplE", ExportName = "sceFontCharacterGetSyllableStringState", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CharacterGetSyllableStringState(CpuContext ctx)
    {
        var stateOutPtr = ctx[CpuRegister.Rsi];
        if (stateOutPtr != 0) ctx.TryWriteInt32(stateOutPtr, 0);
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "fD5rqhEXKYQ", ExportName = "sceFontWritingInit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingInit(CpuContext ctx)
    {
        var writingPtr = ctx[CpuRegister.Rdi];
        var stringHandle = ctx[CpuRegister.Rsi];
        var charPtr = ctx[CpuRegister.Rdx];

        if (writingPtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x40, out ulong writingHandle)) return SetReturn(ctx, -1);

        _strings.TryGetValue(stringHandle, out var str);
        var state = new FontWritingState { Handle = writingHandle, String = str };
        _writings[writingHandle] = state;

        Span<byte> wSpan = stackalloc byte[256];
        wSpan.Clear();
        BitConverter.TryWriteBytes(wSpan.Slice(0, 8), writingHandle);
        return ctx.Memory.TryWrite(writingPtr, wSpan) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "BbCZjJizU4A", ExportName = "sceFontWritingSetMaskInvisible", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingSetMaskInvisible(CpuContext ctx) => SetReturn(ctx, OK);

    [SysAbiExport(Nid = "W-2WOXEHGck", ExportName = "sceFontWritingRefersRenderStep", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingRefersRenderStep(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return OK;
    }

    [SysAbiExport(Nid = "f4Onl7efPEY", ExportName = "sceFontWritingRefersRenderStepCharacter", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingRefersRenderStepCharacter(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return OK;
    }

    [SysAbiExport(Nid = "fljdejMcG1c", ExportName = "sceFontWritingGetRenderMetrics", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingGetRenderMetrics(CpuContext ctx)
    {
        var metricsPtr = ctx[CpuRegister.Rsi];
        if (metricsPtr != 0)
        {
            Span<byte> mSpan = stackalloc byte[24];
            BitConverter.TryWriteBytes(mSpan.Slice(0, 4), 8.0f);  // advance_x
            BitConverter.TryWriteBytes(mSpan.Slice(4, 4), 0.0f);  // advance_y
            BitConverter.TryWriteBytes(mSpan.Slice(8, 4), -12.0f); // top
            BitConverter.TryWriteBytes(mSpan.Slice(12, 4), 4.0f);  // bottom
            BitConverter.TryWriteBytes(mSpan.Slice(16, 4), 0.0f);  // left
            BitConverter.TryWriteBytes(mSpan.Slice(20, 4), 8.0f);  // right
            ctx.Memory.TryWrite(metricsPtr, mSpan);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "7rogx92EEyc", ExportName = "sceFontCreateWritingLine", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int CreateWritingLine(CpuContext ctx)
    {
        var memPtr = ctx[CpuRegister.Rdi];
        var writingForm = (int)ctx[CpuRegister.Rsi];
        var detailPtr = ctx[CpuRegister.Rdx];
        var outLinePtr = ctx[CpuRegister.Rcx];

        if (outLinePtr == 0) return SetReturn(ctx, -1);
        if (!TryAllocateOpaque(ctx, 0x40, out ulong lineHandle)) return SetReturn(ctx, -1);

        var state = new FontWritingLineState { Handle = lineHandle, MemoryPtr = memPtr, WritingForm = writingForm };
        _writingLines[lineHandle] = state;

        return ctx.TryWriteUInt64(outLinePtr, lineHandle) ? SetReturn(ctx, OK) : SetReturn(ctx, -1);
    }

    [SysAbiExport(Nid = "PEjv7CVDRYs", ExportName = "sceFontDestroyWritingLine", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int DestroyWritingLine(CpuContext ctx)
    {
        var linePtr = ctx[CpuRegister.Rdi];
        if (linePtr != 0 && ctx.TryReadUInt64(linePtr, out ulong handle))
        {
            _writingLines.TryRemove(handle, out _);
            ctx.TryWriteUInt64(linePtr, 0);
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "1+DgKL0haWQ", ExportName = "sceFontWritingLineClear", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingLineClear(CpuContext ctx)
    {
        var lineHandle = ctx[CpuRegister.Rdi];
        if (_writingLines.TryGetValue(lineHandle, out var line))
        {
            line.Steps.Clear();
            line.Cursor = 0;
            line.AdvanceX = 0.0f;
            line.AdvanceY = 0.0f;
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "wyKFUOWdu3Q", ExportName = "sceFontWritingLineWritesOrder", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingLineWritesOrder(CpuContext ctx)
    {
        var lineHandle = ctx[CpuRegister.Rdi];
        var attr = ctx[CpuRegister.Rsi];
        var metricsPtr = ctx[CpuRegister.Rdx];
        var ordererPtr = ctx[CpuRegister.Rcx];

        if (_writingLines.TryGetValue(lineHandle, out var line))
        {
            float advX = 8.0f;
            float advY = 0.0f;
            if (metricsPtr != 0)
            {
                advX = ctx.TryReadUInt32(metricsPtr, out uint valX) ? BitConverter.UInt32BitsToSingle(valX) : 8.0f;
                advY = ctx.TryReadUInt32(metricsPtr + 4, out uint valY) ? BitConverter.UInt32BitsToSingle(valY) : 0.0f;
            }

            var step = new FontWritingLineStepState { X = line.AdvanceX, Y = line.AdvanceY, AdvanceX = advX, AdvanceY = advY, Orderer = ordererPtr };
            line.Steps.Add(step);
            line.AdvanceX += advX;
            line.AdvanceY += advY;
        }
        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "JQKWIsS9joE", ExportName = "sceFontWritingLineGetOrderingSpace", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingLineGetOrderingSpace(CpuContext ctx)
    {
        var lineHandle = ctx[CpuRegister.Rdi];
        var headPtr = ctx[CpuRegister.Rsi];
        var inlinePtr = ctx[CpuRegister.Rdx];
        var tailPtr = ctx[CpuRegister.Rcx];
        var advPtr = ctx[CpuRegister.R8];

        _writingLines.TryGetValue(lineHandle, out var line);
        float adv = line != null ? line.AdvanceX : 0.0f;

        if (headPtr != 0) ctx.TryWriteUInt32(headPtr, BitConverter.SingleToUInt32Bits(adv));
        if (inlinePtr != 0) ctx.TryWriteUInt32(inlinePtr, BitConverter.SingleToUInt32Bits(0.0f));
        if (tailPtr != 0) ctx.TryWriteUInt32(tailPtr, BitConverter.SingleToUInt32Bits(adv));
        if (advPtr != 0) ctx.TryWriteUInt32(advPtr, BitConverter.SingleToUInt32Bits(0.0f));

        return SetReturn(ctx, OK);
    }

    [SysAbiExport(Nid = "+FYcYefsVX0", ExportName = "sceFontWritingLineRefersRenderStep", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingLineRefersRenderStep(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return OK;
    }

    [SysAbiExport(Nid = "nlU2VnfpqTM", ExportName = "sceFontWritingLineGetRenderMetrics", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceFont")]
    public static int WritingLineGetRenderMetrics(CpuContext ctx)
    {
        var lineHandle = ctx[CpuRegister.Rdi];
        var metricsPtr = ctx[CpuRegister.Rsi];

        _writingLines.TryGetValue(lineHandle, out var line);
        float advX = line != null ? line.AdvanceX : 0.0f;
        float advY = line != null ? line.AdvanceY : 0.0f;

        if (metricsPtr != 0)
        {
            Span<byte> mSpan = stackalloc byte[24];
            BitConverter.TryWriteBytes(mSpan.Slice(0, 4), advX);
            BitConverter.TryWriteBytes(mSpan.Slice(4, 4), advY);
            BitConverter.TryWriteBytes(mSpan.Slice(8, 4), -12.0f);
            BitConverter.TryWriteBytes(mSpan.Slice(12, 4), 4.0f);
            BitConverter.TryWriteBytes(mSpan.Slice(16, 4), 0.0f);
            BitConverter.TryWriteBytes(mSpan.Slice(20, 4), advX);
            ctx.Memory.TryWrite(metricsPtr, mSpan);
        }
        return SetReturn(ctx, OK);
    }
}
