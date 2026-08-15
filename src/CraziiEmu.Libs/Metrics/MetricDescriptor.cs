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
    private int _validSamples;
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

    public void PushHistory(float sample)
    {
        if (HistoryBuffer is not null)
        {
            HistoryBuffer[_historyIndex] = sample;
            _historyIndex = (_historyIndex + 1) % HistoryBuffer.Length;
            if (_validSamples < HistoryBuffer.Length)
            {
                _validSamples++;
            }
        }
    }

    public void Update(double value)
    {
        _lastRefreshTimestamp = Stopwatch.GetTimestamp();
        CurrentValue = value;
    }

    public void GetHistory(Span<float> destination)
    {
        if (HistoryBuffer is null || _validSamples == 0)
        {
            destination.Clear();
            return;
        }

        var length = Math.Min(destination.Length, HistoryBuffer.Length);
        if (_validSamples < HistoryBuffer.Length)
        {
            for (int i = 0; i < length; i++)
            {
                int srcIdx = (i * _validSamples) / length;
                destination[i] = HistoryBuffer[srcIdx];
            }
            return;
        }

        // Full ring buffer: copy older samples first, then newer samples
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
