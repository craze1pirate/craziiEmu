// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE.Host;

public interface IHostSymbolResolver
{
    /// <summary>Returns the native address of the function, or 0 if unavailable.</summary>
    nint GetAddress(HostRuntimeFunction function);
}
