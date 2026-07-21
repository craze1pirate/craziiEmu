// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Libs.Gpu.Vulkan;

namespace CraziiEmu.Libs.Gpu;

/// <summary>
/// Process-wide access point for the guest-GPU backend, mirroring HostPlatform for the
/// host seam: static HLE export classes resolve the renderer through <see cref="Current"/>.
/// Vulkan is the default everywhere; CRAZIIEMU_GPU_BACKEND=metal opts into the Metal
/// backend (macOS only) while it is being brought up. macOS flips to Metal by default
/// once the presenter reaches parity.
/// </summary>
internal static class GuestGpu
{
    private static readonly Lazy<IGuestGpuBackend> Instance = new(Create);

    public static IGuestGpuBackend Current => Instance.Value;

    private static IGuestGpuBackend Create()
    {
        return new VulkanGuestGpuBackend();
    }
}
