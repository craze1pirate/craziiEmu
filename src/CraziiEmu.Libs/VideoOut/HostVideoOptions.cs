// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

namespace CraziiEmu.Libs.VideoOut;

public enum HostWindowMode
{
    Windowed,
    Borderless,
    ExclusiveFullscreen,
}

public enum HostHdrMode
{
    Auto,
    On,
    Off,
}

public sealed record HostVideoOptions
{
    public static HostVideoOptions Default { get; } = new();

    public HostWindowMode WindowMode { get; init; } = HostWindowMode.Windowed;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public int DisplayIndex { get; init; }

    public int RefreshRate { get; init; }

    public bool VSync { get; init; } = true;

    public HostHdrMode HdrMode { get; init; } = HostHdrMode.Auto;

    public float ResolutionScale { get; init; } = 1.0f;

    public HostVideoOptions Normalize() => this with
    {
        Width = Math.Clamp(Width, 640, 16384),
        Height = Math.Clamp(Height, 360, 16384),
        DisplayIndex = Math.Max(0, DisplayIndex),
        RefreshRate = Math.Clamp(RefreshRate, 0, 1000),
        ResolutionScale = ResolutionScale > 0f ? Math.Clamp(ResolutionScale, 0.25f, 4.0f) : 1.0f,
        HdrMode = Enum.IsDefined(HdrMode) ? HdrMode : HostHdrMode.Auto,
    };
}

public static class HostVideoHost
{
    public static bool TryConfigureVideo(HostVideoOptions options) =>
        VulkanVideoPresenter.TryConfigureVideo(options.Normalize());
}
