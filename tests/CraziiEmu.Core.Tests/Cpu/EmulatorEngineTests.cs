using CraziiEmu.HLE;
// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Memory;
using Xunit;

namespace CraziiEmu.Core.Tests.Cpu;

/// <summary>
/// Contains unit tests for the <see cref="EmulatorEngine"/> class.
/// </summary>
public class EmulatorEngineTests
{
    [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
    private static extern IntPtr memcpy(IntPtr dest, IntPtr src, nuint count);

    /// <summary>
    /// Verifies that the engine correctly decodes and executes a single 'mov rax, 1' instruction.
    /// </summary>
    [Fact]
    public unsafe void Step_WithMovRaxImmediate_AdvancesRipAndSetsRegister()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        ulong entryPoint = vmm.AllocateCpu(1024); // Allocate a 1KB page for our code

        // Construct: mov rax, 1 (48 C7 C0 01 00 00 00)
        byte[] code = new byte[] { 0x48, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00 };

        fixed (byte* pCode = code)
        {
            memcpy((IntPtr)entryPoint, (IntPtr)pCode, (nuint)code.Length);
        }

        engine.Context.Rip = entryPoint;

        // Act
        engine.Step();

        // Assert
        Assert.Equal(entryPoint + 7, engine.Context.Rip);
        Assert.Equal(1UL, engine.Context[CpuRegister.Rax]);
    }

    /// <summary>
    /// Verifies that a full execution loop successfully translates a sequence setting exit code 42 and halting.
    /// </summary>
    [Fact]
    public unsafe void Run_WithSyscallPayload_TerminatesCorrectly()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000);
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        ulong entryPoint = vmm.AllocateCpu(1024);

        // Construct payload:
        // mov rax, 1 -> 48 C7 C0 01 00 00 00
        // mov rdi, 42 -> 48 C7 C7 2A 00 00 00
        // syscall     -> 0F 05
        byte[] payload = new byte[]
        {
            0x48, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00, // mov rax, 1 (sys_exit)
            0x48, 0xC7, 0xC7, 0x2A, 0x00, 0x00, 0x00, // mov rdi, 42 (exit_code)
            0x0F, 0x05                                // syscall
        };

        fixed (byte* pPayload = payload)
        {
            memcpy((IntPtr)entryPoint, (IntPtr)pPayload, (nuint)payload.Length);
        }

        // Act
        engine.Run(entryPoint);

        // Assert
        Assert.True(engine.Context.IsTerminated);
        Assert.Equal(42, engine.Context.ExitCode);
        Assert.Equal(entryPoint + 16, engine.Context.Rip);
    }

    /// <summary>
    /// Verifies that an unknown instruction sequence safely throws InvalidOpcodeException.
    /// </summary>
    [Fact]
    public unsafe void Step_WithInvalidOpcode_ThrowsException()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000);
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        ulong entryPoint = vmm.AllocateCpu(1024);

        // Inject garbage bytes (0xFF 0xFF)
        byte[] garbage = new byte[] { 0xFF, 0xFF };
        fixed (byte* pGarbage = garbage)
        {
            memcpy((IntPtr)entryPoint, (IntPtr)pGarbage, (nuint)garbage.Length);
        }

        engine.Context.Rip = entryPoint;

        // Act & Assert
        var ex = Assert.Throws<InvalidOpcodeException>(() => engine.Step());
        
        Assert.Equal(entryPoint, ex.Rip);
    }

    /// <summary>
    /// Verifies that the engine correctly decodes and executes an 'add rax, rdi' instruction.
    /// </summary>
    [Fact]
    public unsafe void Step_WithAdd_UpdatesRegisterAndAdvancesRip()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000);
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        ulong entryPoint = vmm.AllocateCpu(1024);

        // Construct: add rax, rdi (48 01 F8)
        byte[] code = new byte[] { 0x48, 0x01, 0xF8 };

        fixed (byte* pCode = code)
        {
            memcpy((IntPtr)entryPoint, (IntPtr)pCode, (nuint)code.Length);
        }

        engine.Context.Rip = entryPoint;
        engine.Context[CpuRegister.Rax] = 10;
        engine.Context[CpuRegister.Rdi] = 5;

        // Act
        engine.Step();

        // Assert
        Assert.Equal(entryPoint + 3, engine.Context.Rip);
        Assert.Equal(15UL, engine.Context[CpuRegister.Rax]);
    }
}
