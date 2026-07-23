// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Diagnostics;

namespace CraziiEmu.Libs.Metrics;

public enum MetricCategory
{
    User,
    Host,
    Emulator,
    Developer
}

public delegate bool MetricFormatter(Span<char> destination, out int charsWritten);

public class MetricDescriptor
{
    public string Name { get; }
    public MetricCategory Category { get; }
    public float[]? HistoryBuffer { get; }
    public TimeSpan RefreshInterval { get; }

    public double CurrentValue { get; set; }
    public MetricFormatter Formatter { get; }

    private int _historyIndex;
    private long _lastRefreshTimestamp;

    public MetricDescriptor(
        string name,
        MetricCategory category,
        MetricFormatter formatter,
        TimeSpan refreshInterval,
        int historySize = 0)
    {
        Name = name;
        Category = category;
        Formatter = formatter;
        RefreshInterval = refreshInterval;

        if (historySize > 0)
        {
            HistoryBuffer = new float[historySize];
        }
    }

    public void Update(double value)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = now - _lastRefreshTimestamp;
        
        // Refresh interval throttling
        if (elapsedTicks < RefreshInterval.Ticks)
        {
            return;
        }

        _lastRefreshTimestamp = now;
        CurrentValue = value;

        if (HistoryBuffer is not null)
        {
            HistoryBuffer[_historyIndex] = (float)value;
            _historyIndex = (_historyIndex + 1) % HistoryBuffer.Length;
        }
    }

    public void GetHistory(Span<float> destination)
    {
        if (HistoryBuffer is null)
        {
            return;
        }

        var length = Math.Min(destination.Length, HistoryBuffer.Length);
        
        // Copy older samples first, then newer samples
        var tailLength = HistoryBuffer.Length - _historyIndex;
        if (tailLength >= length)
        {
            new ReadOnlySpan<float>(HistoryBuffer, _historyIndex, length).CopyTo(destination);
        }
        else
        {
            new ReadOnlySpan<float>(HistoryBuffer, _historyIndex, tailLength).CopyTo(destination);
            new ReadOnlySpan<float>(HistoryBuffer, 0, length - tailLength).CopyTo(destination.Slice(tailLength));
        }
    }
}
