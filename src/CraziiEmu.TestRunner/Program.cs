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

            SyncOnAddressTests.RunAllTests();
            HRTimerTests.RunAllTests();
            PngDecTests.RunAllTests();
            Videodec2Tests.RunAllTests();
            UltTests.RunAllTests();
            FontTests.RunAllTests();
            MemoryPoolTests.RunAllTests();
            PsmlShareTests.RunAllTests();
            LoginDialogTests.RunAllTests();
            NetSocketOptionTests.RunAllTests();
            NpWebApi2Tests.RunAllTests();
            PthreadStartTests.RunAllTests();
            SaveDataMountTests.RunAllTests();
            AioCompletionTests.RunAllTests();
            VideoOutFlipTests.RunAllTests();
            GpuRenderTargetReuseTests.RunAllTests();
            PthreadTlsTests.RunAllTests();
            KernelSocketErrnoTests.RunAllTests();
            UnifiedSocketTests.RunAllTests();
            UnityScriptingMemTests.RunAllTests();
            DeadCellsAndFloat16Tests.RunAllTests();
            ShaderCfgTests.RunAllTests();
            TextureCacheOverlapTests.RunAllTests();
            AsyncComputeTimelineTests.RunAllTests();
            GraphicsDynamicStateTests.RunAllTests();
            VirtualMemoryPreReservationTests.RunAllTests();
            UnityEngineCompatTests.RunAllTests();
            GameCompatibilityTests.RunAllTests();
            VulkanPipelineComplianceTests.RunAllTests();
            KernelSemaphoreLifecycleTests.RunAllTests();
            PadAndRudpTests.RunAllTests();
        }
    }
}
