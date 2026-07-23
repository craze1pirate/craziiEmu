// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using CraziiEmu.Libs.Metrics;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public enum OverlayMode
{
    Off,
    Minimal,
    Standard,
    Detailed
}

public static class OverlayRenderer
{
    public static OverlayMode Mode { get; set; } = OverlayMode.Off;
    public static OverlayPosition Position { get; set; } = OverlayPosition.TopLeft;

    public static void Render(
        float viewportWidth, 
        float viewportHeight, 
        List<OverlayVertex> outVertices, 
        List<uint> outIndices)
    {
        if (Mode == OverlayMode.Off)
        {
            return;
        }

        MetricsManager.SampleHostMetrics();
        var metrics = MetricsManager.Metrics;
        
        // Filter metrics based on Mode
        var visibleMetrics = new List<MetricDescriptor>();
        foreach (var m in metrics)
        {
            if (Mode == OverlayMode.Minimal && m.Category != MetricCategory.User) continue;
            if (Mode == OverlayMode.Standard && m.Category == MetricCategory.Developer) continue;
            visibleMetrics.Add(m);
        }

        var (startX, startY, width, height) = LayoutEngine.ComputeLayout(visibleMetrics, Position, viewportWidth, viewportHeight);

        // Render Background Panel (semi-transparent black)
        uint bgColor = 0x80000000; // AABBGGRR in little endian (50% alpha black)
        EmitSolidRect(startX, startY, width, height, bgColor, outVertices, outIndices);

        float curX = startX + LayoutEngine.Padding;
        float curY = startY + LayoutEngine.Padding;

        MetricCategory? lastCategory = null;
        Span<char> textBuffer = stackalloc char[256];
        Span<float> historySpan = stackalloc float[300];

        foreach (var metric in visibleMetrics)
        {
            if (lastCategory != metric.Category)
            {
                // Draw Category Header
                string catName = metric.Category.ToString();
                TextRenderer.EmitTextQuads(catName.AsSpan(), curX, curY, 0xFF00FFFF, outVertices, outIndices); // Yellow text
                curY += LayoutEngine.LineHeight;
                lastCategory = metric.Category;
            }

            // Format Metric Name + Value
            int written = 0;
            metric.Name.AsSpan().CopyTo(textBuffer.Slice(written));
            written += metric.Name.Length;
            textBuffer[written++] = ':';
            textBuffer[written++] = ' ';
            
            if (metric.Formatter(textBuffer.Slice(written), out int valWritten))
            {
                written += valWritten;
            }

            uint textColor = 0xFFFFFFFF; // White text
            TextRenderer.EmitTextQuads(textBuffer.Slice(0, written), curX, curY, textColor, outVertices, outIndices);
            curY += LayoutEngine.LineHeight;

            if (Mode == OverlayMode.Detailed && metric.HistoryBuffer is null == false)
            {
                metric.GetHistory(historySpan);
                float min = float.MaxValue;
                float max = float.MinValue;
                for (int i = 0; i < historySpan.Length; i++)
                {
                    if (historySpan[i] < min) min = historySpan[i];
                    if (historySpan[i] > max) max = historySpan[i];
                }
                
                uint graphColor = 0xFF00FF00; // Green graph
                GraphRenderer.EmitGraph(historySpan, curX, curY, LayoutEngine.GraphWidth, LayoutEngine.GraphHeight, min, max, graphColor, outVertices, outIndices);
                curY += LayoutEngine.GraphHeight;
            }
        }
    }

    private static void EmitSolidRect(float x, float y, float w, float h, uint color, List<OverlayVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;
        
        // UV = 0,0 maps to solid white pixel in our font atlas
        vertices.Add(new OverlayVertex { X = x, Y = y, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x + w, Y = y, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x + w, Y = y + h, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x, Y = y + h, U = 0, V = 0, ColorRgba = color });

        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }
}
