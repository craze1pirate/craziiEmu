// Copyright (C) 2026 CraziiEmu Project
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

        if (args.Length >= 2 && args[0] == "--play-game")
        {
            try
            {
                var config = CraziiEmu.HLE.Configuration.CraziiEmuConfig.Instance;
                CraziiEmu.Libs.VideoOut.Overlay.OverlayRenderer.Mode =
                    (CraziiEmu.Libs.VideoOut.Overlay.OverlayMode)Math.Clamp(config.MetricsOverlayMode, 0, 3);

                var (resW, resH) = config.GetResolution();
                var videoOptions = new CraziiEmu.Libs.VideoOut.HostVideoOptions
                {
                    Width = resW,
                    Height = resH,
                    WindowMode = config.UiFullscreenOnStartup
                        ? CraziiEmu.Libs.VideoOut.HostWindowMode.Borderless
                        : CraziiEmu.Libs.VideoOut.HostWindowMode.Windowed,
                };
                CraziiEmu.Libs.VideoOut.HostVideoHost.TryConfigureVideo(videoOptions);

                var options = new CraziiEmu.Core.Runtime.CraziiEmuRuntimeOptions();
                using var runtime = CraziiEmu.Core.Runtime.CraziiEmuRuntime.CreateDefault(options);
                var result = runtime.Run(args[1]);
                Console.WriteLine($"[Emulation] Finished with result: {result}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CraziiEmu] Emulation Halted: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace ?? string.Empty);
                return 1;
            }
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
