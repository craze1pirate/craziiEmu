// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CraziiEmu.Libs.VideoOut;

namespace CraziiEmu.TestRunner;

public static class AsyncComputeTimelineTests
{
    private sealed class MockSubmission
    {
        public ulong Timeline { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool Retired { get; set; }
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running AsyncComputeTimelineTests...");

        TestMasterSemaphoreMonotonicTickProgression();
        TestMasterSemaphoreKnownGpuTickAdvancement();
        TestMasterSemaphoreCpuWaitAndTimeout();
        TestCrossQueueTimelineSyncSimulation();
        TestQueueSubmissionBatchRetirement();

        Console.WriteLine("[TEST] AsyncComputeTimelineTests PASSED successfully.");
    }

    private static void TestMasterSemaphoreMonotonicTickProgression()
    {
        using var semaphore = new VulkanMasterSemaphore(enableNativeTimeline: false);
        Assert(semaphore.CurrentTick == 1, "Initial CurrentTick must be 1.");
        Assert(semaphore.KnownGpuTick == 0, "Initial KnownGpuTick must be 0.");

        const int threadCount = 8;
        const int iterationsPerThread = 5000;
        var allocatedTicks = new ConcurrentBag<ulong>();

        Parallel.For(0, threadCount, _ =>
        {
            for (var i = 0; i < iterationsPerThread; i++)
            {
                allocatedTicks.Add(semaphore.NextTick());
            }
        });

        var tickSet = new HashSet<ulong>(allocatedTicks);
        Assert(tickSet.Count == threadCount * iterationsPerThread,
            $"Expected {threadCount * iterationsPerThread} unique ticks, got {tickSet.Count}.");

        for (ulong i = 1; i <= (ulong)(threadCount * iterationsPerThread); i++)
        {
            Assert(tickSet.Contains(i), $"Missing tick {i} from concurrent allocations.");
        }

        Assert(semaphore.CurrentTick == (ulong)(threadCount * iterationsPerThread + 1),
            "CurrentTick must equal total iterations + 1.");
    }

    private static void TestMasterSemaphoreKnownGpuTickAdvancement()
    {
        using var semaphore = new VulkanMasterSemaphore(enableNativeTimeline: false);

        Assert(semaphore.IsFree(0), "Tick 0 must always be free.");
        Assert(!semaphore.IsFree(1), "Tick 1 must not be free initially.");

        semaphore.AdvanceGpuTickDirect(50);
        Assert(semaphore.KnownGpuTick == 50, $"Expected KnownGpuTick 50, got {semaphore.KnownGpuTick}.");
        Assert(semaphore.IsFree(50), "Tick 50 must be free.");
        Assert(!semaphore.IsFree(51), "Tick 51 must not be free.");

        // Monotonic progression: attempting to set an earlier tick must be ignored
        semaphore.AdvanceGpuTickDirect(25);
        Assert(semaphore.KnownGpuTick == 50, $"KnownGpuTick regressed to {semaphore.KnownGpuTick}; must remain 50.");

        semaphore.AdvanceGpuTickDirect(100);
        Assert(semaphore.KnownGpuTick == 100, $"Expected KnownGpuTick 100, got {semaphore.KnownGpuTick}.");
        Assert(semaphore.IsFree(100), "Tick 100 must be free.");
        Assert(!semaphore.IsFree(101), "Tick 101 must not be free.");
    }

