// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CraziiEmu.UI;

/// <summary>
/// Avalonia application class. Initializes XAML resources and creates the main window.
/// </summary>
public class App : Application
{
    /// <summary>
    /// Loads the XAML-defined application resources (theme, styles).
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the framework initialization is completed. Sets the main window.
    /// </summary>
    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();

            // Asynchronously wait for 2.5 seconds
            await System.Threading.Tasks.Task.Delay(2500);

            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Show();
            
            splash.Close();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
