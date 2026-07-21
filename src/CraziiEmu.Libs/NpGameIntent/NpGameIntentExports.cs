// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.NpGameIntent;

public static class NpGameIntentExports
{
    private static int _initialized;

    [SysAbiExport(
        Nid = "m87BHxt-H60",
        ExportName = "sceNpGameIntentInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpGameIntent")]
    public static int NpGameIntentInitialize(CpuContext ctx)
    {
        Interlocked.Exchange(ref _initialized, 1);
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }
}
