// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.PngDec;

public static class PngDecExports
{
    public const int PNG_DEC_ERROR_INVALID_ADDR        = unchecked((int)0x80690001);
    public const int PNG_DEC_ERROR_INVALID_SIZE        = unchecked((int)0x80690002);
    public const int PNG_DEC_ERROR_INVALID_PARAM       = unchecked((int)0x80690003);
    public const int PNG_DEC_ERROR_INVALID_HANDLE      = unchecked((int)0x80690004);
    public const int PNG_DEC_ERROR_INVALID_WORK_MEMORY = unchecked((int)0x80690005);
    public const int PNG_DEC_ERROR_INVALID_DATA        = unchecked((int)0x80690010);
    public const int PNG_DEC_ERROR_DECODE_ERROR        = unchecked((int)0x80690012);

    public const uint PNG_DEC_ATTRIBUTE_BIT_DEPTH_16      = 1;
    public const ushort PNG_DEC_COLOR_SPACE_GRAYSCALE       = 2;
    public const ushort PNG_DEC_COLOR_SPACE_RGB             = 3;
    public const ushort PNG_DEC_COLOR_SPACE_CLUT            = 4;
    public const ushort PNG_DEC_COLOR_SPACE_GRAYSCALE_ALPHA = 18;
    public const ushort PNG_DEC_COLOR_SPACE_RGBA            = 19;
    public const ushort PNG_DEC_PIXEL_FORMAT_R8G8B8A8       = 0;
    public const ushort PNG_DEC_PIXEL_FORMAT_B8G8R8A8       = 1;
    public const uint PNG_DEC_IMAGE_FLAG_ADAM7_INTERLACE  = 1;
    public const uint PNG_DEC_IMAGE_FLAG_TRNS_CHUNK_EXIST = 2;

    public const ulong PNG_DEC_CONTEXT_MAGIC = 0x4b595459504e4744UL; // KYTYPNGD

