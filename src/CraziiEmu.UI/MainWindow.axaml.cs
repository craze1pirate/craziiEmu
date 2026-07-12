// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Gpu;
using CraziiEmu.Core.HLE;
using Avalonia.Media.Imaging;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.Runtime;
using CraziiEmu.Logging;
using CraziiEmu.HLE.Input;

namespace CraziiEmu.UI;

/// <summary>
/// Code-behind for the main emulator console dashboard.
/// Manages the clock, game carousel, file-picker dialogs, emulation boot sequence,
/// and the <see cref="ConfigWindow"/> modal.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Size of the virtual memory pool allocated for the guest (64 GB).</summary>
    private const ulong VmmPoolSize = 64UL * 1024 * 1024 * 1024;

    private string? _selectedExecutablePath;
    private string? _firmwareDirectoryPath;
    private Thread? _cpuThread;
    private ICraziiEmuRuntime? _runtime;
    private readonly UiLogSink _logSink;
    private ushort _lastGamepadButtons;

    /// <summary>
    /// Gets the collection of games displayed in the carousel.
    /// </summary>
    public ObservableCollection<GameItem> Games { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class,
    /// wiring all UI event handlers and starting the real-time clock ticker.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        _logSink = new UiLogSink(ConsoleOutput);
        CraziiEmuLog.Sink = _logSink;

        // ── Window chrome ──────────────────────────────────────────────
        BtnClose.Click    += (_, _) => Close();
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        DragHandle.PointerPressed += OnDragHandlePointerPressed;

        // ── Gear icon → ConfigWindow ────────────────────────────────────
        BtnSettings.Click += OnOpenConfig;
        BtnFullscreen.Click += OnBtnFullscreenClick;

        // ── Game carousel ──────────────────────────────────────────────
        GameCarousel.ItemsSource = Games;
        Games.Add(new GameItem { IsAddCard = true, Title = "Add a Game" });
        
        GameCarousel.SelectionChanged += OnCarouselSelectionChanged;
        GameCarousel.SelectedIndex = 0;

        // ── Action buttons ─────────────────────────────────────────────
        BtnPlay.Click    += OnBtnPlay;
        BtnAddGame.Click += OnBtnAddGame;

        // ── Real-time clock ────────────────────────────────────────────
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (_, _) => TxtClock.Text = DateTime.Now.ToString("HH:mm");
        clockTimer.Start();
        TxtClock.Text = DateTime.Now.ToString("HH:mm");

        // ── Gamepad polling ────────────────────────────────────────────
        var gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        gamepadTimer.Tick += OnGamepadTick;
        gamepadTimer.Start();

        // Set initial footer state for the "+" card
        UpdateCarouselFooter(isAddCard: true, title: null);

        AppendConsole("[CraziiEmu] UI initialized. Ready.");
    }

    // ══════════════════════════════════════════════════════════════
    //  Input Navigation (Keyboard & Gamepad)
    // ══════════════════════════════════════════════════════════════

    private bool _isHeaderFocused = false;
    private int _headerFocusIndex = 0; // 0 = Settings, 1 = Fullscreen

    private int _gamepadRepeatDelay = 0;
    private ushort _gamepadRepeatButton = 0;

    private void UpdateHeaderFocus()
    {
        BtnFullscreen.Classes.Remove("HeaderBtnFocused");
        BtnSettings.Classes.Remove("HeaderBtnFocused");
        if (_isHeaderFocused)
        {
            if (_headerFocusIndex == 0) BtnSettings.Classes.Add("HeaderBtnFocused");
            else BtnFullscreen.Classes.Add("HeaderBtnFocused");
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Up && !_isHeaderFocused)
        {
            _isHeaderFocused = true;
            UpdateHeaderFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _isHeaderFocused)
        {
            _isHeaderFocused = false;
            UpdateHeaderFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            if (_isHeaderFocused)
            {
                _headerFocusIndex = 0;
                UpdateHeaderFocus();
            }
            else if (GameCarousel.SelectedIndex > 0)
            {
                GameCarousel.SelectedIndex--;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            if (_isHeaderFocused)
            {
                _headerFocusIndex = 1;
                UpdateHeaderFocus();
            }
            else if (GameCarousel.SelectedIndex < GameCarousel.ItemCount - 1)
            {
                GameCarousel.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            if (_isHeaderFocused)
            {
                if (_headerFocusIndex == 0) OnOpenConfig(this, new RoutedEventArgs());
                else OnBtnFullscreenClick(this, new RoutedEventArgs());
            }
            else if (GameCarousel.SelectedIndex == 0)
            {
                OnBtnAddGame(this, new RoutedEventArgs());
            }
            else if (BtnPlay.IsVisible)
            {
                OnBtnPlay(this, new RoutedEventArgs());
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void OnGamepadTick(object? sender, EventArgs e)
    {
        // Don't intercept gamepad inputs if a modal (like ConfigWindow) is active
        if (!IsActive) return;

        ushort buttons = GamepadHandler.GetButtons();
        ushort pressed = (ushort)(buttons & ~_lastGamepadButtons);
        _lastGamepadButtons = buttons;

        // Auto-repeat logic for navigation
        ushort activeNavButton = 0;
        if (pressed != 0)
        {
            _gamepadRepeatButton = pressed;
            _gamepadRepeatDelay = 25; // ~400ms initial delay
            activeNavButton = pressed;
        }
        else if (buttons == _gamepadRepeatButton && buttons != 0)
        {
            if (_gamepadRepeatDelay > 0)
            {
                _gamepadRepeatDelay--;
            }
            else
            {
                activeNavButton = buttons;
                _gamepadRepeatDelay = 4; // ~64ms repeat rate
            }
        }
        else
        {
            _gamepadRepeatButton = 0;
        }

        if ((activeNavButton & GamepadHandler.DPAD_UP) != 0 && !_isHeaderFocused)
        {
            _isHeaderFocused = true;
            UpdateHeaderFocus();
        }
        else if ((activeNavButton & GamepadHandler.DPAD_DOWN) != 0 && _isHeaderFocused)
        {
            _isHeaderFocused = false;
            UpdateHeaderFocus();
        }
        else if ((activeNavButton & GamepadHandler.DPAD_LEFT) != 0)
        {
            if (_isHeaderFocused)
            {
                _headerFocusIndex = 0;
                UpdateHeaderFocus();
            }
            else if (GameCarousel.SelectedIndex > 0)
            {
                GameCarousel.SelectedIndex--;
            }
        }
        else if ((activeNavButton & GamepadHandler.DPAD_RIGHT) != 0)
        {
            if (_isHeaderFocused)
            {
                _headerFocusIndex = 1;
                UpdateHeaderFocus();
            }
            else if (GameCarousel.SelectedIndex < GameCarousel.ItemCount - 1)
            {
                GameCarousel.SelectedIndex++;
            }
        }
        
        // Action button is strictly on press, no repeat
        if ((pressed & GamepadHandler.BTN_A) != 0)
        {
            if (_isHeaderFocused)
            {
                if (_headerFocusIndex == 0) OnOpenConfig(this, new RoutedEventArgs());
                else OnBtnFullscreenClick(this, new RoutedEventArgs());
            }
            else if (GameCarousel.SelectedIndex == 0)
            {
                OnBtnAddGame(this, new RoutedEventArgs());
            }
            else if (BtnPlay.IsVisible)
            {
                OnBtnPlay(this, new RoutedEventArgs());
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Window Chrome
    // ══════════════════════════════════════════════════════════════

    private void OnBtnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    private void OnDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    // ══════════════════════════════════════════════════════════════
    //  Configuration Window
    // ══════════════════════════════════════════════════════════════

    private void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var cfg = new ConfigWindow();
        cfg.OnFirmwareDirectoryChanged = path =>
        {
            _firmwareDirectoryPath = path;
            AppendConsole($"[System] Firmware directory: {path}");
        };
        cfg.OnWallpaperChanged = path => SetWallpaper(path);
        cfg.OnConsoleVisibilityChanged = isVisible => ConsoleBorder.IsVisible = isVisible;
        cfg.ShowDialog(this);
    }

    // ══════════════════════════════════════════════════════════════
    //  Carousel
    // ══════════════════════════════════════════════════════════════

    private void OnCarouselSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GameCarousel.SelectedItem is GameItem selected)
        {
            _selectedExecutablePath = selected.ExecutablePath;
            UpdateCarouselFooter(selected.IsAddCard, selected.IsAddCard ? null : selected.Title);
        }
    }

    /// <summary>
    /// Updates the game-info footer (subtitle, title, action buttons) to match the
    /// currently selected carousel card.
    /// </summary>
    /// <param name="isAddCard"><c>true</c> if the "+" Add Game card is selected.</param>
    /// <param name="title">Game title to display, or <c>null</c> when <paramref name="isAddCard"/> is true.</param>
    private void UpdateCarouselFooter(bool isAddCard, string? title)
    {
        BtnPlay.IsVisible    = !isAddCard;
        BtnAddGame.IsVisible =  isAddCard;

        if (isAddCard)
        {
            TxtSelectedSubtitle.Text = string.Empty;
            TxtSelectedTitle.Text    = "Add a Game";
        }
        else
        {
            TxtSelectedSubtitle.Text = "PS5 · ELF Executable";
            TxtSelectedTitle.Text    = title ?? "Unknown Title";
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Action Buttons
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a file picker so the user can add a decrypted ELF or directory to the library.
    /// </summary>
    private async void OnBtnAddGame(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Add Game — Select ELF or eboot.bin",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ELF Executables") { Patterns = new[] { "*.elf", "*.bin", "eboot.bin" } },
                new FilePickerFileType("All Files")       { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;

            // Prevent duplicate entries
            for (int i = 0; i < Games.Count; i++)
            {
                if (string.Equals(Games[i].ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    GameCarousel.SelectedIndex = i;
                    AppendConsole($"[Library] Game already exists in library: {path}");
                    return;
                }
            }

            var filename = System.IO.Path.GetFileName(path);
            var title = filename;
            var titleId = string.Empty;
            var version = string.Empty;

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                var paramPath = System.IO.Path.Combine(dir, "sce_sys", "param.json");
                if (System.IO.File.Exists(paramPath))
                {
                    var data = System.IO.File.ReadAllBytes(paramPath);
                    var meta = Ps5ParamJsonReader.TryReadPs5Param(data);
                    
                    if (!string.IsNullOrEmpty(meta.Title)) title = meta.Title;
                    if (!string.IsNullOrEmpty(meta.TitleId)) titleId = meta.TitleId;
                    if (!string.IsNullOrEmpty(meta.Version)) version = meta.Version;
                }
            }
            
            var newGame = new GameItem 
            { 
                IsAddCard = false, 
                Title = !string.IsNullOrEmpty(version) ? $"{title} [{titleId}] v{version}" : title, 
                ExecutablePath = path 
            };
            
            Games.Add(newGame);
            AppendConsole($"[Library] Added: {path}");
            
            // Auto-select the newly added game
            GameCarousel.SelectedIndex = Games.Count - 1;
        }
    }

    /// <summary>
    /// Handles the Play button: boots the emulation engine on background threads
    /// using CraziiEmuRuntime.
    /// </summary>
    private void OnBtnPlay(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedExecutablePath))
        {
            AppendConsole("[Emulation] No executable loaded — add a game first.");
            return;
        }

        AppendConsole("[Emulation] Starting boot sequence…");

        try
        {
            var options = new CraziiEmuRuntimeOptions
            {
                // Can configure more options based on UI later if needed
            };

            _runtime = CraziiEmuRuntime.CreateDefault(options);

            _cpuThread = new Thread(() =>
            {
                try 
                {
                    var result = _runtime.Run(_selectedExecutablePath);
                    AppendConsole($"[Emulation] Finished with result: {result}");
                }
                catch (Exception ex) 
                {
                    // Redirect the exception safely to our UI log sink instead of crashing the app
                    CraziiEmuLog.For("CPU").Error($"[CraziiEmu] Emulation Halted: {ex.Message}");
                    CraziiEmuLog.For("CPU").Error(ex.StackTrace ?? string.Empty);
                }
                finally
                {
                    Dispatcher.UIThread.Post(() => BtnPlay.IsEnabled = true);
                }
            }) { IsBackground = true, Name = "CraziiEmu-CPU" };
            _cpuThread.Start();

            // Note: The UI display thread is no longer explicitly created here because 
            // CraziiEmuRuntime natively spins up VulkanVideoPresenter automatically when AGC is invoked.
            
            BtnPlay.IsEnabled = false;
            AppendConsole("[Emulation] Running.");
        }
        catch (Exception ex)
        {
            AppendConsole($"[Emulation] Boot failed: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Console Log
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Appends a timestamped message to the console output area.
    /// Thread-safe — may be called from any thread.
    /// </summary>
    internal void AppendConsole(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";

        if (Dispatcher.UIThread.CheckAccess())
        {
            ConsoleOutput.Text += line;
            ConsoleOutput.CaretIndex = ConsoleOutput.Text?.Length ?? 0;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConsoleOutput.Text += line;
                ConsoleOutput.CaretIndex = ConsoleOutput.Text?.Length ?? 0;
            });
        }
    }

    /// <summary>
    /// Sets the wallpaper image shown beneath the particles.
    /// </summary>
    public void SetWallpaper(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            WallpaperImage.Source = null;
            AppendConsole("[UI] Wallpaper cleared");
            return;
        }

        if (System.IO.File.Exists(path))
        {
            try
            {
                WallpaperImage.Source = new Bitmap(path);
                AppendConsole($"[UI] Wallpaper set to {path}");
            }
            catch (Exception ex)
            {
                AppendConsole($"[UI] Failed to load wallpaper: {ex.Message}");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _runtime?.Dispose();
    }
}
