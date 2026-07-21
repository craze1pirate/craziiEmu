// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Ajm;

public static class AjmExports
{
    [SysAbiExport(
        Nid = "dl+4eHSzUu4",
        ExportName = "sceAjmInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceAjm")]
    public static int Initialize(CpuContext ctx) =>
        CraziiEmu.Libs.Audio.AjmExports.AjmInitialize(ctx);
}
