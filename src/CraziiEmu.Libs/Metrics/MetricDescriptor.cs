// Copyright (C) 2026 SharpEmu Emulator Project
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
        if (_validSamples < length)
        {
            int empty = length - _validSamples;
            destination.Slice(0, empty).Fill(HistoryBuffer[0]);
            new ReadOnlySpan<float>(HistoryBuffer, 0, _validSamples).CopyTo(destination.Slice(empty));
            return;
        }

        // Full ring buffer: copy the most recent 'length' samples in chronological order
        int startIdx = (_historyIndex - length + HistoryBuffer.Length) % HistoryBuffer.Length;
        int firstChunk = Math.Min(length, HistoryBuffer.Length - startIdx);
        new ReadOnlySpan<float>(HistoryBuffer, startIdx, firstChunk).CopyTo(destination);
        if (firstChunk < length)
        {
            new ReadOnlySpan<float>(HistoryBuffer, 0, length - firstChunk).CopyTo(destination.Slice(firstChunk));
        }
    }
}
