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
/// Tests for stack manipulation (push/pop) and comparison/conditional-jump
/// instruction handlers in <see cref="EmulatorEngine"/>.
/// </summary>
public class StackAndLoopInstructionTests
{
    // using System.Buffer.MemoryCopy for memory operations

    /// <summary>
    /// Verifies that a push followed by a pop preserves the register value
    /// and returns RSP to its original address.
    /// </summary>
    [Fact]
    public unsafe void Step_PushPop_PreservesValueAndRestoresRsp()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1 MB pool
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        // Allocate code region
        ulong codeBase = vmm.AllocateCpu(1024);

        // Allocate a separate stack region so RSP points to valid committed memory.
        // The stack grows downward, so set RSP to the *top* of the allocation.
        const int StackSize = 4096;
        ulong stackBase = vmm.AllocateCpu(StackSize);
        ulong stackTop = stackBase + StackSize;

        // Inject: push rbp (55) + pop rbp (5D)
        byte[] code = new byte[] { 0x55, 0x5D };
        fixed (byte* p = code)
        {
            System.Buffer.MemoryCopy(p, vmm.GetPointer(codeBase), code.Length, code.Length);
        }

        engine.Context.Rip = codeBase;
        engine.Context[CraziiEmu.HLE.CpuRegister.Rsp] = stackTop;
        engine.Context[CraziiEmu.HLE.CpuRegister.Rbp] = 0xDEAD_BEEF_CAFE_BABE;

        // Act — step push, then step pop
        engine.Step(); // push rbp
        engine.Step(); // pop rbp

        // Assert — value preserved, RSP restored
        Assert.Equal(0xDEAD_BEEF_CAFE_BABEUL, engine.Context[CraziiEmu.HLE.CpuRegister.Rbp]);
        Assert.Equal(stackTop, engine.Context[CraziiEmu.HLE.CpuRegister.Rsp]);
    }

    /// <summary>
    /// Verifies that cmp + jne correctly jumps to the target address when the
    /// two operands are not equal, skipping over intermediate instructions.
    /// </summary>
    [Fact]
    public unsafe void Step_CmpAndJne_JumpsWhenNotEqual()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000);
        var router = new SyscallRouter(vmm);
        var engine = new EmulatorEngine(vmm, router);

        ulong codeBase = vmm.AllocateCpu(1024);

        // Layout:
        //   offset 0: cmp rdi, rax   (48 39 C7)       — 3 bytes
        //   offset 3: jne +2         (75 02)           — 2 bytes  (target = offset 7)
        //   offset 5: nop            (90)              — 1 byte   (skipped if jump taken)
        //   offset 6: nop            (90)              — 1 byte   (skipped if jump taken)
        //   offset 7: <target>       — this is where RIP should land
        byte[] code = new byte[]
        {
            0x48, 0x39, 0xC7,   // cmp rdi, rax
            0x75, 0x02,         // jne +2 (skip 2 bytes to offset 7)
            0x90,               // nop (should be skipped)
            0x90                // nop (should be skipped)
        };

        fixed (byte* p = code)
        {
            System.Buffer.MemoryCopy(p, vmm.GetPointer(codeBase), code.Length, code.Length);
        }

        engine.Context.Rip = codeBase;
        engine.Context[CpuRegister.Rdi] = 5;
        engine.Context[CpuRegister.Rax] = 10; // Not equal → jne should fire

        // Act — step cmp, then step jne
        engine.Step(); // cmp rdi, rax → sets ZF=false
        engine.Step(); // jne +2      → condition met, jumps

        // Assert — RIP should be at target (codeBase + 7), not codeBase + 5
        Assert.Equal(codeBase + 7, engine.Context.Rip);
        Assert.False(engine.Context.ZeroFlag); // 5 != 10
    }
}
