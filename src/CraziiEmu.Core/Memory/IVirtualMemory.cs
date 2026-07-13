// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core.Loader;
using CraziiEmu.HLE;

namespace CraziiEmu.Core.Memory;

public interface IVirtualMemory : ICpuMemory
{
    void Clear();

    void Map(ulong virtualAddress, ulong memorySize, ulong fileOffset, ReadOnlySpan<byte> fileData, ProgramHeaderFlags protection);

    IReadOnlyList<VirtualMemoryRegion> SnapshotRegions();
}
