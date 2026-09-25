// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE.Host.Windows;

namespace CraziiEmu.HLE.Host;

public static class HostViewMemory
{
    public static IHostViewMemory Create() => new WindowsHostViews();

    internal static bool IsValidRange(ulong address, ulong size) =>
        address != 0 && size != 0 && size <= ulong.MaxValue - address;

    internal static bool IsValidOffset(HostBackingObject backing, ulong offset, ulong size, ulong pageSize) =>
        offset % pageSize == 0 && offset < backing.Size && size != 0 && size <= backing.Size - offset;
}
