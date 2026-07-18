// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Libs.Gpu.Vulkan;

namespace CraziiEmu.Libs.Gpu;

/// <summary>
/// Process-wide access point for the guest-GPU backend, mirroring HostPlatform for the
/// host seam: static HLE export classes resolve the renderer through <see cref="Current"/>.
/// Vulkan is the only backend today; Metal/DX12 slot in here.
/// </summary>
internal static class GuestGpu
{
    private static readonly Lazy<IGuestGpuBackend> Instance = new(static () => new VulkanGuestGpuBackend());

    public static IGuestGpuBackend Current => Instance.Value;
}
