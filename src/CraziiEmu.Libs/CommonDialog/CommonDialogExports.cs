// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.CommonDialog;

public static class CommonDialogExports
{
    private const int AlreadySystemInitialized = unchecked((int)0x80B80002);
    private static int _initialized;

    [SysAbiExport(
        Nid = "uoUpLGNkygk",
        ExportName = "sceCommonDialogInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceCommonDialog")]
    public static int CommonDialogInitialize(CpuContext ctx)
    {
        var result = Interlocked.Exchange(ref _initialized, 1) == 0
            ? 0
            : AlreadySystemInitialized;
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    [SysAbiExport(
        Nid = "BQ3tey0JmQM",
        ExportName = "sceCommonDialogIsUsed",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceCommonDialog")]
    public static int CommonDialogIsUsed(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }
}
