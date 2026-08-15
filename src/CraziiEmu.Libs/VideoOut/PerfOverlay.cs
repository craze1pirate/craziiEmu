// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Diagnostics;
using CraziiEmu.Libs.Metrics;
using CraziiEmu.Libs.VideoOut.Overlay;

namespace CraziiEmu.Libs.VideoOut;

/// <summary>
/// Modern telemetry and performance overlay for CraziiEmu.
/// Toggled with F3 or F1; cycles through Minimal, Standard, Detailed, and Off.
/// </summary>
public static class PerfOverlay
{
    private static long _guestBufferCacheBytes;

    public static bool Enabled => OverlayRenderer.Mode != OverlayMode.Off;

    public static void Toggle() => OverlayRenderer.CycleMode();

    /// <summary>Called by the presenter after each successful host present.</summary>
    public static void RecordPresent()
    {
        MetricsManager.RecordPresent();
    }

    /// <summary>Called on every guest flip submission.</summary>
    public static void RecordSubmit()
    {
        MetricsManager.RecordSubmit();
    }

    /// <summary>Called per translated draw/dispatch executed.</summary>
    public static void RecordDraw()
    {
        MetricsManager.RecordDraw();
    }

    public static void SetGuestBufferCacheBytes(ulong bytes) =>
        Interlocked.Exchange(ref _guestBufferCacheBytes, checked((long)bytes));

    public static void Fill(Span<byte> destination, int pendingWork = 0, int pendingSubmissions = 0)
    {
        if (OverlayRenderer.Mode == OverlayMode.Off)
        {
            destination.Clear();
            return;
        }

        MetricsManager.RefreshStatsIfDue(pendingWork, pendingSubmissions);
        var uintSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(destination);
        OverlayRenderer.RenderToBuffer(uintSpan, 376, 176);
    }
}
