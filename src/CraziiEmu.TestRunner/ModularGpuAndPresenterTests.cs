// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.Runtime;
using CraziiEmu.HLE;
using CraziiEmu.HLE.GpuMemory;
using CraziiEmu.Libs.AvPlayer;
using CraziiEmu.Libs.Gpu.Buffers;
using CraziiEmu.Libs.Gpu.GpuCommands.Registers;
using CraziiEmu.Libs.Gpu.Images;
using CraziiEmu.Libs.Gpu.Rendering;
using CraziiEmu.Libs.Gpu.Scheduling;
using CraziiEmu.Libs.VideoOut;
using Silk.NET.Vulkan;

namespace CraziiEmu.TestRunner;

public static class ModularGpuAndPresenterTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting ModularGpuAndPresenterTests (Phase 4)...");

        TestRenderExecutorStateAndHelpers();
        TestGuestBufferRegistryAndLifecycles();
        TestImageRequestBuildersAndDescriptors();
        TestVulkanPipelineCacheStorage();
        TestAvPlayerFallbackPresentation();
        TestVulkanVideoPresenterCompatibility();
        TestRuntimeGpuMemoryAttachmentAndLifecycle();

        Console.WriteLine("[TEST] ModularGpuAndPresenterTests PASSED cleanly.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine($"[FAIL] {message}");
            throw new Exception($"Test assertion failed: {message}");
        }
    }

    private static void TestRenderExecutorStateAndHelpers()
    {
        // 1. Thread to group calculations
        Assert(RenderExecutor.GroupsFromThreads(0, 64) == 0, "0 threads should yield 0 groups");
        Assert(RenderExecutor.GroupsFromThreads(64, 64) == 1, "64 threads with groupSize 64 should yield 1 group");
        Assert(RenderExecutor.GroupsFromThreads(65, 64) == 2, "65 threads with groupSize 64 should yield 2 groups");
        Assert(RenderExecutor.GroupsFromThreads(1000, 256) == 4, "1000 threads with groupSize 256 should yield 4 groups");

        // 2. Vertex and instance offset resolution
        var vertexInfo = new VertexInputInfo();
        Assert(RenderExecutor.ResolveVertexOffset(0, vertexInfo) == 0, "Default vertex input offset should be 0");
        Assert(RenderExecutor.ResolveInstanceOffset(vertexInfo) == 0, "Default instance offset should be 0");

        // 3. Dynamic scissor resolution
        var viewportRegs = new ScreenViewportRegisters();
        var scanModeRegs = default(ScanModeRegisters);
        var scissor = RenderExecutor.ResolveScissor(viewportRegs, in scanModeRegs, 1920, 1080);
        Assert(scissor.Left == 0 && scissor.Top == 0 && scissor.Right == 1920 && scissor.Bottom == 1080,
            "Unset screen scissor should fallback to framebuffer dimensions");

        Console.WriteLine("  [PASS] 1. RenderExecutor thread dispatch, offset calculation, and scissor resolution verified");
    }

    private sealed class DummyGpuBuffer : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static void TestGuestBufferRegistryAndLifecycles()
    {
        const ulong pageSize = 0x4000; // 16 KB
        const ulong addressSpace = 0x10000000000UL; // 1 TB
        using var registry = new GuestBufferRegistry<DummyGpuBuffer>(pageSize, addressSpace);

        // 1. Allocate buffer
        var dummy1 = new DummyGpuBuffer();
        var id1 = registry.AllocateBuffer(dummy1, address: 0x10000, size: 0x8000);
        Assert(registry.GetState(id1) == BufferLifetimeState.Allocated, "Newly allocated buffer must have Allocated state");
        Assert(registry.TryGetBuffer(id1) == dummy1, "Allocated buffer resource should match");

        // 2. Register buffer
        registry.RegisterBuffer(id1, tick: 100);
        Assert(registry.GetState(id1) == BufferLifetimeState.Registered, "Registered buffer must have Registered state");
        Assert(registry.RegisteredCount == 1, "RegisteredCount should be 1");
        Assert(registry.RegisteredBytes == 0x8000, "RegisteredBytes should equal 0x8000");

        // 3. Query containing and overlapping buffers
        var foundId = registry.FindContainingBuffer(0x12000, 0x1000);
        Assert(foundId == id1, "FindContainingBuffer should locate registered buffer");

        var missingId = registry.FindContainingBuffer(0x20000, 0x1000);
        Assert(missingId == default, "FindContainingBuffer should return default for unregistered range");

        Assert(registry.HasOverlap(0x14000, 0x2000), "HasOverlap should report true for overlapping range");
        Assert(!registry.HasOverlap(0x20000, 0x2000), "HasOverlap should report false for non-overlapping range");

        // 4. Overlapping registration rejection
        var dummy2 = new DummyGpuBuffer();
        var id2 = registry.AllocateBuffer(dummy2, address: 0x14000, size: 0x8000);
        bool rejected = false;
        var oldFatal = SubmissionScheduler.OnFatal;
        try
        {
            SubmissionScheduler.OnFatal = msg => throw new InvalidOperationException(msg);
            registry.RegisterBuffer(id2, tick: 101);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        finally
        {
            SubmissionScheduler.OnFatal = oldFatal;
        }
        Assert(rejected, "Overlapping buffer registration must throw an exception");

        // 5. Retirement lifecycle
        registry.BeginRetirement(id1);
        Assert(registry.GetState(id1) == BufferLifetimeState.Retiring, "State should transition to Retiring");
        Assert(registry.RegisteredBytes == 0, "RegisteredBytes should decrease on retirement");

        var completed = registry.CompleteRetirement(id1);
        Assert(completed, "CompleteRetirement should return true");
        Assert(registry.GetState(id1) == BufferLifetimeState.Released, "State should transition to Released");
        Assert(dummy1.Disposed, "Underlying buffer resource must be disposed");

        Console.WriteLine("  [PASS] 2. GuestBufferRegistry slot allocation, registration, overlap rejection, and retirement verified");
    }

    private static void TestImageRequestBuildersAndDescriptors()
    {
        // 1. SampleCount mapping
        Assert(ImageRequestBuilders.SampleCount(0) == 1, "SampleCount(0) should be 1");
        Assert(ImageRequestBuilders.SampleCount(1) == 2, "SampleCount(1) should be 2");
        Assert(ImageRequestBuilders.SampleCount(2) == 4, "SampleCount(2) should be 4");
        Assert(ImageRequestBuilders.SampleCount(3) == 8, "SampleCount(3) should be 8");
        Assert(ImageRequestBuilders.SampleCount(4) == 0, "SampleCount(4) out of range should be 0");

        // 2. TargetViewRange resolution
        Assert(TargetViewRange.TryResolve(0, 3, 0, out var range), "Valid TargetViewRange [0, 3] should succeed");
        Assert(range.BaseLayer == 0 && range.LayerCount == 4 && range.ImageLayers == 4, "TargetViewRange should match extent");

        Assert(!TargetViewRange.TryResolve(4, 2, 0, out _), "Inverted TargetViewRange [4, 2] should fail");
        Assert(!TargetViewRange.TryResolve(0, 3, 1, out _), "TargetViewRange with non-zero draw offset should fail");

        // 3. ImageRequest Refresh construction
        var desc = ImageDescription.Create();
        desc.PixelFormat = Format.R8G8B8A8Unorm;
        desc.Extent = new Extent3D(1920, 1080, 1);

        var request = ImageRequest.Refresh(desc, ImageRole.ColorTarget);
        Assert(request.Description.Extent.Width == 1920, "Request description width should match");
        Assert(request.Description.Extent.Height == 1080, "Request description height should match");
        Assert(request.Role == ImageRole.ColorTarget, "Request role should be ColorTarget");

        Console.WriteLine("  [PASS] 3. ImageRequestBuilders sample count, view ranges, and ImageRequest descriptors verified");
    }

    private static void TestVulkanPipelineCacheStorage()
    {
        // 1. Default TitleID path resolution
        var defaultPath = VulkanPipelineCacheStorage.ResolvePath("PPSA01234", null);
        Assert(defaultPath.EndsWith(Path.Combine("user", "pipeline_cache", "PPSA01234", "vulkan-pipeline-cache.bin")),
            $"Default path resolution mismatch: {defaultPath}");

        var unknownPath = VulkanPipelineCacheStorage.ResolvePath(null, null);
        Assert(unknownPath.EndsWith(Path.Combine("user", "pipeline_cache", "UNKNOWN", "vulkan-pipeline-cache.bin")),
            $"Null TitleID path resolution mismatch: {unknownPath}");

        var sanitizedPath = VulkanPipelineCacheStorage.ResolvePath("PPSA/..:01234", null);
        Assert(!sanitizedPath.Contains(".."), "Sanitized path must not contain directory traversal");

        // 2. Legacy path & cache import
        var legacyPath = Path.Combine(Path.GetTempPath(), $"craziiemu_legacy_test_{Guid.NewGuid():N}.bin");
        var destPath = Path.Combine(Path.GetTempPath(), "craziiemu_dest_dir", $"imported_cache_{Guid.NewGuid():N}.bin");

        try
        {
            File.WriteAllBytes(legacyPath, [0xCA, 0xFE, 0xBA, 0xBE]);

            bool imported = VulkanPipelineCacheStorage.ImportLegacyCache(legacyPath, destPath);
            Assert(imported, "ImportLegacyCache should succeed for existing legacy file");
            Assert(File.Exists(destPath), "Destination pipeline cache file must exist");
            Assert(File.ReadAllBytes(destPath).Length == 4, "Destination cache file must match content size");

            // Repeated import should report false
            bool repeated = VulkanPipelineCacheStorage.ImportLegacyCache(legacyPath, destPath);
            Assert(!repeated, "ImportLegacyCache should return false when destination already exists");
        }
        finally
        {
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
            if (File.Exists(destPath)) File.Delete(destPath);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }

        Console.WriteLine("  [PASS] 4. VulkanPipelineCacheStorage per-title pathing, sanitization, and legacy import verified");
    }

    private static void TestAvPlayerFallbackPresentation()
    {
        // Without active players registered, fallback frame retrieval should cleanly report false
        bool hasFrame = AvPlayerExports.TryGetFallbackPresentationFrame(
            out var pixels,
            out var width,
            out var height,
            out var serial);

        Assert(!hasFrame, "TryGetFallbackPresentationFrame should return false when no players exist");
        Assert(pixels.Length == 0, "Pixels should be empty");
        Assert(width == 0 && height == 0, "Dimensions should be 0");
        Assert(serial == 0, "Serial should be 0");

        Console.WriteLine("  [PASS] 5. AvPlayer fallback presentation query and idle safety verified");
    }

    private static void TestVulkanVideoPresenterCompatibility()
    {
        // 1. Storage image format conversions
        Assert(VulkanVideoPresenter.GetStorageImageFormat(Format.R8G8B8A8Srgb) == Format.R8G8B8A8Unorm,
            "R8G8B8A8Srgb storage format should convert to R8G8B8A8Unorm");
        Assert(VulkanVideoPresenter.GetStorageImageFormat(Format.R32Sint) == Format.R32Uint,
            "R32Sint storage format should convert to R32Uint");
        Assert(VulkanVideoPresenter.GetStorageImageFormat(Format.BC7SrgbBlock) == Format.BC7UnormBlock,
            "BC7SrgbBlock storage format should convert to BC7UnormBlock");

        // 2. Format compatibility classes
        Assert(VulkanVideoPresenter.IsCompatibleViewFormat(Format.R8G8B8A8Srgb, Format.B8G8R8A8Unorm),
            "R8G8B8A8Srgb and B8G8R8A8Unorm should belong to the same 32-bit compatibility class");
        Assert(!VulkanVideoPresenter.IsCompatibleViewFormat(Format.R8Unorm, Format.R32Uint),
            "R8Unorm and R32Uint must not be compatible");

        // 3. Polygon offset constant factor conversion
        float converted = VulkanVideoPresenter.ConvertPolygonOffsetConstantFactor(
            guestFactor: 1.0f,
            negNumDbBits: -24,
            dbIsFloatFmt: false,
            hostDepthFormat: Format.D24UnormS8Uint);
        Assert(converted == 1.0f, "Normalized D24 format should scale cleanly to 1.0");

        Console.WriteLine("  [PASS] 6. VulkanVideoPresenter storage format aliasing, view compatibility, and polygon offset verified");
    }

    private static void TestRuntimeGpuMemoryAttachmentAndLifecycle()
    {
        // 1. Verify CraziiEmuRuntime.CreateDefault attaches GuestGpuMemoryHook
        using (var runtime = CraziiEmuRuntime.CreateDefault())
        {
            var memory = GuestGpuMemoryHook.Current;
            Assert(memory != null, "GuestGpuMemoryHook.Current must be non-null after CraziiEmuRuntime creation");
            Assert(memory.AddressSpace is ICpuMemory, "AddressSpace must implement ICpuMemory");
            Assert(memory.AddressSpace is IGuestBackedSpace, "AddressSpace must implement IGuestBackedSpace for buffer/image cache");
        }

        // 2. Verify fallback provider works when PhysicalVirtualMemory is standalone
        using (var vm = new PhysicalVirtualMemory())
        {
            var fallbackMem = GuestGpuMemoryHook.Current;
            Assert(fallbackMem != null, "GuestGpuMemoryHook.Current should resolve via AddressSpaceFallbackProvider");
            Assert(fallbackMem.AddressSpace is IGuestBackedSpace, "Fallback address space must implement IGuestBackedSpace");

            // Allocate a page and verify NoteMapped coverage
            ulong testAddr = 0x0000_0003_0000_0000UL;
            ulong testSize = 0x10000;
            ulong allocated = vm.AllocateAt(testAddr, testSize, executable: false);
            Assert(allocated == testAddr, "AllocateAt must succeed");
            Assert(fallbackMem.Covers(testAddr, testSize), "Allocated memory should be covered by GuestGpuMemory spans");
        }

        // 3. Verify clean detach
        GuestGpuMemoryHook.Attach(null);
        Assert(GuestGpuMemoryHook.Current == null, "GuestGpuMemoryHook.Current should be null after explicit detach and VM disposal");

        Console.WriteLine("  [PASS] 7. Runtime GPU memory attachment, backing space resolution, and fallback provider verified");
    }
}
