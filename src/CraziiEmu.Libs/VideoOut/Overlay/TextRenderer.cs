// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public struct OverlayVertex
{
    public float X;
    public float Y;
    public float U;
    public float V;
    public uint ColorRgba;
}

public static class TextRenderer
{
    public static void EmitTextQuads(
        ReadOnlySpan<char> text, 
        float startX, 
        float startY, 
        uint color, 
        List<OverlayVertex> vertices, 
        List<uint> indices)
    {
        float curX = startX;
        float curY = startY;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            
            if (c == '\n')
            {
                curX = startX;
                curY += OverlayFontAtlas.GlyphHeight;
                continue;
            }

            // Map char to ASCII bounds
            int atlasIndex = c;
            if (atlasIndex < 32 || atlasIndex > 126)
            {
                atlasIndex = '?';
            }

            int col = atlasIndex % 16;
            int row = atlasIndex / 16;

            float u0 = (col * OverlayFontAtlas.GlyphWidth) / (float)OverlayFontAtlas.AtlasWidth;
            float v0 = (row * OverlayFontAtlas.GlyphHeight) / (float)OverlayFontAtlas.AtlasHeight;
            float u1 = ((col + 1) * OverlayFontAtlas.GlyphWidth) / (float)OverlayFontAtlas.AtlasWidth;
            float v1 = ((row + 1) * OverlayFontAtlas.GlyphHeight) / (float)OverlayFontAtlas.AtlasHeight;

            uint baseIndex = (uint)vertices.Count;

            // Top-Left
            vertices.Add(new OverlayVertex { X = curX, Y = curY, U = u0, V = v0, ColorRgba = color });
            // Top-Right
            vertices.Add(new OverlayVertex { X = curX + OverlayFontAtlas.GlyphWidth, Y = curY, U = u1, V = v0, ColorRgba = color });
            // Bottom-Right
            vertices.Add(new OverlayVertex { X = curX + OverlayFontAtlas.GlyphWidth, Y = curY + OverlayFontAtlas.GlyphHeight, U = u1, V = v1, ColorRgba = color });
            // Bottom-Left
            vertices.Add(new OverlayVertex { X = curX, Y = curY + OverlayFontAtlas.GlyphHeight, U = u0, V = v1, ColorRgba = color });

            // Triangle 1
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            // Triangle 2
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);

            curX += OverlayFontAtlas.GlyphWidth;
        }
    }
}
