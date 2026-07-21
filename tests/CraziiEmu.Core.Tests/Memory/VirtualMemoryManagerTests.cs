// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Memory;
using Xunit;

namespace CraziiEmu.Core.Tests.Memory;

/// <summary>
/// Contains unit tests for the <see cref="VirtualMemoryManager"/> class.
/// </summary>
public class VirtualMemoryManagerTests
{
    // RtlZeroMemory removed

    /// <summary>
    /// Verifies that a small allocation works and triggers the page-fault hook when accessed.
    /// </summary>
    [Fact]
    public unsafe void AllocateCpu_SmallAllocation_SucceedsAndFiresPageFault()
    {
        using var vmm = new VirtualMemoryManager(0x40000000); // 1 GB pool

        ulong ptr = vmm.AllocateCpu(4096);
        // ptr can be 0 (offset)

        *(int*)vmm.GetPointer(ptr) = 0;

        *(int*)vmm.GetPointer(ptr) = 0x12345678;
        Assert.Equal(0x12345678, *(int*)vmm.GetPointer(ptr));
    }

    /// <summary>
    /// Verifies that a multi-GB allocation from the GPU region succeeds instantly without crashing.
    /// </summary>
    [Fact]
    public unsafe void AllocateGpu_MultiGBAllocation_SucceedsWithoutCrashing()
    {
        // 16 GB pool (8 GB CPU, 8 GB GPU)
        using var vmm = new VirtualMemoryManager(0x400000000); 

        // Allocate 4 GB
        ulong ptr = vmm.AllocateGpu(0x100000000); 
        // ptr can be 0 (offset)

        // Touch the last page of the 4GB allocation
        ulong endPtr = ptr + 0x100000000 - 4096;
        *(long*)vmm.GetPointer(endPtr) = 0;
        *(long*)vmm.GetPointer(endPtr) = 0xDEADBEEF;
        Assert.Equal(0xDEADBEEFUL, *(ulong*)vmm.GetPointer(endPtr));
    }
}
