// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE;

/// <summary>
/// Implemented by memories that decorate another <see cref="ICpuMemory"/>
/// (e.g. access trackers) so capability lookups can unwrap to the real
/// implementation without reflection.
/// </summary>
public interface ICpuMemoryWrapper
{
    ICpuMemory Inner { get; }
}
