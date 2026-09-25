// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Silk.NET.Windowing;

namespace CraziiEmu.Libs.VideoOut;

public sealed record HostDisplayMode(int Width, int Height, int RefreshRate);

public sealed record HostDisplayInfo(
    int Index,
    string Name,
    IReadOnlyList<HostDisplayMode> Modes);

public static class HostDisplayCatalog
{
    private static int _queryFailureLogged;

    public static IReadOnlyList<HostDisplayInfo> Query()
    {
        try
        {
            var monitors = Silk.NET.Windowing.Monitor.GetMonitors(null).ToArray();
            if (monitors.Length == 0)
            {
                return CreateFallback();
            }

            var result = new List<HostDisplayInfo>(monitors.Length);
            for (var index = 0; index < monitors.Length; index++)
            {
                var monitor = monitors[index];
                var name = monitor.Name;
                var modes = ReadModes(monitor);
                result.Add(new HostDisplayInfo(
                    index,
                    string.IsNullOrWhiteSpace(name) ? $"Display {index + 1}" : name,
                    modes));
            }

            return result;
        }
        catch (Exception exception)
        {
            LogQueryFailure(exception.Message);
            return CreateFallback();
        }
    }

    private static IReadOnlyList<HostDisplayMode> ReadModes(IMonitor monitor)
    {
        var modes = new HashSet<HostDisplayMode>();
        try
        {
            foreach (var mode in monitor.GetAllVideoModes())
            {
                if (mode.Resolution.HasValue && mode.Resolution.Value.X > 0 && mode.Resolution.Value.Y > 0)
                {
                    modes.Add(new HostDisplayMode(
                        mode.Resolution.Value.X,
                        mode.Resolution.Value.Y,
                        mode.RefreshRate ?? 60));
                }
            }
        }
        catch
        {
            // Some platforms may throw on enumeration; fallback to current mode
        }

        if (monitor.VideoMode.Resolution.HasValue &&
            monitor.VideoMode.Resolution.Value.X > 0 &&
            monitor.VideoMode.Resolution.Value.Y > 0)
        {
            modes.Add(new HostDisplayMode(
                monitor.VideoMode.Resolution.Value.X,
                monitor.VideoMode.Resolution.Value.Y,
                monitor.VideoMode.RefreshRate ?? 60));
        }

        if (modes.Count == 0)
        {
            return CreateFallbackModes();
        }

        return modes
            .OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.RefreshRate)
            .ToArray();
    }

    private static IReadOnlyList<HostDisplayInfo> CreateFallback() =>
        [new HostDisplayInfo(0, "Display 1", CreateFallbackModes())];

    private static IReadOnlyList<HostDisplayMode> CreateFallbackModes() =>
        [
            new HostDisplayMode(3840, 2160, 60),
            new HostDisplayMode(2560, 1440, 60),
            new HostDisplayMode(1920, 1080, 60),
            new HostDisplayMode(1280, 720, 60),
        ];

    private static void LogQueryFailure(string message)
    {
        if (Interlocked.Exchange(ref _queryFailureLogged, 1) == 0)
        {
            Console.Error.WriteLine($"[GUI][WARN] Display query failed: {message}");
        }
    }
}