    private static ReadOnlySpan<byte> PngSignature =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    ];

    private struct PngHeaderInfo
    {
        public uint Width;
        public uint Height;
        public ushort ColorSpace;
        public ushort BitDepth;
        public uint ImageFlag;
        public byte RawColorType;
        public byte RawBitDepth;
        public byte RawInterlace;
        public byte[] Palette;
        public byte[] TransKey;
    }

    [SysAbiExport(
        Nid = "-6srIGbLTIU",
        ExportName = "scePngDecQueryMemorySize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePngDec")]
    public static int PngDecQueryMemorySize(CpuContext ctx)
    {
        var paramAddr = ctx[CpuRegister.Rdi];
        if (paramAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        Span<byte> paramBytes = stackalloc byte[12];
        if (!ctx.Memory.TryRead(paramAddr, paramBytes))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        var attribute = BinaryPrimitives.ReadUInt32LittleEndian(paramBytes[0x04..]);
        var maxWidth = BinaryPrimitives.ReadUInt32LittleEndian(paramBytes[0x08..]);

        if (attribute > PNG_DEC_ATTRIBUTE_BIT_DEPTH_16)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        if (maxWidth == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_SIZE);
            return PNG_DEC_ERROR_INVALID_SIZE;
        }

        ctx[CpuRegister.Rax] = 8; // sizeof(PngDecContext)
        return 8;
    }

    [SysAbiExport(
        Nid = "m0uW+8pFyaw",
        ExportName = "scePngDecCreate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePngDec")]
    public static int PngDecCreate(CpuContext ctx)
    {
        var paramAddr = ctx[CpuRegister.Rdi];
        var memoryAddr = ctx[CpuRegister.Rsi];
        var memorySize = (uint)ctx[CpuRegister.Rdx];
        var handleOutAddr = ctx[CpuRegister.Rcx];

        if (paramAddr == 0 || handleOutAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        Span<byte> paramBytes = stackalloc byte[12];
        if (!ctx.Memory.TryRead(paramAddr, paramBytes))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        var attribute = BinaryPrimitives.ReadUInt32LittleEndian(paramBytes[0x04..]);
        var maxWidth = BinaryPrimitives.ReadUInt32LittleEndian(paramBytes[0x08..]);

        if (attribute > PNG_DEC_ATTRIBUTE_BIT_DEPTH_16)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        if (maxWidth == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_SIZE);
            return PNG_DEC_ERROR_INVALID_SIZE;
        }

        if (memoryAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        if (memorySize < 8)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_WORK_MEMORY);
            return PNG_DEC_ERROR_INVALID_WORK_MEMORY;
        }

        Span<byte> magicBuf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(magicBuf, PNG_DEC_CONTEXT_MAGIC);
        if (!ctx.Memory.TryWrite(memoryAddr, magicBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        Span<byte> handleBuf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(handleBuf, memoryAddr);
        if (!ctx.Memory.TryWrite(handleOutAddr, handleBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    [SysAbiExport(
        Nid = "QbD+eENEwo8",
        ExportName = "scePngDecDelete",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePngDec")]
    public static int PngDecDelete(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        if (handle == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_HANDLE);
            return PNG_DEC_ERROR_INVALID_HANDLE;
        }

        Span<byte> magicBuf = stackalloc byte[8];
        if (!ctx.Memory.TryRead(handle, magicBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_HANDLE);
            return PNG_DEC_ERROR_INVALID_HANDLE;
        }

        var magic = BinaryPrimitives.ReadUInt64LittleEndian(magicBuf);
        if (magic != PNG_DEC_CONTEXT_MAGIC)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_HANDLE);
            return PNG_DEC_ERROR_INVALID_HANDLE;
        }

        magicBuf.Clear();
        ctx.Memory.TryWrite(handle, magicBuf);

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    [SysAbiExport(
        Nid = "U6h4e5JRPaQ",
        ExportName = "scePngDecParseHeader",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePngDec")]
    public static int PngDecParseHeader(CpuContext ctx)
    {
        var paramAddr = ctx[CpuRegister.Rdi];
        var infoOutAddr = ctx[CpuRegister.Rsi];

        if (paramAddr == 0 || infoOutAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        Span<byte> paramBuf = stackalloc byte[16];
        if (!ctx.Memory.TryRead(paramAddr, paramBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        var pngMemAddr = BinaryPrimitives.ReadUInt64LittleEndian(paramBuf[0x00..]);
        var pngMemSize = BinaryPrimitives.ReadUInt32LittleEndian(paramBuf[0x08..]);
        var reserved0 = BinaryPrimitives.ReadUInt32LittleEndian(paramBuf[0x0C..]);

        if (pngMemAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        if (reserved0 != 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        var pngData = new byte[pngMemSize];
        if (!ctx.Memory.TryRead(pngMemAddr, pngData))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        if (!ParsePngHeader(pngData, out var headerInfo))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_DATA);
            return PNG_DEC_ERROR_INVALID_DATA;
        }

        WriteImageInfo(ctx, infoOutAddr, headerInfo);

        ctx[CpuRegister.Rax] = 0;
        return 0;
    }

    [SysAbiExport(
        Nid = "WC216DD3El4",
        ExportName = "scePngDecDecode",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePngDec")]
    public static int PngDecDecode(CpuContext ctx)
    {
        var handle = ctx[CpuRegister.Rdi];
        var paramAddr = ctx[CpuRegister.Rsi];
        var infoOutAddr = ctx[CpuRegister.Rdx];

        if (handle == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_HANDLE);
            return PNG_DEC_ERROR_INVALID_HANDLE;
        }

        Span<byte> magicBuf = stackalloc byte[8];
        if (!ctx.Memory.TryRead(handle, magicBuf) || BinaryPrimitives.ReadUInt64LittleEndian(magicBuf) != PNG_DEC_CONTEXT_MAGIC)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_HANDLE);
            return PNG_DEC_ERROR_INVALID_HANDLE;
        }

        if (paramAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        Span<byte> paramBuf = stackalloc byte[32];
        if (!ctx.Memory.TryRead(paramAddr, paramBuf))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        var pngMemAddr = BinaryPrimitives.ReadUInt64LittleEndian(paramBuf[0x00..]);
        var imageMemAddr = BinaryPrimitives.ReadUInt64LittleEndian(paramBuf[0x08..]);
        var pngMemSize = BinaryPrimitives.ReadUInt32LittleEndian(paramBuf[0x10..]);
        var imageMemSize = BinaryPrimitives.ReadUInt32LittleEndian(paramBuf[0x14..]);
        var pixelFormat = BinaryPrimitives.ReadUInt16LittleEndian(paramBuf[0x18..]);
        var alphaValue = BinaryPrimitives.ReadUInt16LittleEndian(paramBuf[0x1A..]);
        var imagePitch = BinaryPrimitives.ReadUInt32LittleEndian(paramBuf[0x1C..]);

        if (pngMemAddr == 0 || imageMemAddr == 0)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        if (pixelFormat != PNG_DEC_PIXEL_FORMAT_R8G8B8A8 && pixelFormat != PNG_DEC_PIXEL_FORMAT_B8G8R8A8)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_PARAM);
            return PNG_DEC_ERROR_INVALID_PARAM;
        }

        var pngData = new byte[pngMemSize];
        if (!ctx.Memory.TryRead(pngMemAddr, pngData))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
            return PNG_DEC_ERROR_INVALID_ADDR;
        }

        if (!ParsePngHeader(pngData, out var headerInfo))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_DATA);
            return PNG_DEC_ERROR_INVALID_DATA;
        }

        if (infoOutAddr != 0)
        {
            WriteImageInfo(ctx, infoOutAddr, headerInfo);
        }

        var minPitch = headerInfo.Width * 4u;
        var pitch = (imagePitch == 0 ? minPitch : imagePitch);
        var minSize = (ulong)pitch * (headerInfo.Height - 1u) + minPitch;

        if (pitch < minPitch || minSize > imageMemSize)
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_SIZE);
            return PNG_DEC_ERROR_INVALID_SIZE;
        }

        if (!DecodePngPixels(pngData, headerInfo, out var rgbaPixels))
        {
            ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_DECODE_ERROR);
            return PNG_DEC_ERROR_DECODE_ERROR;
        }

        var applyAlpha = !PngHasSourceAlpha(headerInfo);
        var clampedAlpha = (byte)Math.Min((int)alphaValue, 255);
        Span<byte> rowPixels = (int)minPitch <= 4096 ? stackalloc byte[(int)minPitch] : new byte[(int)minPitch];

        for (uint y = 0; y < headerInfo.Height; y++)
        {
            var srcRowOffset = (int)(y * headerInfo.Width * 4u);
            var dstRowAddr = imageMemAddr + y * pitch;
            for (uint x = 0; x < headerInfo.Width; x++)
            {
                var srcIdx = srcRowOffset + (int)(x * 4u);
                var dstIdx = (int)(x * 4u);

                var r = rgbaPixels[srcIdx + 0];
                var g = rgbaPixels[srcIdx + 1];
                var b = rgbaPixels[srcIdx + 2];
                var a = applyAlpha ? clampedAlpha : rgbaPixels[srcIdx + 3];

                if (pixelFormat == PNG_DEC_PIXEL_FORMAT_B8G8R8A8)
                {
                    rowPixels[dstIdx + 0] = b;
                    rowPixels[dstIdx + 1] = g;
                    rowPixels[dstIdx + 2] = r;
                    rowPixels[dstIdx + 3] = a;
                }
                else
                {
                    rowPixels[dstIdx + 0] = r;
                    rowPixels[dstIdx + 1] = g;
                    rowPixels[dstIdx + 2] = b;
                    rowPixels[dstIdx + 3] = a;
                }
            }

            if (!ctx.Memory.TryWrite(dstRowAddr, rowPixels))
            {
                ctx[CpuRegister.Rax] = unchecked((ulong)PNG_DEC_ERROR_INVALID_ADDR);
                return PNG_DEC_ERROR_INVALID_ADDR;
            }
        }

        int resultVal = (headerInfo.Width > 32767u || headerInfo.Height > 32767u)
            ? 0
            : (int)((headerInfo.Width << 16) | headerInfo.Height);

        ctx[CpuRegister.Rax] = unchecked((ulong)resultVal);
        return resultVal;
    }

    private static bool PngHasSourceAlpha(in PngHeaderInfo header)
    {
        return header.ColorSpace == PNG_DEC_COLOR_SPACE_RGBA ||
               header.ColorSpace == PNG_DEC_COLOR_SPACE_GRAYSCALE_ALPHA ||
               (header.ImageFlag & PNG_DEC_IMAGE_FLAG_TRNS_CHUNK_EXIST) != 0;
    }

    private static void WriteImageInfo(CpuContext ctx, ulong addr, in PngHeaderInfo header)
    {
        Span<byte> buf = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x00..], header.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x04..], header.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x08..], header.ColorSpace);
        BinaryPrimitives.WriteUInt16LittleEndian(buf[0x0A..], header.BitDepth);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[0x0C..], header.ImageFlag);
        ctx.Memory.TryWrite(addr, buf);
    }

    private static bool ParsePngHeader(ReadOnlySpan<byte> png, out PngHeaderInfo info)
    {
        info = default;
        if (png.Length < 33 || !png[..8].SequenceEqual(PngSignature))
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(png[8..12]) != 13 || !png[12..16].SequenceEqual("IHDR"u8))
        {
            return false;
        }

        info.Width = BinaryPrimitives.ReadUInt32BigEndian(png[16..20]);
        info.Height = BinaryPrimitives.ReadUInt32BigEndian(png[20..24]);
        info.RawBitDepth = png[24];
        info.RawColorType = png[25];
        info.RawInterlace = png[28];

        info.BitDepth = info.RawBitDepth;
        info.ImageFlag = (info.RawInterlace == 1 ? PNG_DEC_IMAGE_FLAG_ADAM7_INTERLACE : 0u);

        switch (info.RawColorType)
        {
            case 0: info.ColorSpace = PNG_DEC_COLOR_SPACE_GRAYSCALE; break;
            case 2: info.ColorSpace = PNG_DEC_COLOR_SPACE_RGB; break;
            case 3: info.ColorSpace = PNG_DEC_COLOR_SPACE_CLUT; break;
            case 4: info.ColorSpace = PNG_DEC_COLOR_SPACE_GRAYSCALE_ALPHA; break;
            case 6: info.ColorSpace = PNG_DEC_COLOR_SPACE_RGBA; break;
            default: return false;
        }

        if (info.Width == 0 || info.Height == 0)
        {
            return false;
        }

        var offset = 33;
        while (offset + 12 <= png.Length)
        {
            var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            var type = png.Slice(offset + 4, 4);

            if (offset + 12 + (long)chunkLen > png.Length)
            {
                return false;
            }

            var chunkData = png.Slice(offset + 8, (int)chunkLen);

            if (type.SequenceEqual("PLTE"u8))
            {
                info.Palette = chunkData.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                info.ImageFlag |= PNG_DEC_IMAGE_FLAG_TRNS_CHUNK_EXIST;
                info.TransKey = chunkData.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
            {
                break;
            }

            offset += (int)chunkLen + 12;
        }

        return true;
    }

    private static bool DecodePngPixels(ReadOnlySpan<byte> png, in PngHeaderInfo header, out byte[] rgbaPixels)
    {
        rgbaPixels = [];
        try
        {
            using var compressed = new MemoryStream();
            var offset = 8;
            while (offset <= png.Length - 12)
            {
                var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
                if (offset + 12 + (long)chunkLen > png.Length) break;

                var type = png.Slice(offset + 4, 4);
                var data = png.Slice(offset + 8, (int)chunkLen);

                if (type.SequenceEqual("IDAT"u8))
                {
                    compressed.Write(data);
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    break;
                }

                offset += (int)chunkLen + 12;
            }

            if (compressed.Length == 0) return false;

            int bpp = header.RawColorType switch
            {
                0 => 1,
                2 => 3,
                3 => 1,
                4 => 2,
                6 => 4,
                _ => 0
            };

            if (bpp == 0 || header.BitDepth != 8 || header.RawInterlace != 0)
            {
                return false;
            }

            var stride = (int)header.Width * bpp;
            var scanlineLen = stride + 1;
            var decompressedLen = scanlineLen * (int)header.Height;
            var scanlines = GC.AllocateUninitializedArray<byte>(decompressedLen);

            compressed.Position = 0;
            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            {
                zlib.ReadExactly(scanlines);
            }

            var reconstructed = GC.AllocateUninitializedArray<byte>(stride * (int)header.Height);
            for (var y = 0; y < (int)header.Height; y++)
            {
                var srcLine = scanlines.AsSpan(y * scanlineLen + 1, stride);
                var targetLine = reconstructed.AsSpan(y * stride, stride);
                var prevLine = y == 0 ? ReadOnlySpan<byte>.Empty : reconstructed.AsSpan((y - 1) * stride, stride);

                if (!UnfilterLine(scanlines[y * scanlineLen], srcLine, prevLine, targetLine, bpp))
                {
                    return false;
                }
            }

            rgbaPixels = GC.AllocateUninitializedArray<byte>((int)header.Width * (int)header.Height * 4);
            for (int y = 0; y < (int)header.Height; y++)
            {
                for (int x = 0; x < (int)header.Width; x++)
                {
                    int srcIdx = (y * stride) + (x * bpp);
                    int dstIdx = ((y * (int)header.Width) + x) * 4;

                    byte r = 0, g = 0, b = 0, a = 255;
                    switch (header.RawColorType)
                    {
                        case 0: // Grayscale
                            r = g = b = reconstructed[srcIdx];
                            if (header.TransKey is not null && header.TransKey.Length >= 2 &&
                                r == header.TransKey[1])
                            {
                                a = 0;
                            }
                            break;
                        case 2: // RGB
                            r = reconstructed[srcIdx + 0];
                            g = reconstructed[srcIdx + 1];
                            b = reconstructed[srcIdx + 2];
                            if (header.TransKey is not null && header.TransKey.Length >= 6 &&
                                r == header.TransKey[1] && g == header.TransKey[3] && b == header.TransKey[5])
                            {
                                a = 0;
                            }
                            break;
                        case 3: // Indexed / CLUT
                            var idx = reconstructed[srcIdx];
                            if (header.Palette is not null && (idx * 3 + 2) < header.Palette.Length)
                            {
                                r = header.Palette[idx * 3 + 0];
                                g = header.Palette[idx * 3 + 1];
                                b = header.Palette[idx * 3 + 2];
                            }
                            if (header.TransKey is not null && idx < header.TransKey.Length)
                            {
                                a = header.TransKey[idx];
                            }
                            break;
                        case 4: // Grayscale + Alpha
                            r = g = b = reconstructed[srcIdx + 0];
                            a = reconstructed[srcIdx + 1];
                            break;
                        case 6: // RGBA
                            r = reconstructed[srcIdx + 0];
                            g = reconstructed[srcIdx + 1];
                            b = reconstructed[srcIdx + 2];
                            a = reconstructed[srcIdx + 3];
                            break;
                    }

                    rgbaPixels[dstIdx + 0] = r;
                    rgbaPixels[dstIdx + 1] = g;
                    rgbaPixels[dstIdx + 2] = b;
                    rgbaPixels[dstIdx + 3] = a;
                }
            }

            return true;
        }
        catch
        {
            rgbaPixels = [];
            return false;
        }
    }

    private static bool UnfilterLine(byte filter, ReadOnlySpan<byte> source, ReadOnlySpan<byte> previous, Span<byte> target, int bpp)
    {
        for (var x = 0; x < source.Length; x++)
        {
            var left = x >= bpp ? target[x - bpp] : (byte)0;
            var above = previous.IsEmpty ? (byte)0 : previous[x];
            var upperLeft = !previous.IsEmpty && x >= bpp ? previous[x - bpp] : (byte)0;
            target[x] = filter switch
            {
                0 => source[x],
                1 => unchecked((byte)(source[x] + left)),
                2 => unchecked((byte)(source[x] + above)),
                3 => unchecked((byte)(source[x] + ((left + above) >> 1))),
                4 => unchecked((byte)(source[x] + Paeth(left, above, upperLeft))),
                _ => source[x],
            };

            if (filter > 4) return false;
        }
        return true;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
    }
}
