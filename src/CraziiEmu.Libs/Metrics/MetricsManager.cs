// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CraziiEmu.Libs.Metrics;

public static class MetricsManager
{
    private static readonly List<MetricDescriptor> _metrics = new();
    
    public static IReadOnlyList<MetricDescriptor> Metrics => _metrics;

    public static MetricDescriptor Fps { get; }
    public static MetricDescriptor Frametime { get; }
    public static MetricDescriptor DrawCalls { get; }
    public static MetricDescriptor DrawTimeMs { get; }
    public static MetricDescriptor PipelineCreations { get; }
    public static MetricDescriptor SpirvCompilations { get; }
    public static MetricDescriptor ProcessCpuUsage { get; }
    public static MetricDescriptor ProcessRamUsageMB { get; }
    public static MetricDescriptor GuestWorkerThreads { get; }
    public static MetricDescriptor GuestBlockedThreads { get; }

    // Live Telemetry Values (Updated by SystemTelemetrySampler async thread)
    public static double CpuUsagePercent { get; set; } = 0;
    public static double ProcessCpuPercent { get; set; } = 0;
    public static float CpuFreqMHz { get; set; } = 3625;
    public static double RamUsedGB { get; set; } = 0;
    public static double RamTotalGB { get; set; } = 0;
    public static double SsdReadWriteMBs { get; set; } = 0;
    public static double AllocatedMBs { get; set; } = 0;
    public static int Gen0Count { get; set; } = 0;
    public static int Gen1Count { get; set; } = 0;
    public static int Gen2Count { get; set; } = 0;
    public static double EmuRamMB { get; set; } = 0;

    public static float GpuTempC { get; set; } = 0;
    public static float GpuClockMHz { get; set; } = 0;
    public static float GpuLoadPercent { get; set; } = 0;
    public static float GpuPowerW { get; set; } = 0;
    public static double VramUsedGB { get; set; } = 0;
    public static double VramTotalGB { get; set; } = 0;
    public static string GpuDeviceName { get; set; } = "";

    private static long _lastSubmitTimestamp;
    private static long _lastPresentTimestamp;
    private static long _sessionStartTimestamp;
    private static long _submittedInWindow;
    private static long _presentedInWindow;
    private static long _drawsInWindow;
    private static long _totalSpirvCompilations;
    private static long _totalFrames;
    private static long _statsWindowStart = Stopwatch.GetTimestamp();
    private static long _lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    private static int _lastGen0 = GC.CollectionCount(0);
    private static int _lastGen1 = GC.CollectionCount(1);
    private static int _lastGen2 = GC.CollectionCount(2);
    private static double _lastFrameIntervalMs = 16.6;

