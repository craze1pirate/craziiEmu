// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;

namespace CraziiEmu.TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0] == "--deadcells" || args[0] == "DeadCellsAndFloat16Tests")
                {
                    DeadCellsAndFloat16Tests.RunAllTests();
                    return;
                }

            }

            // Active regression test suites for the synchronized codebase
            PngDecTests.RunAllTests();
            UltTests.RunAllTests();
            LoginDialogTests.RunAllTests();
            SaveDataMountTests.RunAllTests();
            AioCompletionTests.RunAllTests();
            VideoOutFlipTests.RunAllTests();
            PthreadTlsTests.RunAllTests();
            DeadCellsAndFloat16Tests.RunAllTests();
            ShaderResourceEngineTests.RunAllTests();
            TextureCacheOverlapTests.RunAllTests();
            AsyncComputeTimelineTests.RunAllTests();
            VulkanPipelineComplianceTests.RunAllTests();
            GpuMemoryAndHostViewsTests.RunAllTests();
            ModularGpuAndPresenterTests.RunAllTests();
            UnifiedSocketTests.RunAllTests();
            NetSocketOptionTests.RunAllTests();
            KernelSocketErrnoTests.RunAllTests();
            NetEpollTests.RunAllTests();
        }
    }
}
