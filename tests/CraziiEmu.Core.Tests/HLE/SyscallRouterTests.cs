// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Runtime.InteropServices;
using System.Text;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using Xunit;

namespace CraziiEmu.Core.Tests.HLE;

/// <summary>
/// Contains unit tests for the <see cref="SyscallRouter"/> class.
/// </summary>
public class SyscallRouterTests
{
    private class DummyMemory : ICpuMemory
    {
        public bool TryRead(ulong address, Span<byte> destination) => throw new NotImplementedException();
        public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => throw new NotImplementedException();
        public bool TryReadString(ulong address, int maxLength, out string value) => throw new NotImplementedException();
        public void RegisterFaultHandler(ulong start, ulong end, Action<CpuMemoryFaultInfo> handler) => throw new NotImplementedException();
        public void UnregisterFaultHandler(ulong start, ulong end) => throw new NotImplementedException();
        public ulong? GetPhysicalAddress(ulong virtualAddress) => throw new NotImplementedException();
    }

    // using System.Buffer.MemoryCopy;

    /// <summary>
    /// Verifies that the sys_write syscall successfully reads from memory and fires the OnStdoutWrite event.
    /// </summary>
    [Fact]
    public unsafe void SysWrite_WithValidBuffer_TriggersOnStdoutWrite()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        var router = new SyscallRouter(vmm) { ActiveAbi = SyscallAbi.Linux };
        
        ulong bufferAddress = vmm.AllocateCpu(1024);
        string testMessage = "Hello from guest!";
        var bytes = Encoding.UTF8.GetBytes(testMessage);
        
        // Write manually into VMM using an unmanaged copy to safely trigger the VEH hook
        fixed (byte* pBytes = bytes)
        {
            System.Buffer.MemoryCopy(pBytes, vmm.GetPointer(bufferAddress), bytes.Length, bytes.Length);
        }

        var ctx = new CpuContext(new DummyMemory(), Generation.Gen5)
        {
[CpuRegister.Rax] = 1, // sys_write
[CpuRegister.Rdi] = 1, // stdout
[CpuRegister.Rsi] = bufferAddress,
[CpuRegister.Rdx] = (ulong)bytes.Length
        };

        // Act
        router.Dispatch(ctx);

        // Assert
        Assert.Equal((ulong)bytes.Length, ctx[CpuRegister.Rax]);
    }

    /// <summary>
    /// Verifies that the sys_exit syscall successfully flags the context as terminated with the exit code.
    /// </summary>
    [Fact]
    public void SysExit_WithExitCode_TerminatesContext()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        var router = new SyscallRouter(vmm) { ActiveAbi = SyscallAbi.FreeBsd };
        
        var ctx = new CpuContext(new DummyMemory(), Generation.Gen5)
        {
[CpuRegister.Rax] = 1, // sys_exit in FreeBSD
[CpuRegister.Rdi] = 42, // Exit code
            IsTerminated = false
        };

        // Act
        router.Dispatch(ctx);

        // Assert
        Assert.True(ctx.IsTerminated);
        Assert.Equal(42, ctx.ExitCode);
    }

    /// <summary>
    /// Verifies that an invalid syscall ID safely updates Rax to -ENOSYS without crashing.
    /// </summary>
    [Fact]
    public void InvalidSyscall_Linux_UpdatesRaxToEnosys()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        var router = new SyscallRouter(vmm) { ActiveAbi = SyscallAbi.Linux };
        
        var ctx = new CpuContext(new DummyMemory(), Generation.Gen5)
        {
[CpuRegister.Rax] = 9999, // Unregistered syscall
        };

        // Act
        router.Dispatch(ctx);

        // Assert
        ulong expectedEnosys = unchecked((ulong)-38);
        Assert.Equal(expectedEnosys, ctx[CpuRegister.Rax]);
    }

    /// <summary>
    /// Verifies that an invalid syscall ID under FreeBSD ABI safely updates Rax to 78 (ENOSYS) and sets the Carry flag.
    /// </summary>
    [Fact]
    public void InvalidSyscall_FreeBsd_UpdatesRaxToEnosys_SetsCarry()
    {
        // Arrange
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        var router = new SyscallRouter(vmm) { ActiveAbi = SyscallAbi.FreeBsd };
        
        var ctx = new CpuContext(new DummyMemory(), Generation.Gen5)
        {
[CpuRegister.Rax] = 9999, // Unregistered syscall
            CarryFlag = false
        };

        // Act
        router.Dispatch(ctx);

        // Assert
        Assert.Equal(78UL, ctx[CpuRegister.Rax]);
        Assert.True(ctx.CarryFlag);
    }
}
