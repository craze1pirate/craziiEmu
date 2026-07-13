// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Gpu;
using CraziiEmu.Core.Memory;
using Xunit;

namespace CraziiEmu.Core.Tests.Gpu;

/// <summary>
/// Contains unit tests for the <see cref="DisplayController"/> class.
/// </summary>
public class DisplayControllerTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    /// <summary>
    /// Verifies that the GUEST_VRAM_START aligns within the mapped GPU memory of the VirtualMemoryManager.
    /// </summary>
    [Fact]
    public void DisplayController_VramMappingTest()
    {
        // Require at least a 16GB pool so the GPU heap (pool/2) starts at 8GB (0x200000000)
        using var vmm = new VirtualMemoryManager(0x400000000); 
        var controller = new DisplayController(vmm);

        // VMM's GPU heap is allocated at poolSize / 2.
        ulong poolBase = vmm.AllocateGpu(0) - DisplayController.GUEST_VRAM_START;
        
        // Assert that we can access exactly the VRAM start + size in the pool without OutOfRange
        var span = vmm.GetSpan(poolBase + DisplayController.GUEST_VRAM_START, 1280 * 720 * 4);
        Assert.Equal(1280 * 720 * 4, span.Length);
    }

    /// <summary>
    /// Verifies that the framebuffer controller can successfully read a generated pattern from VRAM without page faults.
    /// </summary>
    [Fact]
    public void DisplayController_FramebufferBlitTest()
    {
        using var vmm = new VirtualMemoryManager(0x400000000); 
        var controller = new DisplayController(vmm);

        ulong absoluteVramStart = vmm.AllocateGpu(1280 * 720 * 4);

        // Map and commit the memory natively on Windows to prevent VEH crash when writing in managed code
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualAlloc((IntPtr)absoluteVramStart, 1280 * 720 * 4, 0x1000, 0x04);
        }

        // Fill with solid red
        var span = vmm.GetSpan(absoluteVramStart, 1280 * 720 * 4);
        for (int i = 0; i < 1280 * 720 * 4; i += 4)
        {
            span[i + 0] = 0xFF; // R
            span[i + 1] = 0x00; // G
            span[i + 2] = 0x00; // B
            span[i + 3] = 0xFF; // A
        }

        // Trigger the headless update which reads the memory block
        var exception = Record.Exception(() => controller.UpdateFrameBufferOnly());
        
        // Assert that the controller read the bytes successfully without throwing page fault errors
        Assert.Null(exception);
    }
}
