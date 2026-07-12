// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Gpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.HLE;
using Xunit;

namespace CraziiEmu.Core.Tests.Gpu;

/// <summary>
/// End-to-end integration tests combining the EmulatorEngine, DisplayController, and VirtualMemoryManager.
/// </summary>
public class GraphicalIntegrationTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    /// <summary>
    /// Connects all emulator components, writes a homebrew payload to memory, executes it, and verifies visual frame buffer output.
    /// </summary>
    [Fact]
    public void GraphicalHomebrewPayload_ExecutesAndRendersCorrectly()
    {
        // 1. Initialize VirtualMemoryManager with 16GB pool
        using var vmm = new VirtualMemoryManager(0x400000000);

        // 2. Initialize SyscallRouter (Linux ABI)
        var router = new SyscallRouter(vmm)
        {
            ActiveAbi = SyscallAbi.Linux
        };

        // 3. Initialize DisplayController
        var display = new DisplayController(vmm);

        // 4. Initialize EmulatorEngine
        var engine = new EmulatorEngine(vmm, router);

        // Calculate absolute host addresses
        // Using AllocateCpu(0) safely queries the base since offset is 0.
        ulong poolBase = vmm.AllocateCpu(0);
        ulong absoluteVramStart = vmm.AllocateGpu(0);
        ulong entryPoint = poolBase + 0x401790; // Standard ELF entry point

        // Commit memory if on Windows to prevent VEH crash
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Commit entry point for the executable bytecode
            ulong entryPage = entryPoint & ~0xFFFUL; // Align to 4KB boundary
            VirtualAlloc((IntPtr)entryPage, 4096, 0x1000, 0x04);
            
            // Commit VRAM for the visual output
            VirtualAlloc((IntPtr)absoluteVramStart, 1280 * 720 * 4, 0x1000, 0x04);
        }

        // Write the exact bytecode sequence
        byte[] payload = new byte[]
        {
            0x48, 0xC7, 0xC7, 0x00, 0x00, 0x00, 0x00, // mov rdi, 0 (or 0x200000000)
            0x48, 0xC7, 0xC0, 0xFF, 0x00, 0x00, 0x00, // mov rax, 0x000000FF
            0x48, 0x89, 0x07,                         // mov [rdi], rax
            0x48, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00, // mov rax, 1 (sys_exit)
            0x48, 0xC7, 0xC7, 0x00, 0x00, 0x00, 0x00, // mov rdi, 0 (exit status 0)
            0x0F, 0x05                                // syscall
        };

        var codeSpan = vmm.GetSpan(entryPoint, payload.Length);
        payload.CopyTo(codeSpan);

        // Execute Payload
        engine.Run(entryPoint);

        // Verify Execution
        Assert.True(engine.Context.IsTerminated);
        Assert.Equal(0u, engine.Context.Rdi); // Status code 0

        // Trigger Framebuffer Update
        var exception = Record.Exception(() => display.UpdateFrameBufferOnly());
        Assert.Null(exception); // Should read cleanly without OS page faults

        // Verify VRAM Content
        var vramSpan = vmm.GetSpan(absoluteVramStart, 8);
        ulong pixelData = BinaryPrimitives.ReadUInt64LittleEndian(vramSpan);
        
        // As defined in the payload: mov rax, 0xFF
        Assert.Equal(0x000000FFUL, pixelData);
    }
}
