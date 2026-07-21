// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core.Loader;

namespace CraziiEmu.Core.Memory;

public readonly struct VirtualMemoryRegion
{
    public VirtualMemoryRegion(
        ulong virtualAddress,
        ulong memorySize,
        ulong fileOffset,
        ulong fileSize,
        ProgramHeaderFlags protection)
    {
        VirtualAddress = virtualAddress;
        MemorySize = memorySize;
        FileOffset = fileOffset;
        FileSize = fileSize;
        Protection = protection;
    }

    public ulong VirtualAddress { get; }

    public ulong MemorySize { get; }

    public ulong FileOffset { get; }

    public ulong FileSize { get; }

    public ProgramHeaderFlags Protection { get; }
}
