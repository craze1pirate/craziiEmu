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
    public const float Padding = 12f;
    public const float LineHeight = 22f;

    public static (float X, float Y, float Width, float Height) ComputeLayout(
        OverlayMode mode,
        OverlayPosition position,
        float viewportWidth,
        float viewportHeight)
    {
        float width = 320f;
        float height = Padding * 2;

        switch (mode)
        {
            case OverlayMode.Minimal:
                // Frame time line + box graph + Frames line + box graph
                height += (LineHeight + 28f + 6f) * 2;
                break;

            case OverlayMode.Standard:
                // Minimal (Frametime + Frames + 2 graphs) + GPU + VRAM + RAM + CPU
                height += (LineHeight + 28f + 6f) * 2;
                height += LineHeight * 4 + 16f;
                break;

            case OverlayMode.Detailed:
                // Frame time + Box Graph (28px) + Frames + Box Graph (28px)
                height += LineHeight * 2 + (28f + 6f) * 2 + 14f;
                // GPU + VRAM + GPU Power + CPU + RAM + SSD R/W
                height += LineHeight * 6 + 14f;
                // Draw Calls + Alloc + GC + EMU RAM + Guest Workers + Blocked Workers + SPIR-V Compiles
                height += LineHeight * 7;
                break;

            default:
                width = 0;
                height = 0;
                break;
        }

        float startX = Margin;
        float startY = Margin;

        if (position == OverlayPosition.TopRight || position == OverlayPosition.BottomRight)
        {
            startX = viewportWidth - width - Margin;
        }

        if (position == OverlayPosition.BottomLeft || position == OverlayPosition.BottomRight)
        {
            startY = viewportHeight - height - Margin;
        }

        return (startX, startY, width, height);
    }
}


