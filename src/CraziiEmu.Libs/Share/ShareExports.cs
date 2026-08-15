// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Share;

public static class ShareExports
{
    private const int MaxContentParamBytes = 4096;

    public const int SHARE_ERROR_INVALID_PARAM = -2120876030; // 0x81960002
    public const int SHARE_ERROR_NOT_SUPPORTED = -2120876025; // 0x81960007
    public const int SHARE_REQUEST_ID_INVALID  = -1;

    private static int _initialized;
    private static string _contentParam = string.Empty;
    private static string _applicationTitleParam = string.Empty;

    private static bool ShareFeatureFlagValid(uint featureFlags)
    {
        return featureFlags != 0;
    }

    [SysAbiExport(Nid = "nBDD66kiFW8", ExportName = "sceShareInitialize", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShareUtility")]
    public static int ShareInitialize(CpuContext ctx)
    {
        var memorySize = ctx[CpuRegister.Rdi];
        var priority = unchecked((int)ctx[CpuRegister.Rsi]);
        var affinityMask = ctx[CpuRegister.Rdx];
        if (memorySize == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Interlocked.Exchange(ref _initialized, 1);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "ErH6tKS7fzE", ExportName = "sceShareCaptureScreenshot", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareCaptureScreenshot(CpuContext ctx)
    {
        var reqIdPtr = ctx[CpuRegister.Rsi];
        if (reqIdPtr != 0)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, SHARE_REQUEST_ID_INVALID);
            ctx.Memory.TryWrite(reqIdPtr, buf);
        }

        return ctx.SetReturn(SHARE_ERROR_NOT_SUPPORTED);
    }

    [SysAbiExport(Nid = "GQTObcITIXI", ExportName = "sceShareCaptureScreenshotExtended", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareCaptureScreenshotExtended(CpuContext ctx) => ShareCaptureScreenshot(ctx);

    [SysAbiExport(Nid = "4jt8pMDudgk", ExportName = "sceShareCaptureVideoClip", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareCaptureVideoClip(CpuContext ctx)
    {
        var reqIdPtr = ctx[CpuRegister.Rsi];
        if (reqIdPtr != 0)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buf, SHARE_REQUEST_ID_INVALID);
            ctx.Memory.TryWrite(reqIdPtr, buf);
        }

        return ctx.SetReturn(SHARE_ERROR_NOT_SUPPORTED);
    }

    [SysAbiExport(Nid = "AcDNpEpoT9U", ExportName = "sceShareCaptureVideoClipExtended", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareCaptureVideoClipExtended(CpuContext ctx) => ShareCaptureVideoClip(ctx);

    [SysAbiExport(Nid = "8qAJ0Jd58-Q", ExportName = "sceShareOpenMenuForContent", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareOpenMenuForContent(CpuContext ctx)
    {
        return ctx.SetReturn(SHARE_ERROR_NOT_SUPPORTED);
    }

    [SysAbiExport(Nid = "YBiIdcDPrxs", ExportName = "sceShareFeaturePermit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareFeaturePermit(CpuContext ctx)
    {
        uint flags = (uint)ctx[CpuRegister.Rdi];
        if (!ShareFeatureFlagValid(flags)) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "5wjxESwX68I", ExportName = "sceShareFeatureProhibit", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareFeatureProhibit(CpuContext ctx)
    {
        uint flags = (uint)ctx[CpuRegister.Rdi];
        if (!ShareFeatureFlagValid(flags)) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "kCurUZVFqcI", ExportName = "sceShareSetCaptureSource", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareSetCaptureSource(CpuContext ctx)
    {
        uint flags = (uint)ctx[CpuRegister.Rdi];
        if (!ShareFeatureFlagValid(flags)) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "7QZtURYnXG4", ExportName = "sceShareSetContentParam", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareSetContentParam(CpuContext ctx)
    {
        var contentParamAddress = ctx[CpuRegister.Rdi];
        if (contentParamAddress == 0) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        if (!TryReadNullTerminatedUtf8(ctx, contentParamAddress, MaxContentParamBytes, out var contentParam))
        {
            return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);
        }

        _contentParam = contentParam;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "ORspsWDXPps", ExportName = "sceShareSetContentParamForApplicationTitle", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareSetContentParamForApplicationTitle(CpuContext ctx)
    {
        var appTitleAddress = ctx[CpuRegister.Rdi];
        if (appTitleAddress == 0) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        if (!TryReadNullTerminatedUtf8(ctx, appTitleAddress, MaxContentParamBytes, out var appTitle))
        {
            return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);
        }

        _applicationTitleParam = appTitle;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "T64o-315wbg", ExportName = "sceShareSetScreenshotOverlayImage", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareSetScreenshotOverlayImage(CpuContext ctx)
    {
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "Sygnk9dr5WQ", ExportName = "sceShareRegisterContentEventCallback", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareRegisterContentEventCallback(CpuContext ctx)
    {
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "KnsfHKmZqFA", ExportName = "sceShareUnregisterContentEventCallback", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareUnregisterContentEventCallback(CpuContext ctx)
    {
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "QNop2YAtIDE", ExportName = "sceShareGetCurrentStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareGetCurrentStatus(CpuContext ctx)
    {
        uint flags = (uint)ctx[CpuRegister.Rdi];
        ulong statusPtr = ctx[CpuRegister.Rsi];

        if (!ShareFeatureFlagValid(flags) || statusPtr == 0) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        Span<byte> zeroBuf = stackalloc byte[16];
        zeroBuf.Clear();
        if (!ctx.Memory.TryWrite(statusPtr, zeroBuf)) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "crFxyW3HdK0", ExportName = "sceShareGetRunningStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceShare")]
    public static int ShareGetRunningStatus(CpuContext ctx)
    {
        ulong flagsPtr = ctx[CpuRegister.Rdi];
        if (flagsPtr == 0) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        Span<byte> zeroBuf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(zeroBuf, 0);
        if (!ctx.Memory.TryWrite(flagsPtr, zeroBuf)) return ctx.SetReturn(SHARE_ERROR_INVALID_PARAM);

        return ctx.SetReturn(0);
    }

    private static bool TryReadNullTerminatedUtf8(CpuContext ctx, ulong address, int maxLength, out string value)
    {
        Span<byte> bytes = stackalloc byte[maxLength];
        Span<byte> one = stackalloc byte[1];
        for (var index = 0; index < maxLength; index++)
        {
            if (!ctx.Memory.TryRead(address + (ulong)index, one))
            {
                value = string.Empty;
                return false;
            }

            if (one[0] == 0)
            {
                value = Encoding.UTF8.GetString(bytes[..index]);
                return true;
            }

            bytes[index] = one[0];
        }

        value = string.Empty;
        return false;
    }
}
