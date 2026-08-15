// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Libs.Gpu.Vulkan;

namespace CraziiEmu.Libs.Gpu;

/// <summary>
/// Process-wide access point for the Vulkan guest-GPU backend.
/// </summary>
internal static class GuestGpu
{
    private static readonly Lazy<IGuestGpuBackend> Instance = new(static () => new VulkanGuestGpuBackend());

    public static IGuestGpuBackend Current => Instance.Value;
}
