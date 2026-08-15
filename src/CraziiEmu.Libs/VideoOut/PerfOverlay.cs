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
    private static long _lastPresentTimestamp;
    private static long _lastSubmitTimestamp;
    private static long _sessionStartTimestamp;
    private const int FrameHistorySize = 128;
    private static readonly double[] _frameMilliseconds = new double[FrameHistorySize];
    private static int _frameHistoryIndex;
    private static long _presentedInWindow;
    private static long _submittedInWindow;
    private static long _drawsInWindow;
    private static long _guestBufferCacheBytes;

    public static bool Enabled => OverlayRenderer.Mode != OverlayMode.Off;

    public static void Toggle() => OverlayRenderer.CycleMode();

    /// <summary>Called by the presenter after each successful host present.</summary>
    public static void RecordPresent()
    {
        Interlocked.CompareExchange(ref _sessionStartTimestamp, Stopwatch.GetTimestamp(), 0);
        Interlocked.Increment(ref _presentedInWindow);
        _lastPresentTimestamp = Stopwatch.GetTimestamp();
        MetricsManager.RecordFrame();
    }

    /// <summary>Called on every guest flip submission.</summary>
    public static void RecordSubmit()
    {
        var now = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _sessionStartTimestamp, now, 0);
        Interlocked.Increment(ref _submittedInWindow);
        var last = Interlocked.Exchange(ref _lastSubmitTimestamp, now);
        if (last != 0)
        {
            var milliseconds = (now - last) * 1000.0 / Stopwatch.Frequency;
            var index = _frameHistoryIndex;
            _frameMilliseconds[index] = milliseconds;
            _frameHistoryIndex = (index + 1) % FrameHistorySize;
        }

        MetricsManager.RecordFrame();
    }

    /// <summary>Called per translated draw/dispatch executed.</summary>
    public static void RecordDraw()
    {
        Interlocked.Increment(ref _drawsInWindow);
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

        var uintSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(destination);
        OverlayRenderer.RenderToBuffer(uintSpan, 376, 176);
    }
}
