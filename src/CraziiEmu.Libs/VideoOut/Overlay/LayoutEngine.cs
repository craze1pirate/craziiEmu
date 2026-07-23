// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using CraziiEmu.Libs.Metrics;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public static class LayoutEngine
{
    public const float Margin = 10f;
    public const float Padding = 10f;
    public const float LineHeight = 20f;
    public const float GraphHeight = 40f;
    public const float GraphWidth = 200f;

    public static (float X, float Y, float Width, float Height) ComputeLayout(
        IReadOnlyList<MetricDescriptor> metrics, 
        OverlayPosition position, 
        float viewportWidth, 
        float viewportHeight)
    {
        // Calculate total content height and width
        float totalHeight = Padding * 2;
        float maxWidth = 250f; // Fixed minimum width for aesthetics

        MetricCategory? lastCategory = null;

        foreach (var metric in metrics)
        {
            if (lastCategory != metric.Category)
            {
                // Add spacing for category header
                totalHeight += LineHeight;
                lastCategory = metric.Category;
            }

            totalHeight += LineHeight;
            if (metric.HistoryBuffer is null)
            {
                // Just text
            }
            else
            {
                // Text + Graph
                totalHeight += GraphHeight;
            }
        }

        // Anchor
        float startX = Margin;
        float startY = Margin;

        if (position == OverlayPosition.TopRight || position == OverlayPosition.BottomRight)
        {
            startX = viewportWidth - maxWidth - Margin;
        }

        if (position == OverlayPosition.BottomLeft || position == OverlayPosition.BottomRight)
        {
            startY = viewportHeight - totalHeight - Margin;
        }

        return (startX, startY, maxWidth, totalHeight);
    }
}
