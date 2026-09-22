// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Generic;
using CraziiEmu.Libs.Gpu;
using CraziiEmu.Libs.VideoOut;

namespace CraziiEmu.TestRunner;

public static class TextureCacheOverlapTests
{
    private sealed class MockTrackedImage
    {
        public ulong Address { get; init; }
        public ulong Size { get; init; }
        public string Name { get; init; } = string.Empty;

        public (ulong Address, ulong Size) Range => (Address, Size);
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running TextureCacheOverlapTests...");

        TestPageTrackerRegistrationAndLookup();
        TestMultiPageCrossBoundaryRegistration();
        TestSubAllocationEnclosingLookup();
        TestUnmapRangeOverlaps();
        Test48BitHighAddressFallback();
        TestGuestDepthExtentResolutionWithOverlap();

        Console.WriteLine("[TEST] TextureCacheOverlapTests PASSED successfully.");
    }

    private static void TestPageTrackerRegistrationAndLookup()
    {
        var tracker = new GuestTexturePageTracker<MockTrackedImage>();
        var image1 = new MockTrackedImage { Address = 0x1000_0000, Size = 0x10_0000, Name = "Image1" }; // 1 MiB
        var image2 = new MockTrackedImage { Address = 0x1020_0000, Size = 0x10_0000, Name = "Image2" }; // 1 MiB

        tracker.Register(image1.Address, image1.Size, image1);
        tracker.Register(image2.Address, image2.Size, image2);

        // Exact query on image1
        var results = tracker.FindOverlapping(0x1000_0000, 0x10_0000, img => img.Range);
        Assert(results.Count == 1 && results[0] == image1, "Expected only Image1 in range");

        // Non-overlapping query between image1 and image2
        var emptyResults = tracker.FindOverlapping(0x1010_0000, 0x08_0000, img => img.Range);
        Assert(emptyResults.Count == 0, "Expected 0 results for unmapped region");

        // Unregister image1
        bool unregistered = tracker.Unregister(image1.Address, image1.Size, image1);
        Assert(unregistered, "Expected successful unregistration of Image1");

        results = tracker.FindOverlapping(0x1000_0000, 0x10_0000, img => img.Range);
        Assert(results.Count == 0, "Expected 0 results after unregistration");

        Console.WriteLine("  [PASS] TestPageTrackerRegistrationAndLookup");
    }

    private static void TestMultiPageCrossBoundaryRegistration()
    {
        var tracker = new GuestTexturePageTracker<MockTrackedImage>();
        // 8 MiB image spanning from 0x2000_0000 to 0x2080_0000 (8 consecutive 1 MiB pages)
        var largeImage = new MockTrackedImage { Address = 0x2000_0000, Size = 0x80_0000, Name = "Large8MB" };
        tracker.Register(largeImage.Address, largeImage.Size, largeImage);

        // Query in page 3
        var midResults = tracker.FindOverlapping(0x2030_0000, 0x01_0000, img => img.Range);
        Assert(midResults.Count == 1 && midResults[0] == largeImage, "Expected Large8MB when querying page 3");

        // Query spanning the entire 8 MiB range (must return Large8MB exactly ONCE despite 8 page entries)
        var fullResults = tracker.FindOverlapping(0x2000_0000, 0x80_0000, img => img.Range);
        Assert(fullResults.Count == 1 && fullResults[0] == largeImage, "Query epoch must deduplicate across pages");

        Console.WriteLine("  [PASS] TestMultiPageCrossBoundaryRegistration");
    }

    private static void TestSubAllocationEnclosingLookup()
    {
        var tracker = new GuestTexturePageTracker<MockTrackedImage>();
        // 4 MiB buffer representing a texture atlas or GPU render target
        var atlas = new MockTrackedImage { Address = 0x3000_0000, Size = 0x40_0000, Name = "Atlas4MB" };
        tracker.Register(atlas.Address, atlas.Size, atlas);

        // A sub-allocated texture located inside the atlas at offset +0x10_0000 with size 256 KiB
        ulong subTextureAddr = 0x3010_0000;
        ulong subTextureSize = 0x04_0000;

        var enclosing = tracker.FindEnclosing(subTextureAddr, subTextureSize, img => img.Range);
        Assert(enclosing == atlas, "FindEnclosing should return the containing atlas");

        Assert(GuestTexturePageTracker<object>.ImageRangeEncloses(atlas.Address, atlas.Size, subTextureAddr, subTextureSize),
            "ImageRangeEncloses should be true for sub-allocation");

        // A texture that starts inside but overflows the atlas
        ulong overflowingAddr = 0x303E_0000;
        ulong overflowingSize = 0x04_0000;
        var notEnclosed = tracker.FindEnclosing(overflowingAddr, overflowingSize, img => img.Range);
        Assert(notEnclosed == null, "FindEnclosing should return null when resource overflows boundary");

        Console.WriteLine("  [PASS] TestSubAllocationEnclosingLookup");
    }

