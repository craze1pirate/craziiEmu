// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public static class GraphRenderer
{
    public static void EmitOutlineBox(
        float x, float y, float w, float h,
        uint color,
        List<OverlayVertex> vertices,
        List<uint> indices)
    {
        float border = 1.0f;
        // Top
        EmitSolidRect(x, y, w, border, color, vertices, indices);
        // Bottom
        EmitSolidRect(x, y + h - border, w, border, color, vertices, indices);
        // Left
        EmitSolidRect(x, y, border, h, color, vertices, indices);
        // Right
        EmitSolidRect(x + w - border, y, border, h, color, vertices, indices);
    }

    public static void EmitLineGraph(
        ReadOnlySpan<float> history,
        float startX,
        float startY,
        float width,
        float height,
        float minVal,
        float maxVal,
        uint color,
        List<OverlayVertex> vertices,
        List<uint> indices)
    {
        if (history.Length == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        if (maxVal <= minVal)
        {
            maxVal = minVal + 1.0f;
        }

        float range = maxVal - minVal;
        int stride = Math.Max(1, history.Length / 75);
        float stepX = width / (history.Length / (float)stride);
        float prevX = startX;
        float prevY = startY + height - Math.Clamp((history[0] - minVal) / range, 0f, 1f) * height;

        int stepIdx = 1;
        for (int i = stride; i < history.Length; i += stride, stepIdx++)
        {
            float val = history[i];
            float normalized = Math.Clamp((val - minVal) / range, 0.0f, 1.0f);
            float currX = startX + stepIdx * stepX;
            float currY = startY + height - (normalized * height);

            EmitLineSegment(prevX, prevY, currX, prevY, 1.5f, color, vertices, indices);
            EmitLineSegment(currX, prevY, currX, currY, 1.5f, color, vertices, indices);

            prevX = currX;
            prevY = currY;
        }
    }

    private static void EmitLineSegment(
        float x0, float y0, float x1, float y1,
        float thickness,
        uint color,
        List<OverlayVertex> vertices,
        List<uint> indices)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0001f) return;

        float nx = -dy / length * (thickness * 0.5f);
        float ny = dx / length * (thickness * 0.5f);

        uint baseIndex = (uint)vertices.Count;
        vertices.Add(new OverlayVertex { X = x0 + nx, Y = y0 + ny, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x1 + nx, Y = y1 + ny, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x1 - nx, Y = y1 - ny, U = 0, V = 0, ColorRgba = color });
        vertices.Add(new OverlayVertex { X = x0 - nx, Y = y0 - ny, U = 0, V = 0, ColorRgba = color });

        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }

    private static void EmitSolidRect(float x, float y, float w, float h, uint color, List<OverlayVertex> vertices, List<uint> indices)
    {
        uint baseIndex = (uint)vertices.Count;
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

