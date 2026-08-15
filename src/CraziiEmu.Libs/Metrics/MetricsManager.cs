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

    private static long _drawsInWindow;
    private static long _totalSpirvCompilations;
    private static long _lastFrameTimestamp;
    private static long _totalFrames;

    public static void RecordFrame()
    {
        Interlocked.Increment(ref _totalFrames);
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Exchange(ref _lastFrameTimestamp, now);
        if (last != 0)
        {
            var deltaMs = (double)(now - last) * 1000.0 / Stopwatch.Frequency;
            if (deltaMs > 0.05 && deltaMs < 2000.0)
            {
                Frametime.Update(deltaMs);
                var fps = 1000.0 / deltaMs;
                Fps.Update(fps);
            }
        }
    }

    public static void RecordDraw() => Interlocked.Increment(ref _drawsInWindow);
    public static long FlushDrawCount() => Interlocked.Exchange(ref _drawsInWindow, 0);

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

