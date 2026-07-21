// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Voice;

public static class VoiceQoSExports
{
    [SysAbiExport(
        Nid = "U8IfNl6-Css",
        ExportName = "sceVoiceQoSInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVoiceQoS")]
    public static int VoiceQoSInit(CpuContext ctx)
    {
        return ctx.SetReturn(0);
    }
}
