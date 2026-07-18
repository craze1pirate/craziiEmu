// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using Silk.NET.Windowing;

namespace CraziiEmu.Libs.VideoOut;

internal static unsafe class OpenGLVideoPresenter
{
    private static IWindow? _window;
    private static bool _started;

    public static void EnsureStarted(uint width, uint height)
    {
        if (_started)
            return;

        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>((int)width, (int)height);
        options.Title = "CraziiEmu (OpenGL Hook)";
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));

        _window = Window.Create(options);
        _window.Initialize();

        _started = true;
        Console.WriteLine("[VideoOut] OpenGL Video Presenter Initialized.");
    }

    public static void Submit(ReadOnlySpan<byte> rgbaFrame, uint width, uint height)
    {
        EnsureStarted(width, height);
        // OpenGL buffer upload logic placeholder
    }
}
