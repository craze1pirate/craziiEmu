// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Network;

public static class RudpExports
{
    public static ulong EventHandler { get; private set; }
    public static ulong EventArg { get; private set; }
    public static bool IsInitialized { get; private set; }

    [SysAbiExport(
        Nid = "amuBfI-AQc4",
        ExportName = "sceRudpInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceRudp")]
    public static int RudpInit(CpuContext ctx)
    {
        var memPool = ctx[CpuRegister.Rdi];
        var memPoolSize = unchecked((int)ctx[CpuRegister.Rsi]);

        IsInitialized = true;
        return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "6PBNpsgyaxw",
        ExportName = "sceRudpEnableInternalIOThread",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceRudp")]
    public static int RudpEnableInternalIOThread(CpuContext ctx)
    {
        var stackSize = unchecked((uint)ctx[CpuRegister.Rdi]);
        var priority = unchecked((uint)ctx[CpuRegister.Rsi]);

        return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "SUEVes8gvmw",
        ExportName = "sceRudpSetEventHandler",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceRudp")]
    public static int RudpSetEventHandler(CpuContext ctx)
    {
        var handler = ctx[CpuRegister.Rdi];
        var arg = ctx[CpuRegister.Rsi];

        EventHandler = handler;
        EventArg = arg;

        return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_OK);
    }
}
