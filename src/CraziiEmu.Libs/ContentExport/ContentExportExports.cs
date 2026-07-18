// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;

namespace CraziiEmu.Libs.ContentExport;

// No host media library exists to export captures into, so initialization
// reports success.
public static class ContentExportExports
{
    [SysAbiExport(
        Nid = "0GnN4QCgIfs",
        ExportName = "sceContentExportInit2",
        Target = Generation.Gen5,
        LibraryName = "libSceContentExport")]
    public static int ContentExportInit2(CpuContext ctx) => ctx.SetReturn(0);
}
