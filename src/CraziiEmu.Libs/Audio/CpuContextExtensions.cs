// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System;
using System.Buffers.Binary;

namespace CraziiEmu.Libs.Audio;

internal static class CpuContextExtensions
{
    public static int SetReturn(this CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    public static bool TryWriteUInt64(this CpuContext ctx, ulong address, ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        return ctx.Memory.TryWrite(address, buffer);
    }
}
