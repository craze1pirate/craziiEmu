// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;
using Xunit;

namespace CraziiEmu.Core.Tests.HLE;

/// <summary>
/// Contains unit tests for the <see cref="LibKernelHle"/> class.
/// </summary>
public class LibKernelHleTests
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    private ulong GetRegisteredStubAddress(DynamicLinker linker, string module, string symbol)
    {
        var hleStubsField = typeof(DynamicLinker).GetField("_hleStubs", BindingFlags.NonPublic | BindingFlags.Instance);
        var stubs = hleStubsField!.GetValue(linker) as System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, ulong>>;
        return stubs![module][symbol];
    }

    /// <summary>
    /// Verifies that sceKernelUsleep suspends execution for the expected duration.
    /// </summary>
    [Fact]
    public void sceKernelUsleep_SuspendsExecution()
    {
        using var vmm = new VirtualMemoryManager(0x100000);
        var linker = new DynamicLinker(vmm, _ => null);
        var hle = new LibKernelHle(vmm, linker);

        ulong address = GetRegisteredStubAddress(linker, "libkernel.sprx", "sceKernelUsleep");
        var delegateInstance = Marshal.GetDelegateForFunctionPointer<LibKernelHle.UsleepDelegate>((IntPtr)address);

        var sw = Stopwatch.StartNew();
        delegateInstance(50000); // 50 milliseconds
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 40, $"Elapsed was {sw.ElapsedMilliseconds} ms, expected >= 40 ms.");
    }

    /// <summary>
    /// Verifies that sceKernelGettimeofday correctly writes the current UTC time to the provided virtual address.
    /// </summary>
    [Fact]
    public void sceKernelGettimeofday_WritesTimeval()
    {
        using var vmm = new VirtualMemoryManager(0x100000);
        var linker = new DynamicLinker(vmm, _ => null);
        var hle = new LibKernelHle(vmm, linker);

        ulong address = GetRegisteredStubAddress(linker, "libkernel.sprx", "sceKernelGettimeofday");
        var delegateInstance = Marshal.GetDelegateForFunctionPointer<LibKernelHle.GetTimeOfDayDelegate>((IntPtr)address);

        ulong timevalAddr = vmm.AllocateCpu(16);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualAlloc((IntPtr)timevalAddr, 16, 0x1000, 0x04);
        }
        
        int result = delegateInstance(timevalAddr, 0);
        Assert.Equal(0, result);

        var span = vmm.GetSpan(timevalAddr, 12);
        ulong seconds = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(0, 8));
        uint microseconds = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));

        var now = DateTimeOffset.UtcNow;
        ulong currentSeconds = (ulong)now.ToUnixTimeSeconds();
        
        // Assert the returned seconds is close to current (within 5 seconds)
        Assert.True(Math.Abs((long)seconds - (long)currentSeconds) <= 5);
        Assert.True(microseconds < 1000000); // max 999,999 us
    }

    /// <summary>
    /// Verifies that sceKernelAllocateDirectMemory allocates memory in the VMM and writes the address out.
    /// </summary>
    [Fact]
    public void sceKernelAllocateDirectMemory_AllocatesMemory()
    {
        using var vmm = new VirtualMemoryManager(0x100000);
        var linker = new DynamicLinker(vmm, _ => null);
        var hle = new LibKernelHle(vmm, linker);

        ulong address = GetRegisteredStubAddress(linker, "libkernel.sprx", "sceKernelAllocateDirectMemory");
        var delegateInstance = Marshal.GetDelegateForFunctionPointer<LibKernelHle.AllocateDirectMemoryDelegate>((IntPtr)address);

        ulong outAddr = vmm.AllocateCpu(8);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualAlloc((IntPtr)outAddr, 8, 0x1000, 0x04);
        }
        
        int result = delegateInstance(0, 0, 4096, 4096, 0, outAddr);
        Assert.Equal(0, result);

        var span = vmm.GetSpan(outAddr, 8);
        ulong allocatedAddress = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span);

        Assert.NotEqual(0UL, allocatedAddress);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualAlloc((IntPtr)allocatedAddress, 4096, 0x1000, 0x04);
        }
        
        // Verify we can write to the allocated address
        var allocSpan = vmm.GetSpan(allocatedAddress, 4096);
        allocSpan[0] = 0xAA;
        Assert.Equal(0xAA, allocSpan[0]);
    }
}
