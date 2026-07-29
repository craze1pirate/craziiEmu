// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using CraziiEmu.Libs.VideoOut;

namespace CraziiEmu.Libs.Unity;

public static class UnityCompatExports
{
    [SysAbiExport(
        ExportName = "UnitySetGraphicsDevice",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int UnitySetGraphicsDevice(CpuContext ctx)
    {
        var deviceHandlePtr = ctx[CpuRegister.Rdi];
        if (deviceHandlePtr != 0)
        {
            _ = ctx.TryWriteUInt64(deviceHandlePtr, 1UL);
        }
        return 0;
    }

    [SysAbiExport(
        ExportName = "UnityRenderEvent",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int UnityRenderEvent(CpuContext ctx)
    {
        var deviceHandle = unchecked((int)ctx[CpuRegister.Rdi]);
        var eventId = unchecked((int)ctx[CpuRegister.Rsi]);
        return VideoOutExports.SubmitFlipFromAgc(ctx, deviceHandle == 0 ? 1 : deviceHandle, 0, 0, (long)eventId);
    }

    [SysAbiExport(
        ExportName = "UnityGetAudioEffectDefinitions",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libAudioOut")]
    public static int UnityGetAudioEffectDefinitions(CpuContext ctx)
    {
        var countPtr = ctx[CpuRegister.Rdi];
        if (countPtr != 0)
        {
            _ = ctx.TryWriteUInt32(countPtr, 0);
        }
        return 0;
    }

    [SysAbiExport(
        ExportName = "UnityPluginLoad",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnityPluginLoad(CpuContext ctx)
    {
        var handlePtr = ctx[CpuRegister.Rsi];
        if (handlePtr != 0)
        {
            _ = ctx.TryWriteUInt64(handlePtr, 1UL);
        }
        return 0;
    }

    [SysAbiExport(
        ExportName = "UnityPluginUnload",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnityPluginUnload(CpuContext ctx) => 0;

    [SysAbiExport(
        ExportName = "UnityRenderingExtEvent",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnityRenderingExtEvent(CpuContext ctx) => 0;

    [SysAbiExport(
        ExportName = "UnityRenderingExtQuery",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnityRenderingExtQuery(CpuContext ctx) => 0;

    [SysAbiExport(
        ExportName = "UnityShaderCompilerExtEvent",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnityShaderCompilerExtEvent(CpuContext ctx) => 0;

    [SysAbiExport(
        ExportName = "UnitySetEventQueue",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libKernel")]
    public static int UnitySetEventQueue(CpuContext ctx)
    {
        var flagIdPtr = ctx[CpuRegister.Rdi];
        if (flagIdPtr != 0)
        {
            _ = ctx.TryWriteUInt32(flagIdPtr, 1);
        }
        return 0;
    }
}
