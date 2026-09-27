// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using System.Linq;
using FFmpeg.AutoGen;

namespace CraziiEmu.Libs.Media;

internal static class FfmpegRuntime
{
    private static readonly object _gate = new();
    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            var pluginsDir = FindPluginsDirectory();
            ffmpeg.RootPath = pluginsDir;

            try
            {
                DynamicallyLoadedBindings.Initialize();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AVPLAYER][WARN] Failed to initialize FFmpeg dynamic bindings: {ex.Message}");
            }
        }
    }

    private static string FindPluginsDirectory()
    {
        var appBase = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appBase))
        {
            var appPlugins = Path.Combine(appBase, "plugins");
            if (IsValidPluginDir(appPlugins))
            {
                return appPlugins;
            }

            if (IsValidPluginDir(appBase))
            {
                return appBase;
            }

            var current = new DirectoryInfo(appBase);
            for (var i = 0; i < 6 && current != null; i++)
            {
                var candidate = Path.Combine(current.FullName, "plugins");
                if (IsValidPluginDir(candidate))
                {
                    return candidate;
                }
                current = current.Parent;
            }
        }

        var cwd = Environment.CurrentDirectory;
        if (!string.IsNullOrEmpty(cwd))
        {
            var cwdPlugins = Path.Combine(cwd, "plugins");
            if (IsValidPluginDir(cwdPlugins))
            {
                return cwdPlugins;
            }

            var current = new DirectoryInfo(cwd);
            for (var i = 0; i < 6 && current != null; i++)
            {
                var candidate = Path.Combine(current.FullName, "plugins");
                if (IsValidPluginDir(candidate))
                {
                    return candidate;
                }
                current = current.Parent;
            }
        }

        return Path.Combine(appBase ?? string.Empty, "plugins");
    }

    private static bool IsValidPluginDir(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            return Directory.EnumerateFiles(path, "*avformat*").Any();
        }
        catch
        {
            return false;
        }
    }
}
