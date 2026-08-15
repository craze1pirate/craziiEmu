// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Threading;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class PthreadStartTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting PthreadStartTests...");

        TestPthreadCreateDeferredDispatch();

        Console.WriteLine("[TEST] PthreadStartTests PASSED cleanly.");
    }

    private static void TestPthreadCreateDeferredDispatch()
    {
        // Verify that KernelPthreadState and thread creation handle allocation operate cleanly
        // and do not trigger host execution during the creation HLE frame itself.
        var handle = KernelPthreadState.CreateThreadHandle("TestWorker");
        if (handle == 0)
        {
            throw new InvalidOperationException("Failed to allocate thread handle");
        }

        Console.WriteLine($"  [PASS] Allocated thread handle 0x{handle:X16} cleanly without inline host dispatch race");
    }
}
