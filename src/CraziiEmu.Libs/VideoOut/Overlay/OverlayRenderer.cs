// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
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

    // Palette (AABBGGRR Little-Endian)
    private const uint ColorPink      = 0xFFB18FF4; // #F48FB1
    private const uint ColorGreen     = 0xFF5EC522; // #22C55E
    private const uint ColorCyan      = 0xFFD8B400; // #00B4D8
    private const uint ColorGold      = 0xFF0B9EF5; // #F59E0B
    private const uint ColorOrange    = 0xFF007AFF; // #FF7A00
    private const uint ColorDimGrey   = 0xFF777777;
    private const uint BgTranslucent  = 0x48080808; // ~28% opacity dark tint
    private const uint BoxBorderColor = 0x60B18FF4;

    public static void CycleMode()
    {
        Mode = Mode switch
        {
            OverlayMode.Off      => OverlayMode.Minimal,
            OverlayMode.Minimal  => OverlayMode.Standard,
            OverlayMode.Standard => OverlayMode.Detailed,
            OverlayMode.Detailed => OverlayMode.Off,
            _                    => OverlayMode.Off,
        };
    }

    public static void RenderToBuffer(Span<uint> pixels, int panelW, int panelH)
    {
        if (Mode == OverlayMode.Off) return;

        // Clear background with translucent dark tint
        pixels.Fill(BgTranslucent);

        int curX = (int)LayoutEngine.Padding;
        int curY = (int)LayoutEngine.Padding;
        int innerW = panelW - (int)LayoutEngine.Padding * 2;
        int rightX = panelW - (int)LayoutEngine.Padding;
        int lineH = (int)LayoutEngine.LineHeight;

        Span<char> buf = stackalloc char[128];
        Span<float> ftHistory = stackalloc float[300];
        Span<float> fpsHistory = stackalloc float[300];

        MetricsManager.Frametime.GetHistory(ftHistory);
        MetricsManager.Fps.GetHistory(fpsHistory);

        double frametime = MetricsManager.Frametime.CurrentValue;
        double fps = MetricsManager.Fps.CurrentValue;

        if (Mode == OverlayMode.Minimal)
        {
            DrawString(pixels, panelW, panelH, curX, curY, "Frame time".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(frametime, buf, out var written, " ms");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, ftHistory, 0f, 33.3f, ColorPink);
            curY += 28 + 6;

            DrawString(pixels, panelW, panelH, curX, curY, "Frames".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(fps, buf, out written, " FPS");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, fpsHistory, 0f, 120f, ColorPink);
        }
        else if (Mode == OverlayMode.Standard)
        {
            DrawString(pixels, panelW, panelH, curX, curY, "Frame time".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(frametime, buf, out var written, " ms");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, ftHistory, 0f, 33.3f, ColorPink);
            curY += 28 + 6;

            DrawString(pixels, panelW, panelH, curX, curY, "Frames".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(fps, buf, out written, " FPS");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, fpsHistory, 0f, 120f, ColorPink);
            curY += 28 + 16;

            DrawString(pixels, panelW, panelH, curX, curY, "GPU".AsSpan(), ColorGreen);
            FormatGpuStandard(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "VRAM".AsSpan(), ColorGreen);
            FormatVramStandard(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "RAM".AsSpan(), ColorCyan);
            MetricsManager.TryFormatFloat1(MetricsManager.RamUsedGB, buf, out written, " GB");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "CPU".AsSpan(), ColorCyan);
            FormatCpuStandard(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
        }
        else if (Mode == OverlayMode.Detailed)
        {
            DrawString(pixels, panelW, panelH, curX, curY, "Frame time".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(frametime, buf, out var written, " ms");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, ftHistory, 0f, 33.3f, ColorPink);
            curY += 28 + 6;

            DrawString(pixels, panelW, panelH, curX, curY, "Frames".AsSpan(), ColorPink);
            MetricsManager.TryFormatFloat1(fps, buf, out written, " FPS");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawOutlineBox(pixels, panelW, panelH, curX, curY, innerW, 28, BoxBorderColor);
            DrawLineGraph(pixels, panelW, panelH, curX + 2, curY + 2, innerW - 4, 24, fpsHistory, 0f, 120f, ColorPink);
            curY += 28 + 14;

            DrawString(pixels, panelW, panelH, curX, curY, "GPU".AsSpan(), ColorGreen);
            FormatGpuDetailed(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "VRAM".AsSpan(), ColorGreen);
            FormatVramDetailed(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "GPU Power".AsSpan(), ColorGreen);
            MetricsManager.TryFormatFloat1(MetricsManager.GpuPowerW, buf, out written, " W");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "CPU".AsSpan(), ColorCyan);
            FormatCpuDetailed(buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "RAM".AsSpan(), ColorCyan);
            MetricsManager.TryFormatFloat1(MetricsManager.RamUsedGB, buf, out written, " GB");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "SSD R/W".AsSpan(), ColorCyan);
            MetricsManager.TryFormatFloat1(MetricsManager.SsdReadWriteMBs, buf, out written, " MB/s");
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH + 14;

            DrawString(pixels, panelW, panelH, curX, curY, "Draw Calls".AsSpan(), ColorGold);
            MetricsManager.TryFormatInt((long)MetricsManager.DrawCalls.CurrentValue, buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "Guest Workers".AsSpan(), ColorGold);
            MetricsManager.TryFormatInt((long)MetricsManager.GuestWorkerThreads.CurrentValue, buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "Blocked Workers".AsSpan(), ColorGold);
            MetricsManager.TryFormatInt((long)MetricsManager.GuestBlockedThreads.CurrentValue, buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH;

            DrawString(pixels, panelW, panelH, curX, curY, "SPIR-V Compiles".AsSpan(), ColorGold);
            MetricsManager.TryFormatInt((long)MetricsManager.SpirvCompilations.CurrentValue, buf, out written);
            DrawRightString(pixels, panelW, panelH, rightX, curY, buf.Slice(0, written), ColorOrange);
            curY += lineH + 14;

            string gpuName = string.IsNullOrEmpty(MetricsManager.GpuDeviceName) ? "Direct3D12 / Vulkan GPU" : MetricsManager.GpuDeviceName;
            DrawString(pixels, panelW, panelH, curX, curY, gpuName.AsSpan(), ColorDimGrey);
        }
    }

    private static void DrawRightString(Span<uint> buffer, int bufW, int bufH, int rightX, int y, ReadOnlySpan<char> text, uint color)
    {
        int textW = text.Length * OverlayFontAtlas.GlyphWidth;
        DrawString(buffer, bufW, bufH, rightX - textW, y, text, color);
    }

    private static void DrawString(Span<uint> buffer, int bufW, int bufH, int x, int y, ReadOnlySpan<char> text, uint color)
    {
        uint srcA = (color >> 24) & 0xFF;
        uint srcB = (color >> 16) & 0xFF;
        uint srcG = (color >> 8) & 0xFF;
        uint srcR = color & 0xFF;

        ReadOnlySpan<byte> atlas = OverlayFontAtlas.Data;
        int atlasW = OverlayFontAtlas.AtlasWidth;
        int gW = OverlayFontAtlas.GlyphWidth;
        int gH = OverlayFontAtlas.GlyphHeight;

        int curX = x;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 32 || c > 126) c = '?';

            int col = c % 16;
            int row = c / 16;
            int gStartX = col * gW;
            int gStartY = row * gH;

            for (int gy = 0; gy < gH; gy++)
            {
                int py = y + gy;
                if ((uint)py >= (uint)bufH) continue;

                int atlasRow = (gStartY + gy) * atlasW + gStartX;
                int bufRow = py * bufW;

                for (int gx = 0; gx < gW; gx++)
                {
                    int px = curX + gx;
                    if ((uint)px >= (uint)bufW) continue;

                    byte mask = atlas[atlasRow + gx];
                    if (mask == 0) continue;

                    uint curA = (srcA * mask) / 255;
                    if (curA == 0) continue;

                    int idx = bufRow + px;
                    if (curA == 255)
                    {
                        buffer[idx] = (255u << 24) | (srcB << 16) | (srcG << 8) | srcR;
                    }
                    else
                    {
                        uint dst = buffer[idx];
                        uint dstA = (dst >> 24) & 0xFF;
                        uint dstB = (dst >> 16) & 0xFF;
                        uint dstG = (dst >> 8) & 0xFF;
                        uint dstR = dst & 0xFF;

                        uint invA = 255 - curA;
                        uint outA = curA + (dstA * invA) / 255;
                        uint outR = (srcR * curA + dstR * invA) / 255;
                        uint outG = (srcG * curA + dstG * invA) / 255;
                        uint outB = (srcB * curA + dstB * invA) / 255;

                        buffer[idx] = (outA << 24) | (outB << 16) | (outG << 8) | outR;
                    }
                }
            }

            curX += gW;
        }
    }

    private static void DrawOutlineBox(Span<uint> buffer, int bufW, int bufH, int x, int y, int w, int h, uint color)
    {
        DrawHorizontalLine(buffer, bufW, bufH, x, y, w, color);
        DrawHorizontalLine(buffer, bufW, bufH, x, y + h - 1, w, color);
        DrawVerticalLine(buffer, bufW, bufH, x, y, h, color);
        DrawVerticalLine(buffer, bufW, bufH, x + w - 1, y, h, color);
    }

    private static void DrawLineGraph(Span<uint> buffer, int bufW, int bufH, int x, int y, int w, int h, ReadOnlySpan<float> history, float minVal, float maxVal, uint color)
    {
        if (history.Length == 0 || w <= 0 || h <= 0) return;

        if (maxVal <= minVal) maxVal = minVal + 1.0f;
        float range = maxVal - minVal;

        int stepStride = Math.Max(1, history.Length / w);
        int prevX = x;
        int prevY = y + h - 1 - (int)Math.Clamp((history[0] - minVal) / range * (h - 1), 0f, h - 1);

        int sampleCount = history.Length / stepStride;
        for (int i = 1; i < sampleCount; i++)
        {
            float val = history[i * stepStride];
            float norm = Math.Clamp((val - minVal) / range, 0f, 1f);
            int currX = Math.Min(x + w - 1, x + (i * w) / sampleCount);
            int currY = y + h - 1 - (int)(norm * (h - 1));

            DrawHorizontalLine(buffer, bufW, bufH, Math.Min(prevX, currX), prevY, Math.Abs(currX - prevX) + 1, color);
            DrawVerticalLine(buffer, bufW, bufH, currX, Math.Min(prevY, currY), Math.Abs(currY - prevY) + 1, color);

            prevX = currX;
            prevY = currY;
        }
    }

    private static void DrawHorizontalLine(Span<uint> buffer, int bufW, int bufH, int x, int y, int len, uint color)
    {
        if ((uint)y >= (uint)bufH || len <= 0) return;
        int startX = Math.Clamp(x, 0, bufW);
        int endX = Math.Clamp(x + len, 0, bufW);
        if (startX >= endX) return;

        int offset = y * bufW + startX;
        buffer.Slice(offset, endX - startX).Fill(color);
    }

    private static void DrawVerticalLine(Span<uint> buffer, int bufW, int bufH, int x, int y, int len, uint color)
    {
        if ((uint)x >= (uint)bufW || len <= 0) return;
        int startY = Math.Clamp(y, 0, bufH);
        int endY = Math.Clamp(y + len, 0, bufH);
        if (startY >= endY) return;

        for (int py = startY; py < endY; py++)
        {
            buffer[py * bufW + x] = color;
        }
    }

    private static void FormatGpuStandard(Span<char> dest, out int written)
    {
        int temp = (int)MetricsManager.GpuTempC;
        int load = (int)MetricsManager.GpuLoadPercent;
        string s = temp > 0 ? $"{temp} \u00B0C    {load} %" : $"{load} %";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }

    private static void FormatGpuDetailed(Span<char> dest, out int written)
    {
        int temp = (int)MetricsManager.GpuTempC;
        int clock = (int)MetricsManager.GpuClockMHz;
        int load = (int)MetricsManager.GpuLoadPercent;
        string s = (temp > 0 && clock > 0) ? $"{temp} \u00B0C   {clock} MHz   {load} %" : $"{load} %";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }

    private static void FormatVramStandard(Span<char> dest, out int written)
    {
        double used = MetricsManager.VramUsedGB;
        double total = MetricsManager.VramTotalGB;
        int pct = total > 0 ? (int)(used / total * 100.0) : 0;
        string s = $"{used:F1} GB    {pct} %";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }

    private static void FormatVramDetailed(Span<char> dest, out int written)
    {
        double used = MetricsManager.VramUsedGB;
        double total = MetricsManager.VramTotalGB;
        string s = total > 0 ? $"{used:F1} GB / {total:F1} GB" : $"{used:F1} GB";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }

    private static void FormatCpuStandard(Span<char> dest, out int written)
    {
        int cpuPct = (int)MetricsManager.CpuUsagePercent;
        float ghz = MetricsManager.CpuFreqMHz / 1000.0f;
        string s = $"{cpuPct} %      {ghz:F1} GHz";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }

    private static void FormatCpuDetailed(Span<char> dest, out int written)
    {
        int cpuPct = (int)MetricsManager.CpuUsagePercent;
        int mhz = (int)MetricsManager.CpuFreqMHz;
        string s = $"{cpuPct} %   {mhz} MHz";
        s.AsSpan().CopyTo(dest);
        written = s.Length;
    }
}
