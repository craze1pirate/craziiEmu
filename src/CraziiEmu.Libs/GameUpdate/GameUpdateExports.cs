// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.GameUpdate;

public static class GameUpdateExports
{
    private static int _initialized;

    [SysAbiExport(
        Nid = "YJtKLttI9fM",
        ExportName = "sceGameUpdateInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceGameUpdate")]
    public static int GameUpdateInitialize(CpuContext ctx)
    {
        Interlocked.Exchange(ref _initialized, 1);
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }
}
