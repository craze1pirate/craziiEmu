// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public static class GraphRenderer
{
    public static void EmitGraph(
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

        // Adjust max to prevent div by zero
        if (maxVal <= minVal)
        {
            maxVal = minVal + 1.0f;
        }
        
        float range = maxVal - minVal;
        float stepX = width / history.Length;

        // Draw as a solid bar chart for simplicity
        for (int i = 0; i < history.Length; i++)
        {
            float val = history[i];
            float normalized = Math.Clamp((val - minVal) / range, 0.0f, 1.0f);
            
            float barHeight = normalized * height;
            float px0 = startX + i * stepX;
            float px1 = px0 + stepX * 0.9f; // Small gap between bars
            float py0 = startY + height - barHeight;
            float py1 = startY + height;

            uint baseIndex = (uint)vertices.Count;

            // UV mapped to a solid white pixel on the font atlas (assuming white borders exist or we can pass a untextured quad pipeline).
            // For now, map UV to 0,0 where we assume there's a solid pixel or the shader handles untextured quads (e.g. UV = -1, -1).
            // A common trick is to map to the top-left pixel of a solid block. 
            // In our dummy atlas, border pixels are 255 (white). We can just use U=0, V=0.
            float u = 0.0f;
            float v = 0.0f;

            // Top-Left
            vertices.Add(new OverlayVertex { X = px0, Y = py0, U = u, V = v, ColorRgba = color });
            // Top-Right
            vertices.Add(new OverlayVertex { X = px1, Y = py0, U = u, V = v, ColorRgba = color });
            // Bottom-Right
            vertices.Add(new OverlayVertex { X = px1, Y = py1, U = u, V = v, ColorRgba = color });
            // Bottom-Left
            vertices.Add(new OverlayVertex { X = px0, Y = py1, U = u, V = v, ColorRgba = color });

            // Triangles
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }
    }
}