    private static void TestUnmapRangeOverlaps()
    {
        var tracker = new GuestTexturePageTracker<MockTrackedImage>();
        var imageA = new MockTrackedImage { Address = 0x4000_0000, Size = 0x20_0000, Name = "ImageA" }; // [0x4000_0000, 0x4020_0000)
        var imageB = new MockTrackedImage { Address = 0x4030_0000, Size = 0x10_0000, Name = "ImageB" }; // [0x4030_0000, 0x4040_0000)
        var imageC = new MockTrackedImage { Address = 0x4050_0000, Size = 0x10_0000, Name = "ImageC" }; // [0x4050_0000, 0x4060_0000)

        tracker.Register(imageA.Address, imageA.Size, imageA);
        tracker.Register(imageB.Address, imageB.Size, imageB);
        tracker.Register(imageC.Address, imageC.Size, imageC);

        // Unmap [0x4015_0000, 0x4035_0000)
        // Partial overlap with tail of A, and partial overlap with head of B
        var evictedList = new List<MockTrackedImage>();
        var evicted = tracker.UnmapRange(0x4015_0000, 0x20_0000, img => img.Range, item => evictedList.Add(item));

        Assert(evicted.Count == 2, $"Expected 2 evicted images, got {evicted.Count}");
        Assert(evicted.Contains(imageA), "ImageA must be evicted due to tail overlap");
        Assert(evicted.Contains(imageB), "ImageB must be evicted due to head overlap");
        Assert(!evicted.Contains(imageC), "ImageC must remain untouched");

        // Verify remaining
        var remainingC = tracker.FindOverlapping(imageC.Address, imageC.Size, img => img.Range);
        Assert(remainingC.Count == 1 && remainingC[0] == imageC, "ImageC should still be present in tracker");

        Console.WriteLine("  [PASS] TestUnmapRangeOverlaps");
    }

    private static void Test48BitHighAddressFallback()
    {
        var tracker = new GuestTexturePageTracker<MockTrackedImage>();
        // 48-bit address space (> 1 TiB, outside the 40-bit first level array)
        ulong highAddr = 0x8000_1000_0000UL;
        ulong size = 0x20_0000UL; // 2 MiB

        var highImage = new MockTrackedImage { Address = highAddr, Size = size, Name = "High48Bit" };
        tracker.Register(highImage.Address, highImage.Size, highImage);

        var results = tracker.FindOverlapping(highAddr + 0x1000, 0x1000, img => img.Range);
        Assert(results.Count == 1 && results[0] == highImage, "High 48-bit address lookup failed");

        bool unreg = tracker.Unregister(highImage.Address, highImage.Size, highImage);
        Assert(unreg, "High 48-bit address unregister failed");

        results = tracker.FindOverlapping(highAddr, size, img => img.Range);
        Assert(results.Count == 0, "High 48-bit address should be gone after unregister");

        Console.WriteLine("  [PASS] Test48BitHighAddressFallback");
    }

    private static void TestGuestDepthExtentResolutionWithOverlap()
    {
        // Test depth target of 1920x1080
        var depthTarget = new GuestDepthTarget(
            ReadAddress: 0x5000_0000,
            WriteAddress: 0x5000_0000,
            Width: 1920,
            Height: 1080,
            GuestFormat: 2, // Z32F (4 bytes/pixel)
            SwizzleMode: 0,
            ClearDepth: 1.0f,
            ReadOnly: false);

        // Case 1: Exact resolution when color target matches depth extent
        var resExact = GuestDepthExtentResolver.Resolve(depthTarget, 1920, 1080, []);
        Assert(resExact.Kind == GuestDepthExtentResolutionKind.Exact && resExact.Width == 1920 && resExact.Height == 1080,
            "Expected exact resolution when color fits depth");

        // Case 2: Depth buffer initialized smaller (e.g. 1280x720) but an aliasing texture overlaps at base address
        var smallDepthTarget = new GuestDepthTarget(
            ReadAddress: 0x6000_0000,
            WriteAddress: 0x6000_0000,
            Width: 1280,
            Height: 720,
            GuestFormat: 2,
            SwizzleMode: 0,
            ClearDepth: 1.0f,
            ReadOnly: false);

        var aliasingTexture = new GuestDrawTexture(
            Address: 0x6000_0000,
            Width: 1920,
            Height: 1080,
            Format: 10,
            NumberType: 0,
            RgbaPixels: [],
            IsFallback: false,
            IsStorage: false);

        var resAlias = GuestDepthExtentResolver.Resolve(smallDepthTarget, 1920, 1080, [aliasingTexture]);
        Assert(resAlias.Kind == GuestDepthExtentResolutionKind.TextureAlias && resAlias.Width == 1920 && resAlias.Height == 1080,
            "Expected TextureAlias resolution for aliasing texture");

        // Case 3: Overlapping texture (e.g. offset into shared allocation)
        var offsetTexture = new GuestDrawTexture(
            Address: 0x6000_0100, // slightly offset
            Width: 1920,
            Height: 1080,
            Format: 10,
            NumberType: 0,
            RgbaPixels: [],
            IsFallback: false,
            IsStorage: false);

        var resOverlap = GuestDepthExtentResolver.Resolve(smallDepthTarget, 1920, 1080, [offsetTexture]);
        Assert(resOverlap.Kind == GuestDepthExtentResolutionKind.TextureAlias && resOverlap.Width == 1920 && resOverlap.Height == 1080,
            "Expected TextureAlias resolution for overlapping texture");

        Console.WriteLine("  [PASS] TestGuestDepthExtentResolutionWithOverlap");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERTION FAILED] {message}");
        }
    }
}
