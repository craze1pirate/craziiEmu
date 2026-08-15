// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using CraziiEmu.Libs.VideoOut;
using CraziiEmu.Libs.Gpu;

namespace CraziiEmu.TestRunner
{
    public static class GpuRenderTargetReuseTests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("[TEST] Running GpuRenderTargetReuseTests...");

            TestGpuRenderTargetReuse();
            TestCpuBackedFallbackForNonGpuAddress();
            TestAddressRangeOverlapLookup();
            TestRepeatedRenderCompositeCycles();

            Console.WriteLine("[TEST] GpuRenderTargetReuseTests PASSED successfully.");
        }

        private static void TestGpuRenderTargetReuse()
        {
            const ulong renderTargetAddress = 0x14000000;
            const ulong displayBufferAddress = 0x13BB0000;
            const uint width = 1920;
            const uint height = 1080;
            const uint format = 10; // R8G8B8A8
            const uint numberType = 0;

            // Submit blit from 0x14000000 to 0x13BB0000
            bool success = GuestGpu.Current.TrySubmitGuestImageBlit(
                renderTargetAddress,
                width,
                height,
                format,
                numberType,
                displayBufferAddress,
                width,
                height,
                format,
                numberType);

            if (!success)
            {
                throw new Exception($"TrySubmitGuestImageBlit failed for RT 0x{renderTargetAddress:X16}");
            }

            Console.WriteLine("  [PASS] TestGpuRenderTargetReuse");
        }

        private static void TestCpuBackedFallbackForNonGpuAddress()
        {
            const ulong cpuAddress = 0x20000000;
            const ulong displayBufferAddress = 0x13BB0000;
            const uint width = 64;
            const uint height = 64;
            const uint format = 10;
            const uint numberType = 0;

            // Non-GPU address should still accept blit request gracefully
            bool success = GuestGpu.Current.TrySubmitGuestImageBlit(
                cpuAddress,
                width,
                height,
                format,
                numberType,
                displayBufferAddress,
                width,
                height,
                format,
                numberType);

            if (!success)
            {
                throw new Exception($"TrySubmitGuestImageBlit failed for CPU address 0x{cpuAddress:X16}");
            }

            Console.WriteLine("  [PASS] TestCpuBackedFallbackForNonGpuAddress");
        }

        private static void TestAddressRangeOverlapLookup()
        {
            const ulong offsetAddress = 0x14000100;
            const ulong displayBufferAddress = 0x13BB0000;
            const uint width = 512;
            const uint height = 512;
            const uint format = 10;
            const uint numberType = 0;

            bool success = GuestGpu.Current.TrySubmitGuestImageBlit(
                offsetAddress,
                width,
                height,
                format,
                numberType,
                displayBufferAddress,
                width,
                height,
                format,
                numberType);

            if (!success)
            {
                throw new Exception($"TrySubmitGuestImageBlit failed for range offset 0x{offsetAddress:X16}");
            }

            Console.WriteLine("  [PASS] TestAddressRangeOverlapLookup");
        }

        private static void TestRepeatedRenderCompositeCycles()
        {
            const ulong renderTargetAddress = 0x14000000;
            const ulong displayBufferAddress = 0x13BB0000;
            const uint width = 1920;
            const uint height = 1080;
            const uint format = 10;
            const uint numberType = 0;

            for (int cycle = 0; cycle < 10; cycle++)
            {
                bool success = GuestGpu.Current.TrySubmitGuestImageBlit(
                    renderTargetAddress,
                    width,
                    height,
                    format,
                    numberType,
                    displayBufferAddress,
                    width,
                    height,
                    format,
                    numberType);

                if (!success)
                {
                    throw new Exception($"TrySubmitGuestImageBlit failed on cycle {cycle}");
                }
            }

            Console.WriteLine("  [PASS] TestRepeatedRenderCompositeCycles");
        }
    }
}
