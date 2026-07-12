// Copyright (C) 2026 CraziiEmu Emulator Project
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
