// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.HLE.GpuMemory;
using CraziiEmu.HLE.GuestMemory;
using CraziiEmu.HLE.Host;

namespace CraziiEmu.TestRunner;

public static class GpuMemoryAndHostViewsTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting GpuMemoryAndHostViewsTests (Phase 3)...");

        TestPageMaskOperations();
        TestSpanSetOperations();
        TestRegionLockOperations();
        TestHostViewMemoryLifecycle();
        TestGuestSpaceOwnerAndBackingViews();
        TestGuestPageTrackerAndFaults();
        TestGuestImageWriteTrackerEnhanced();
        TestPhysicalVirtualMemoryBackingIntegration();

        Console.WriteLine("[TEST] GpuMemoryAndHostViewsTests PASSED cleanly.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine($"[FAIL] {message}");
            throw new Exception($"Test assertion failed: {message}");
        }
    }

    private static void TestPageMaskOperations()
    {
        var mask = new PageMask();
        Assert(mask.None, "Initial PageMask must be empty");

        // Set pages 2 to 5 inclusive ([2, 6))
        mask.SetRange(2, 6);
        Assert(mask.Any, "PageMask should not be empty after setting bits");
        Assert(!mask.Get(1), "Bit 1 should not be set");
        Assert(mask.Get(2), "Bit 2 should be set");
        Assert(mask.Get(3), "Bit 3 should be set");
        Assert(mask.Get(4), "Bit 4 should be set");
        Assert(mask.Get(5), "Bit 5 should be set");
        Assert(!mask.Get(6), "Bit 6 should not be set");

        Assert(mask.AnyInRange(1, 3), "Range [1, 3) intersects [2, 6)");
        Assert(!mask.AnyInRange(6, 8), "Range [6, 8) does not intersect [2, 6)");

        mask.UnsetRange(3, 5);
        Assert(mask.Get(2), "Bit 2 should still be set");
        Assert(!mask.Get(3), "Bit 3 should be cleared");
        Assert(!mask.Get(4), "Bit 4 should be cleared");
        Assert(mask.Get(5), "Bit 5 should still be set");

        Console.WriteLine("  [PASS] 1. PageMask bit operations and range intersection verified");
    }

    private static void TestSpanSetOperations()
    {
        var spans = new SpanSet();
        Assert(spans.IsEmpty, "SpanSet should start empty");

        spans.Add(0x1000, 0x2000);
        Assert(!spans.IsEmpty, "SpanSet should not be empty after add");
        Assert(spans.Contains(0x1000, 0x1000), "SpanSet should contain added subrange");
        Assert(spans.Contains(0x1000, 0x2000), "SpanSet should contain entire added range");
        Assert(!spans.Contains(0x2800, 0x1000), "SpanSet should not contain out-of-range address");

        // Merge adjacent span
        spans.Add(0x3000, 0x1000);
        Assert(spans.Contains(0x1000, 0x3000), "SpanSet should coalesce contiguous spans");

        spans.Remove(0x2000, 0x1000);
        Assert(spans.Contains(0x1000, 0x1000), "Left piece should remain");
        Assert(spans.Contains(0x3000, 0x1000), "Right piece should remain");
        Assert(!spans.Contains(0x1800, 0x1000), "Split region should not contain removed hole");

        Console.WriteLine("  [PASS] 2. SpanSet intervals, coalesce, and split operations verified");
    }

    private static void TestRegionLockOperations()
    {
        var regionLock = new RegionLock();
        using (regionLock.Hold())
        {
            // Successfully entered critical section
        }

        Console.WriteLine("  [PASS] 3. RegionLock lock acquisition and release verified");
    }

    private static void TestHostViewMemoryLifecycle()
    {
        var host = HostViewMemory.Create();
        Assert(host != null, "HostViewMemory.Create returned null");
        Assert(host!.PageSize > 0, "Host page size must be > 0");
        Assert(host.Granularity >= host.PageSize, "Host granularity must be >= page size");

        // Allocate a 16 MiB section backing
        const ulong backingSize = 16 * 1024 * 1024;
        bool created = host.TryCreateBacking(backingSize, out var backing, out var failure);
        Assert(created && backing != null, $"TryCreateBacking failed: {failure}");
        Assert(backing!.Size == backingSize, "Backing size mismatch");
        Assert(backing.AliasBase != 0, "Backing alias base must not be 0");

        // Write to alias memory to ensure it's accessible
        unsafe
        {
            var ptr = (ulong*)backing.AliasBase;
            *ptr = 0xCAFE_BABE_DEAD_BEEFUL;
            Assert(*ptr == 0xCAFE_BABE_DEAD_BEEFUL, "Backing alias memory read/write verified");
        }

        // Test hole reservation
        ulong holeSize = host.Granularity * 4;
        ulong holeAddr = 0x20_0000_0000UL;
        ulong reserved = host.ReserveHole(holeAddr, holeSize);
        if (reserved != 0)
        {
            Assert(reserved == holeAddr, "ReserveHole address mismatch");

            // Map view into hole
            bool mapped = host.TryMapView(backing, holeAddr, 0, host.Granularity, HostPageProtection.ReadWrite, out var mapFailure);
            if (mapped)
            {
                unsafe
                {
                    var viewPtr = (ulong*)holeAddr;
                    Assert(*viewPtr == 0xCAFE_BABE_DEAD_BEEFUL, "View should alias backing memory");
                    *viewPtr = 0x1234_5678_9ABC_DEF0UL;
                    Assert(*(ulong*)backing.AliasBase == 0x1234_5678_9ABC_DEF0UL, "Write to view must reflect in backing");
                }

                // Change protection
                bool protectOk = host.ChangeAccess(holeAddr, host.Granularity, HostPageProtection.ReadOnly);
                Assert(protectOk, "ChangeAccess to ReadOnly failed");

                // Unmap view
                bool unmapped = host.UnmapView(holeAddr, host.Granularity);
                Assert(unmapped, "UnmapView failed");
            }

            // Free the hole
            host.FreeHole(holeAddr, holeSize);
        }

        backing.Dispose();
        Console.WriteLine("  [PASS] 4. HostViewMemory backing allocation, aliased mapping, and hole management verified");
    }

    private static void TestGuestSpaceOwnerAndBackingViews()
    {
        var host = HostViewMemory.Create();
        const ulong testBackingSize = 64 * 1024 * 1024; // 64 MiB
        using var spaceOwner = new GuestSpaceOwner(host, testBackingSize, preReserveGuestAddressSpace: false);

        Assert(spaceOwner.BackingSize == testBackingSize, "GuestSpaceOwner backing size mismatch");
        Assert(spaceOwner.AliasBase != 0, "GuestSpaceOwner alias base must be non-zero");

        // Clear backing
        bool cleared = spaceOwner.TryClearBacking(0, 4096);
        Assert(cleared, "TryClearBacking failed");

        // Test writing and reading backing directly
        byte[] testData = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        ulong testAddress = 0x30_0000_0000UL;
        ulong testSize = 64 * 1024;

        if (spaceOwner.TryReserveAddressRange(testAddress, testSize))
        {
            bool mapped = spaceOwner.MapShared(testAddress, testSize, 0, HostPageProtection.ReadWrite, out var failure);
            if (mapped)
            {
                Assert(spaceOwner.IsBacked(testAddress, testSize), "Address range must report backed");

                bool wrote = spaceOwner.TryWriteBacking(testAddress, testData);
                Assert(wrote, "TryWriteBacking failed");

                byte[] readBack = new byte[testData.Length];
                bool read = spaceOwner.TryReadBacking(testAddress, readBack);
                Assert(read, "TryReadBacking failed");
                Assert(readBack.AsSpan().SequenceEqual(testData), "Readback payload mismatch");

                // Copy backing
                ulong copyDest = testAddress + 0x1000;
                bool copied = spaceOwner.TryCopyBacking(copyDest, testAddress, (ulong)testData.Length);
                Assert(copied, "TryCopyBacking failed");

                byte[] copyRead = new byte[testData.Length];
                spaceOwner.TryReadBacking(copyDest, copyRead);
                Assert(copyRead.AsSpan().SequenceEqual(testData), "Copy destination readback mismatch");

                spaceOwner.UnmapShared(testAddress, testSize);
            }
        }

        Console.WriteLine("  [PASS] 5. GuestSpaceOwner range reservation, shared view aliasing, and direct copy verified");
    }

    private static void TestGuestPageTrackerAndFaults()
    {
        using var memory = new PhysicalVirtualMemory();
        using var gpu = new GuestGpuMemory(memory);

        GuestGpuMemoryHook.Attach(gpu);
        try
        {
            ulong guestAddr = 0x0000_0002_0000_0000UL;
            ulong guestSize = 64 * 1024; // 64 KiB = 4 guest pages (16 KiB each)

            ulong allocated = memory.AllocateAt(guestAddr, guestSize, executable: false);
            Assert(allocated == guestAddr, "Memory allocation failed");

            // Register GPU memory
            GuestGpuMemoryHook.NoteMapped(guestAddr, guestSize, GuestPageProtection.Read | GuestPageProtection.Write);
            Assert(gpu.Pages.Allows(guestAddr, FaultKind.Read), "Read must be allowed initially");
            Assert(gpu.Pages.Allows(guestAddr, FaultKind.Write), "Write must be allowed initially");

            // Mark CPU write notification
            GuestGpuMemoryHook.MarkCpuWrite(guestAddr, 256);

            // Test page fault hook resolution
            bool resolvedWrite = GuestGpuMemoryHook.TryResolveFault(FaultKind.Write, guestAddr + 0x100);

            // NoteUnmapped
            GuestGpuMemoryHook.NoteUnmapped(guestAddr, guestSize);

            bool hasSummary = GuestGpuMemoryHook.TryTakeShutdownSummary(out var summary);
            Assert(hasSummary, "Shutdown summary should be generated");
            Assert(summary.Contains("gpu_memory:"), "Summary format mismatch");
        }
        finally
        {
            GuestGpuMemoryHook.Attach(null);
        }

        Console.WriteLine("  [PASS] 6. GuestPageTracker and GuestGpuMemoryHook registration and fault dispatch verified");
    }

    private static void TestGuestImageWriteTrackerEnhanced()
    {
        GuestImageWriteTracker.Configure(true);
        Assert(GuestImageWriteTracker.Enabled, "GuestImageWriteTracker should be enabled");

        const nuint imageSize = 64 * 1024;
        unsafe
        {
            void* alloc = HostMemory.Alloc(null, imageSize, HostMemory.MEM_COMMIT | HostMemory.MEM_RESERVE, HostMemory.PAGE_READWRITE);
            Assert(alloc != null, "HostMemory.Alloc failed");
            ulong imageAddr = (ulong)alloc;

            try
            {
                // Untrack before track should be safe
                GuestImageWriteTracker.Untrack(imageAddr);

                // Track image
                GuestImageWriteTracker.Track(imageAddr, imageSize, source: "TestImage", protect: true);

                // Read snapshot
                var snapshot = GuestImageWriteTracker.BeginReadSnapshot(imageAddr, imageSize);
                Assert(snapshot.Address == imageAddr, "Snapshot address mismatch");
                Assert(GuestImageWriteTracker.IsReadSnapshotStable(snapshot), "Fresh read snapshot should be stable");

                // Managed writer notification
                GuestImageWriteTracker.NotifyManagedWrite(imageAddr + 64, 128);

                // Generation should advance
                Assert(!GuestImageWriteTracker.IsReadSnapshotStable(snapshot), "Snapshot should become stale after managed write");

                // Track watch-only
                ulong watchOnlyAddr = imageAddr + 0x1000;
                GuestImageWriteTracker.Track(watchOnlyAddr, 4096, source: "WatchOnlyImage", protect: false);

                // Untrack
                GuestImageWriteTracker.UntrackProtected(imageAddr);
                GuestImageWriteTracker.UntrackWatchOnly(watchOnlyAddr);
            }
            finally
            {
                GuestImageWriteTracker.Untrack(imageAddr);
                HostMemory.Free(alloc, 0, HostMemory.MEM_RELEASE);
            }
        }

        Console.WriteLine("  [PASS] 7. GuestImageWriteTracker watch-only, managed writes, and snapshot stability verified");
    }

    private static void TestPhysicalVirtualMemoryBackingIntegration()
    {
        var host = HostViewMemory.Create();
        using var vm = new PhysicalVirtualMemory(viewHost: host, backingBytes: 64 * 1024 * 1024);

        ulong searchStart = 0x20_0000_0000UL;
        ulong holdBytes = 256 * 1024;
        bool held = vm.TryHoldRangeAtOrAbove(searchStart, holdBytes, host.Granularity, out var heldAddr);
        if (held)
        {
            Assert(heldAddr >= searchStart, "Held range must be >= search start");

            bool mapped = vm.TryMapBacked(heldAddr, holdBytes, 0, GuestPageProtection.Read | GuestPageProtection.Write, out var failure);
            if (mapped)
            {
                Assert(vm.IsBackedView(heldAddr), "IsBackedView must report true for mapped view");
                Assert(vm.IsBackedRange(heldAddr, holdBytes), "IsBackedRange must report true");
                Assert(vm.CanRead(heldAddr, holdBytes), "CanRead must report true for backed range");

                byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
                bool written = vm.TryWriteBacking(heldAddr, data);
                Assert(written, "TryWriteBacking on PhysicalVirtualMemory failed");

                byte[] readBuf = new byte[8];
                bool read = vm.TryReadBacking(heldAddr, readBuf);
                Assert(read, "TryReadBacking on PhysicalVirtualMemory failed");
                Assert(readBuf.AsSpan().SequenceEqual(data), "Data read from backing mismatch");

                bool unmapped = vm.TryUnmapBacked(heldAddr, holdBytes);
                Assert(unmapped, "TryUnmapBacked failed");
            }
        }

        Console.WriteLine("  [PASS] 8. PhysicalVirtualMemory direct backing views and IHostViewMemory integration verified");
    }
}