    private static void TestMasterSemaphoreCpuWaitAndTimeout()
    {
        using var semaphore = new VulkanMasterSemaphore(enableNativeTimeline: false);
        semaphore.AdvanceGpuTickDirect(10);

        // Fast path: already free tick must return true immediately
        var sw = Stopwatch.StartNew();
        var freeResult = semaphore.Wait(10, timeoutNs: 5_000_000_000UL); // 5 sec
        sw.Stop();
        Assert(freeResult, "Wait on already-free tick must succeed.");
        Assert(sw.ElapsedMilliseconds < 50, $"Fast path took {sw.ElapsedMilliseconds}ms; must be near-instant.");

        // Timed wait with background signal
        var targetTick = semaphore.NextTick(); // tick 1
        var backgroundCompleted = false;
        var waitTask = Task.Run(() =>
        {
            Thread.Sleep(30);
            backgroundCompleted = true;
            semaphore.SignalHost(targetTick);
        });

        var waited = semaphore.Wait(targetTick, timeoutNs: 2_000_000_000UL); // 2 sec
        waitTask.Wait();
        Assert(waited, "Wait must succeed once background thread signals host.");
        Assert(backgroundCompleted, "Background task should have run before wait resolved.");
        Assert(semaphore.IsFree(targetTick), "Target tick must be free.");

        // Timeout test: waiting for a future tick that is never signaled
        var futureTick = semaphore.NextTick() + 100;
        sw.Restart();
        var timeoutResult = semaphore.Wait(futureTick, timeoutNs: 20_000_000UL); // 20ms
        sw.Stop();
        Assert(!timeoutResult, "Wait on unsignaled tick must time out and return false.");
        Assert(sw.ElapsedMilliseconds >= 10, $"Timeout elapsed too quickly: {sw.ElapsedMilliseconds}ms.");
    }

    private static void TestCrossQueueTimelineSyncSimulation()
    {
        using var semaphore = new VulkanMasterSemaphore(enableNativeTimeline: false);
        const int cycles = 50;

        ulong lastComputeTick = 0;
        ulong lastGraphicsTick = 0;

        for (var i = 0; i < cycles; i++)
        {
            // 1. Async Compute queue dispatches work
            var computeTick = semaphore.NextTick();
            // Compute optionally waits on previous graphics work
            if (lastGraphicsTick > 0)
            {
                Assert(semaphore.IsFree(lastGraphicsTick),
                    $"Async compute must see graphics tick {lastGraphicsTick} completed.");
            }

            // Simulate compute execution and signal
            semaphore.SignalHost(computeTick);
            lastComputeTick = computeTick;

            // 2. Graphics queue renders, depending on compute output
            Assert(semaphore.IsFree(lastComputeTick),
                $"Graphics must see compute tick {lastComputeTick} completed.");

            var graphicsTick = semaphore.NextTick();
            // Simulate graphics execution and signal
            semaphore.SignalHost(graphicsTick);
            lastGraphicsTick = graphicsTick;
        }

        Assert(semaphore.KnownGpuTick == lastGraphicsTick,
            $"Final GPU tick must match last graphics tick {lastGraphicsTick}.");
    }

    private static void TestQueueSubmissionBatchRetirement()
    {
        using var semaphore = new VulkanMasterSemaphore(enableNativeTimeline: false);
        var pending = new Queue<MockSubmission>();

        for (var i = 1; i <= 10; i++)
        {
            var tick = semaphore.NextTick();
            pending.Enqueue(new MockSubmission { Timeline = tick, Name = $"Sub_{i}" });
        }

        Assert(pending.Count == 10, "Should have 10 pending submissions.");

        // Advance GPU tick to 4 and retire
        semaphore.AdvanceGpuTickDirect(4);
        var completedTick = semaphore.KnownGpuTick;

        while (pending.TryPeek(out var sub))
        {
            if (sub.Timeline > completedTick)
            {
                break;
            }

            pending.Dequeue();
            sub.Retired = true;
        }

        Assert(pending.Count == 6, $"Expected 6 pending submissions after tick 4, got {pending.Count}.");
        Assert(pending.Peek().Timeline == 5, $"Oldest pending submission must be tick 5, got {pending.Peek().Timeline}.");

        // Advance GPU tick to 10 and retire all
        semaphore.AdvanceGpuTickDirect(10);
        completedTick = semaphore.KnownGpuTick;

        while (pending.TryPeek(out var sub))
        {
            if (sub.Timeline > completedTick)
            {
                break;
            }

            pending.Dequeue();
            sub.Retired = true;
        }

        Assert(pending.Count == 0, $"All submissions should be retired, but {pending.Count} remain.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