    /// <summary>Called on every guest flip submission.</summary>
    public static void RecordSubmit()
    {
        Interlocked.Increment(ref _totalFrames);
        var now = Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _sessionStartTimestamp, now, 0);
        Interlocked.Increment(ref _submittedInWindow);
        var last = Interlocked.Exchange(ref _lastSubmitTimestamp, now);
        if (last != 0)
        {
            var deltaMs = (double)(now - last) * 1000.0 / Stopwatch.Frequency;
            if (deltaMs > 0.05 && deltaMs < 2000.0)
            {
                _lastFrameIntervalMs = deltaMs;
                Frametime.PushHistory((float)deltaMs);
                Fps.PushHistory((float)(1000.0 / deltaMs));
            }
        }
    }

    /// <summary>Called by the presenter after each successful host present.</summary>
    public static void RecordPresent()
    {
        Interlocked.CompareExchange(ref _sessionStartTimestamp, Stopwatch.GetTimestamp(), 0);
        Interlocked.Increment(ref _presentedInWindow);
        _lastPresentTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Called per translated draw/dispatch executed.</summary>
    public static void RecordDraw() => Interlocked.Increment(ref _drawsInWindow);
    public static long FlushDrawCount() => Interlocked.Exchange(ref _drawsInWindow, 0);

    public static void RefreshStatsIfDue(int pendingWork = 0, int inFlightSubmissions = 0)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = now - _statsWindowStart;
        if (elapsedTicks >= Stopwatch.Frequency / 2) // 500ms update cadence
        {
            var seconds = (double)elapsedTicks / Stopwatch.Frequency;
            _statsWindowStart = now;

            var submitted = Interlocked.Exchange(ref _submittedInWindow, 0);
            var presented = Interlocked.Exchange(ref _presentedInWindow, 0);
            var draws = Interlocked.Exchange(ref _drawsInWindow, 0);

            var fps = submitted / seconds;
            var presentFps = presented / seconds;
            var drawsPerSec = draws / seconds;

            var allocated = GC.GetTotalAllocatedBytes(precise: false);
            AllocatedMBs = Math.Max(0, (allocated - _lastAllocatedBytes) / seconds / (1024.0 * 1024.0));
            _lastAllocatedBytes = allocated;

            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            Gen0Count = gen0 - _lastGen0;
            Gen1Count = gen1 - _lastGen1;
            Gen2Count = gen2 - _lastGen2;
            _lastGen0 = gen0;
            _lastGen1 = gen1;
            _lastGen2 = gen2;

            EmuRamMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            var lastSubmit = Interlocked.Read(ref _lastSubmitTimestamp);
            double avgFrameMs;
            if (fps > 0)
            {
                avgFrameMs = _lastFrameIntervalMs > 0 ? _lastFrameIntervalMs : 1000.0 / fps;
                Fps.Update(fps);
                Frametime.Update(avgFrameMs);
            }
            else if (lastSubmit != 0)
            {
                avgFrameMs = (now - lastSubmit) * 1000.0 / Stopwatch.Frequency;
                Fps.Update(0);
                Frametime.Update(avgFrameMs);
            }
            else if (presentFps > 0)
            {
                avgFrameMs = 1000.0 / presentFps;
                Fps.Update(presentFps);
                Frametime.Update(avgFrameMs);
            }
            else
            {
                Fps.Update(0);
                Frametime.Update(0);
            }

            DrawCalls.Update(drawsPerSec);
        }
    }

    public static void RecordSpirvCompilation()
    {
        long count = Interlocked.Increment(ref _totalSpirvCompilations);
        SpirvCompilations.Update(count);
    }

    static MetricsManager()
    {
        var fpsFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatFloat1(MetricsManager.Fps.CurrentValue, dest, out written));
            
        var timeFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatFloat1(MetricsManager.Frametime.CurrentValue, dest, out written, " ms"));

        var countFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatInt((long)MetricsManager.DrawCalls.CurrentValue, dest, out written));

        Fps = Register(new MetricDescriptor("FPS", MetricCategory.User, fpsFormatter, TimeSpan.FromMilliseconds(16), 300));
        Frametime = Register(new MetricDescriptor("Frametime", MetricCategory.User, timeFormatter, TimeSpan.FromMilliseconds(16), 300));
        
        DrawCalls = Register(new MetricDescriptor("Draw Calls", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(500)));
        DrawTimeMs = Register(new MetricDescriptor("Draw Time", MetricCategory.Emulator, timeFormatter, TimeSpan.FromMilliseconds(500)));
        PipelineCreations = Register(new MetricDescriptor("Pipelines", MetricCategory.Developer, countFormatter, TimeSpan.FromMilliseconds(500)));
        SpirvCompilations = Register(new MetricDescriptor("SPIR-V Compiles", MetricCategory.Developer, countFormatter, TimeSpan.FromMilliseconds(500)));

        ProcessCpuUsage = Register(new MetricDescriptor("Host CPU", MetricCategory.Host, new MetricFormatter((dest, out written) => TryFormatFloat1(MetricsManager.ProcessCpuUsage.CurrentValue, dest, out written, " %")), TimeSpan.FromMilliseconds(1000)));
        ProcessRamUsageMB = Register(new MetricDescriptor("Host RAM", MetricCategory.Host, new MetricFormatter((dest, out written) => TryFormatInt((long)MetricsManager.ProcessRamUsageMB.CurrentValue, dest, out written, " MB")), TimeSpan.FromMilliseconds(1000)));

        GuestWorkerThreads = Register(new MetricDescriptor("Guest Workers", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(1000)));
        GuestBlockedThreads = Register(new MetricDescriptor("Blocked Workers", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(1000)));

        // Start background telemetry sampling
        SystemTelemetrySampler.Start();
    }

    private static MetricDescriptor Register(MetricDescriptor metric)
    {
        _metrics.Add(metric);
        return metric;
    }

    public static void SampleHostMetrics()
    {
        // No-op on render thread! Metrics are sampled asynchronously by SystemTelemetrySampler.
    }

    // Zero-allocation formatting helpers
    public static bool TryFormatFloat1(double value, Span<char> dest, out int written, string suffix = "")
    {
        written = 0;
        if (!value.TryFormat(dest, out var valWritten, "F1")) return false;
        written += valWritten;
        
        if (suffix.Length > 0)
        {
            if (dest.Length < written + suffix.Length) return false;
            suffix.AsSpan().CopyTo(dest.Slice(written));
            written += suffix.Length;
        }
        return true;
    }

    public static bool TryFormatInt(long value, Span<char> dest, out int written, string suffix = "")
    {
        written = 0;
        if (!value.TryFormat(dest, out var valWritten)) return false;
        written += valWritten;

        if (suffix.Length > 0)
        {
            if (dest.Length < written + suffix.Length) return false;
            suffix.AsSpan().CopyTo(dest.Slice(written));
            written += suffix.Length;
        }
        return true;
    }
}

