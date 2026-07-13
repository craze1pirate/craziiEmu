// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Buffers.Binary;

namespace CraziiEmu.Libs.Np;

public static class NpManagerExports
{
    private const int NpTitleIdSize = 16;
    private const int NpTitleSecretSize = 128;

    [SysAbiExport(
        Nid = "3Zl8BePTh9Y",
        ExportName = "sceNpCheckCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallback(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "JELHf4xPufo",
        ExportName = "sceNpCheckCallbackForLib",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallbackForLib(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "VfRSmPmj8Q8",
        ExportName = "sceNpRegisterStateCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallback(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "qQJfO8HAiaY",
        ExportName = "sceNpRegisterStateCallbackA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallbackA(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "0c7HbXRKUt4",
        ExportName = "sceNpRegisterStateCallbackForToolkit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManagerForToolkit")]
    public static int NpRegisterStateCallbackForToolkit(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "eQH7nWPcAgc",
        ExportName = "sceNpGetState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetState(CpuContext ctx)
    {
        var stateAddress = ctx[CpuRegister.Rsi];
        if (stateAddress == 0)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> stateBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(stateBytes, 1);
        return ctx.Memory.TryWrite(stateAddress, stateBytes)
            ? SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK)
            : SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "Ec63y59l9tw",
        ExportName = "sceNpSetNpTitleId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpSetNpTitleId(CpuContext ctx)
    {
        var titleIdAddress = ctx[CpuRegister.Rdi];
        var titleSecretAddress = ctx[CpuRegister.Rsi];
        if (titleIdAddress == 0 || titleSecretAddress == 0)
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> titleId = stackalloc byte[NpTitleIdSize];
        Span<byte> titleSecret = stackalloc byte[NpTitleSecretSize];
        if (!ctx.Memory.TryRead(titleIdAddress, titleId) ||
            !ctx.Memory.TryRead(titleSecretAddress, titleSecret))
        {
            return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TraceNp($"set_np_title_id title='{ReadTitleId(titleId)}'");
        return SetReturn(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int SetReturn(CpuContext ctx, OrbisGen2Result result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)(int)result);
        return (int)result;
    }

    private static string ReadTitleId(ReadOnlySpan<byte> bytes)
    {
        var length = 0;
        while (length < 12 && length < bytes.Length && bytes[length] != 0)
        {
            length++;
        }

        return length == 0
            ? string.Empty
            : System.Text.Encoding.ASCII.GetString(bytes[..length]);
    }

    private static void TraceNp(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CraziiEmu_LOG_NP"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine($"[LOADER][TRACE] np.{message}");
    }
}
