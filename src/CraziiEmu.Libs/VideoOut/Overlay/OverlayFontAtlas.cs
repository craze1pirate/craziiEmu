// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;

namespace CraziiEmu.Libs.VideoOut.Overlay;

public static class OverlayFontAtlas
{
    public const int GlyphWidth = 8;
    public const int GlyphHeight = 16;
    public const int AtlasWidth = 128;
    public const int AtlasHeight = 128;

    // A simple 128x128 8-bit alpha mask for 256 ASCII characters (16 cols x 16 rows)
    // For now, we will generate a dummy block atlas. A real font bitmap should be loaded here.
    private static readonly byte[] _atlasData = GenerateDummyAtlas();

    public static ReadOnlySpan<byte> Data => _atlasData;

    private static byte[] GenerateDummyAtlas()
    {
        var data = new byte[AtlasWidth * AtlasHeight];
        // Generate a simple bordered block for every printable ASCII character
        for (int c = 32; c < 127; c++)
        {
            int col = c % 16;
            int row = c / 16;
            int startX = col * GlyphWidth;
            int startY = row * GlyphHeight;

            for (int y = 0; y < GlyphHeight; y++)
            {
                for (int x = 0; x < GlyphWidth; x++)
                {
                    // Draw a border
                    if (x == 0 || x == GlyphWidth - 1 || y == 0 || y == GlyphHeight - 1)
                    {
                        data[(startY + y) * AtlasWidth + (startX + x)] = 255;
                    }
                }
            }
        }
        return data;
    }
}
