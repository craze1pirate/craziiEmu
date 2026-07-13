// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Np;

public static class NpSessionSignalingExports
{
    [SysAbiExport(
        Nid = "ysmw6J-P8Ak",
        ExportName = "sceNpSessionSignalingInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpSessionSignaling")]
    public static int NpSessionSignalingInitialize(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }
}
