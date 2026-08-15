// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Security.Cryptography;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Random;

public static class RandomExports
{
    private const int RandomErrorInvalid = unchecked((int)0x817C0016);
    private const int MaxRandomBytes = 64;

    [SysAbiExport(
        Nid = "PI7jIZj4pcE",
        ExportName = "sceRandomGetRandomNumber",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceRandom")]
    public static int RandomGetRandomNumber(CpuContext ctx)
    {
        var destination = ctx[CpuRegister.Rdi];
        var size = ctx[CpuRegister.Rsi];
        if ((destination == 0 && size != 0) || size > MaxRandomBytes)
        {
            return ctx.SetReturn(RandomErrorInvalid);
        }

        if (size == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
        }

        Span<byte> bytes = stackalloc byte[(int)size];
        RandomNumberGenerator.Fill(bytes);
        return ctx.Memory.TryWrite(destination, bytes)
            ? ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK)
            : ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }
}
