// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using System.Text;

namespace CraziiEmu.HLE;

public static class HleDataSymbols
{
    private const string StackChkGuardNid = "f7uOxY9mM1U";
    private const string ProgNameNid = "djxxOmW6-aw";
    private const string LibcNeedFlagNid = "P330P3dFF68";
    private const string LibcInternalNeedFlagNid = "ZT4ODD2Ts9o";
    private const int ProgNameMaxBytes = 511;
    private const ulong StackChkGuardValue = 0xC0DEC0DECAFEBABEUL;

    private static readonly object _gate = new();
    private static ulong _stackChkGuardAddress;
    private static ulong _progNameBufferAddress;
    private static ulong _progNamePointerAddress;
    private static ulong _libcNeedFlagAddress;
    private static ulong _libcInternalNeedFlagAddress;
    private static bool _isInitialized;

    public static void InitializeGuestMemory(IGuestMemoryAllocator allocator, ICpuMemory memory)
    {
        lock (_gate)
        {
            if (_isInitialized) return;

            _stackChkGuardAddress = AllocateGuest(allocator, memory, sizeof(ulong) * 2);
            _progNameBufferAddress = AllocateGuest(allocator, memory, ProgNameMaxBytes + 1);
            _progNamePointerAddress = AllocateGuest(allocator, memory, 8);
            _libcNeedFlagAddress = AllocateGuest(allocator, memory, sizeof(uint));
            _libcInternalNeedFlagAddress = AllocateGuest(allocator, memory, sizeof(uint));

            if (_stackChkGuardAddress != 0)
            {
                Span<byte> buf = stackalloc byte[16];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(0, 8), StackChkGuardValue);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(8, 8), StackChkGuardValue);
                memory.TryWrite(_stackChkGuardAddress, buf);
            }

            if (_libcNeedFlagAddress != 0)
            {
                Span<byte> buf = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf, 1);
                memory.TryWrite(_libcNeedFlagAddress, buf);
            }

            if (_libcInternalNeedFlagAddress != 0)
            {
                Span<byte> buf = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buf, 1);
                memory.TryWrite(_libcInternalNeedFlagAddress, buf);
            }

            _isInitialized = true;
        }

        ConfigureProcessImageName("eboot.bin", memory);
    }

    public static IEnumerable<string> EnumerateKnownNids()
    {
        yield return StackChkGuardNid;
        yield return ProgNameNid;
        yield return LibcNeedFlagNid;
        yield return LibcInternalNeedFlagNid;
    }

    public static void ConfigureProcessImageName(string? processImageName, ICpuMemory? memory = null)
    {
        var effectiveName = string.IsNullOrWhiteSpace(processImageName)
            ? "eboot.bin"
            : processImageName;
        var encodedName = Encoding.UTF8.GetBytes(effectiveName);
        var byteCount = Math.Min(encodedName.Length, ProgNameMaxBytes);

        lock (_gate)
        {
            if (_progNameBufferAddress == 0 || _progNamePointerAddress == 0 || memory == null)
            {
                return;
            }

            Span<byte> emptyBytes = new byte[ProgNameMaxBytes + 1];
            memory.TryWrite(_progNameBufferAddress, emptyBytes);
            memory.TryWrite(_progNameBufferAddress, encodedName.AsSpan(0, byteCount));

            Span<byte> pointerBytes = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(pointerBytes, _progNameBufferAddress);
            memory.TryWrite(_progNamePointerAddress, pointerBytes);
        }
    }

    public static bool TryGetAddress(string nid, out ulong address)
    {
        var pointer = nid switch
        {
            StackChkGuardNid => _stackChkGuardAddress,
            ProgNameNid => _progNamePointerAddress,
            LibcNeedFlagNid => _libcNeedFlagAddress,
            LibcInternalNeedFlagNid => _libcInternalNeedFlagAddress,
            _ => 0UL,
        };

        if (pointer == 0)
        {
            address = 0;
            return false;
        }

        address = pointer;
        return true;
    }

    private static ulong AllocateGuest(IGuestMemoryAllocator allocator, ICpuMemory memory, int size)
    {
        if (allocator.TryAllocateGuestMemory((ulong)size, 16, out var address))
        {
            memory.TryWrite(address, new byte[size]);
            return address;
        }
        return 0;
    }

    private static void WritePointer(nint target, nint value)
    {
        if (nint.Size == sizeof(int))
        {
            Marshal.WriteInt32(target, value.ToInt32());
            return;
        }

        Marshal.WriteInt64(target, value.ToInt64());
    }
}
