// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public interface ITrackedCpuMemory
{
    CpuMemoryAccessFailure? LastFailure { get; }
}
