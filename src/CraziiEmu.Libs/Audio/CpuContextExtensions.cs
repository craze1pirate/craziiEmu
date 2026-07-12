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
