// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;

namespace CraziiEmu.Libs;

/// <summary>
/// High-resolution host sleeps for guest pacing. <see cref="Thread.Sleep(int)"/>
/// routinely overshoots by a scheduler quantum, which turns per-frame waits
/// (flip pacing, usleep-based game loops) into a hard frame-rate cap; these
/// helpers sleep coarsely for the bulk of the interval and yield-spin the
/// remainder so wakeups land within tens of microseconds of the target.
/// </summary>
internal static class HostTiming
{
    /// <summary>
    /// Blocks until <see cref="Stopwatch.GetTimestamp"/> reaches
    /// <paramref name="targetTimestamp"/>.
    /// </summary>
    public static void SleepUntil(long targetTimestamp)
    {
        while (true)
        {
            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return;
            }

            if (remainingTicks > Stopwatch.Frequency * 60)
            {
                // Far-future target: coarse sleep avoids overflowing the
                // microsecond conversion below and needs no precision.
                Thread.Sleep(30_000);
                continue;
            }

            var remainingMicroseconds = remainingTicks * 1_000_000 / Stopwatch.Frequency;
            if (remainingMicroseconds > 3000)
            {
                // Sleep coarsely for the bulk with a safety margin to prevent Windows scheduler overshoots.
                Thread.Sleep((int)((remainingMicroseconds - 1500) / 1000));
            }
            else if (remainingMicroseconds > 1000)
            {
                // Yield quantum to ready threads without entering kernel timer sleep
                Thread.Sleep(0);
            }
            else if (remainingMicroseconds > 50)
            {
                Thread.Yield();
            }
            else
            {
                Thread.SpinWait(32);
            }
        }
    }

    /// <summary>Blocks for the given number of microseconds.</summary>
    public static void SleepMicroseconds(long microseconds)
    {
        if (microseconds <= 0)
        {
            return;
        }

        if (microseconds >= 10_000_000)
        {
            // Long/sentinel sleeps (usleep(-1) parks): sub-millisecond
            // precision is irrelevant and the tick conversion below would
            // overflow, which used to turn the park into a hot spin.
            Thread.Sleep((int)Math.Min(microseconds / 1000, int.MaxValue));
            return;
        }

        var ticks = microseconds * Stopwatch.Frequency / 1_000_000;
        SleepUntil(Stopwatch.GetTimestamp() + ticks);
    }
}
