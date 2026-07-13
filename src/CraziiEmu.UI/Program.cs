// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;

namespace CraziiEmu.UI;

/// <summary>
/// Application entry point. Configures and launches the Avalonia desktop lifetime.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The main entry point for the CraziiEmu UI application.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        args = CraziiEmu.Core.Runtime.WindowsMitigationHelper.NormalizeInternalArguments(args, out var isMitigatedChild);
        if (!isMitigatedChild && CraziiEmu.Core.Runtime.WindowsMitigationHelper.TryRunMitigatedChild(args, out var childExitCode))
        {
            return childExitCode;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures the Avalonia application builder with platform detection,
    /// Inter font family, and trace-level logging.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
