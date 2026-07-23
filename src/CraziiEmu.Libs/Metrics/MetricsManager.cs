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

    private static readonly Process _currentProcess = Process.GetCurrentProcess();
    private static TimeSpan _lastCpuTime = _currentProcess.TotalProcessorTime;
    private static long _lastCpuCheckTimestamp = Stopwatch.GetTimestamp();

    static MetricsManager()
    {
        var fpsFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatFloat1(MetricsManager.Fps.CurrentValue, dest, out written));
            
        var timeFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatFloat1(MetricsManager.Frametime.CurrentValue, dest, out written, " ms"));

        var countFormatter = new MetricFormatter((Span<char> dest, out int written) =>
            TryFormatInt((long)MetricsManager.DrawCalls.CurrentValue, dest, out written)); // Shared

        Fps = Register(new MetricDescriptor("FPS", MetricCategory.User, fpsFormatter, TimeSpan.FromMilliseconds(500), 300));
        Frametime = Register(new MetricDescriptor("Frametime", MetricCategory.User, timeFormatter, TimeSpan.FromMilliseconds(16), 300));
        
        DrawCalls = Register(new MetricDescriptor("Draw Calls", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(500)));
        DrawTimeMs = Register(new MetricDescriptor("Draw Time", MetricCategory.Emulator, timeFormatter, TimeSpan.FromMilliseconds(500)));
        PipelineCreations = Register(new MetricDescriptor("Pipelines", MetricCategory.Developer, countFormatter, TimeSpan.FromMilliseconds(500)));
        SpirvCompilations = Register(new MetricDescriptor("SPIR-V Compiles", MetricCategory.Developer, countFormatter, TimeSpan.FromMilliseconds(500)));

        ProcessCpuUsage = Register(new MetricDescriptor("Host CPU", MetricCategory.Host, new MetricFormatter((dest, out written) => TryFormatFloat1(MetricsManager.ProcessCpuUsage.CurrentValue, dest, out written, " %")), TimeSpan.FromMilliseconds(1000)));
        ProcessRamUsageMB = Register(new MetricDescriptor("Host RAM", MetricCategory.Host, new MetricFormatter((dest, out written) => TryFormatInt((long)MetricsManager.ProcessRamUsageMB.CurrentValue, dest, out written, " MB")), TimeSpan.FromMilliseconds(1000)));

        GuestWorkerThreads = Register(new MetricDescriptor("Guest Workers", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(1000)));
        GuestBlockedThreads = Register(new MetricDescriptor("Blocked Workers", MetricCategory.Emulator, countFormatter, TimeSpan.FromMilliseconds(1000)));
    }

    private static MetricDescriptor Register(MetricDescriptor metric)
    {
        _metrics.Add(metric);
        return metric;
    }

    public static void SampleHostMetrics()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = now - _lastCpuCheckTimestamp;
        
        if (elapsed > Stopwatch.Frequency)
        {
            _currentProcess.Refresh();
            var cpuTime = _currentProcess.TotalProcessorTime;
            var cpuDelta = cpuTime - _lastCpuTime;
            
            var seconds = (double)elapsed / Stopwatch.Frequency;
            var cpuPercent = (cpuDelta.TotalSeconds / seconds) * 100.0 / Environment.ProcessorCount;
            
            ProcessCpuUsage.Update(cpuPercent);
            ProcessRamUsageMB.Update(_currentProcess.WorkingSet64 / 1024.0 / 1024.0);
            
            _lastCpuTime = cpuTime;
            _lastCpuCheckTimestamp = now;
        }
    }

    // Zero-allocation formatting helpers
    private static bool TryFormatFloat1(double value, Span<char> dest, out int written, string suffix = "")
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

    private static bool TryFormatInt(long value, Span<char> dest, out int written, string suffix = "")
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
