// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public readonly struct CpuMemoryAccessFailure
{
    public CpuMemoryAccessFailure(ulong address, int size, bool isWrite)
    {
        Address = address;
        Size = size;
        IsWrite = isWrite;
    }

    public ulong Address { get; }

    public int Size { get; }

    public bool IsWrite { get; }
}
