// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;

namespace CraziiEmu.TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
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
        }
    }
}
