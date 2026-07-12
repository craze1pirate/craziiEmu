// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE;

public interface IGuestMemoryAllocator
{
    bool TryAllocateGuestMemory(ulong size, ulong alignment, out ulong address);
}
