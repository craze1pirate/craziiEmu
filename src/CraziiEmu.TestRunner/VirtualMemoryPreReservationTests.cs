// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;

namespace CraziiEmu.TestRunner;

public static class VirtualMemoryPreReservationTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running VirtualMemoryPreReservationTests...");

        TestHostMemoryCapabilities();
        TestPreReserveGuestRangeAndCommit();
        TestDisposeReleasesReservations();

        Console.WriteLine("[TEST] VirtualMemoryPreReservationTests PASSED successfully.");
    }

    private static void TestHostMemoryCapabilities()
    {
        bool supported = HostMemory.IsVirtualAlloc2Supported;
        Console.WriteLine($"  [INFO] HostMemory.IsVirtualAlloc2Supported = {supported}");

        Assert(HostMemory.MEM_RESERVE_PLACEHOLDER == 0x00040000, "MEM_RESERVE_PLACEHOLDER constant mismatch");
        Assert(HostMemory.MEM_REPLACE_PLACEHOLDER == 0x00004000, "MEM_REPLACE_PLACEHOLDER constant mismatch");
        Assert(HostMemory.MEM_PRESERVE_PLACEHOLDER == 0x00000002, "MEM_PRESERVE_PLACEHOLDER constant mismatch");

        Console.WriteLine("  [PASS] TestHostMemoryCapabilities");
    }

    private static void TestPreReserveGuestRangeAndCommit()
    {
        using var vm = new PhysicalVirtualMemory();

        Assert(vm.IsAddressInPreReservedGuestSpace(0x0000_0001_0000_0000UL), "4 GiB should be recognized in pre-reserve space");
        Assert(vm.IsAddressInPreReservedGuestSpace(0x0000_0070_0000_0000UL), "Guest space should cover high addresses");
        Assert(!vm.IsAddressInPreReservedGuestSpace(0x0000_0000_8000_0000UL), "Low memory (<4GB) should not be in pre-reserve space");

        if (OperatingSystem.IsWindows())
        {
            // Pre-reserve a 4 GiB range: from 0x1_0000_0000 to 0x2_0000_0000 in 1 GiB chunks
            int chunks = vm.PreReserveGuestAddressRange(
                startAddress: 0x0000_0001_0000_0000UL,
                endAddress: 0x0000_0002_0000_0000UL,
                chunkSize: 0x0000_0000_4000_0000UL);

            Assert(chunks > 0, "Expected at least one pre-reserved chunk on Windows");
            Assert(vm.PreReservedBases.Count > 0, "PreReservedBases should contain recorded chunk bases");

            // Allocate and commit memory directly inside the pre-reserved chunk
            ulong targetAddress = 0x0000_0001_0000_0000UL;
            ulong allocSize = 0x10000UL; // 64 KiB
            bool allocated = vm.TryAllocateAtExact(targetAddress, allocSize, executable: false, out var actualAddress);
            Assert(allocated, "TryAllocateAtExact should succeed inside pre-reserved chunk");
            Assert(actualAddress == targetAddress, "Allocated address must match target exactly");

            // Test read/write to committed pages
            byte[] testPattern = [0x11, 0x22, 0x33, 0x44, 0x55, 0xAA, 0xBB, 0xCC];
            Assert(vm.TryWrite(actualAddress, testPattern), "TryWrite to committed chunk failed");

            byte[] readBack = new byte[testPattern.Length];
            Assert(vm.TryRead(actualAddress, readBack), "TryRead from committed chunk failed");
            for (int i = 0; i < testPattern.Length; i++)
            {
                Assert(readBack[i] == testPattern[i], $"Mismatch at byte {i}: expected {testPattern[i]:X2}, got {readBack[i]:X2}");
            }

            // Decommit range back to reserved state
            bool decommitted = vm.TryDecommitRange(actualAddress, allocSize);
            Assert(decommitted, "TryDecommitRange within pre-reserved chunk should succeed");
        }

        Console.WriteLine("  [PASS] TestPreReserveGuestRangeAndCommit");
    }

    private static void TestDisposeReleasesReservations()
    {
        var vm = new PhysicalVirtualMemory();
        if (OperatingSystem.IsWindows())
        {
            vm.PreReserveGuestAddressRange(
                startAddress: 0x0000_0002_0000_0000UL,
                endAddress: 0x0000_0002_4000_0000UL,
                chunkSize: 0x0000_0000_4000_0000UL);
        }

        // Dispose should clear all pre-reserved chunks without error
        vm.Dispose();
        Assert(vm.PreReservedBases.Count == 0, "PreReservedBases should be empty after Dispose");

        Console.WriteLine("  [PASS] TestDisposeReleasesReservations");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERTION FAILED] {message}");
        }
    }
}
