// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Management;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Input.Platform;
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
using CraziiEmu.HLE.Configuration;
using CraziiEmu.UI.Input;
using Avalonia.VisualTree;

namespace CraziiEmu.UI;

/// <summary>
/// Code-behind for the main emulator console dashboard.
/// Manages the clock, game carousel, file-picker dialogs, emulation boot sequence,
/// and the Settings overlay.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Size of the virtual memory pool allocated for the guest (64 GB).</summary>
    private const ulong VmmPoolSize = 64UL * 1024 * 1024 * 1024;

    private string? _selectedExecutablePath;


    private readonly UiLogSink _logSink;
    private readonly ConcurrentQueue<ConsoleLine> _logQueue = new();
    private ushort _lastGamepadButtons;

    // Settings fields
    private Button? _bindingButton;
    private PsControllerButton? _bindingProperty;
    private readonly ControllerConfig _controllerConfig;

    /// <summary>
    /// Gets the collection of games displayed in the carousel.
    /// </summary>
    public ObservableCollection<GameItem> Games { get; } = new();

    /// <summary>
    /// Gets the collection of log messages for the console output.
    /// </summary>
    public ObservableCollection<ConsoleLine> ConsoleMessages { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class,
    /// wiring all UI event handlers and starting the real-time clock ticker.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        ConsoleOutput.ItemsSource = ConsoleMessages;
        
        _logSink = new UiLogSink(line => 
        {
            InsertConsoleLine(line);
        });
        CraziiEmuLog.Sink = _logSink;
        CraziiEmuLog.MinimumLevel = LogLevel.Info;

        var consoleWriter = new ConsoleTextWriter(line => InsertConsoleLine(line));
        Console.SetOut(consoleWriter);
        Console.SetError(consoleWriter);

        BtnClearConsole.Click += OnBtnClearConsole;
        BtnCopyConsole.Click += OnBtnCopyConsole;
        BtnExportConsole.Click += OnBtnExportConsole;

        // ── Window chrome ──────────────────────────────────────────────
        BtnClose.Click    += (_, _) => Close();
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        DragHandle.PointerPressed += OnDragHandlePointerPressed;
        SettingsDragHandle.PointerPressed += OnDragHandlePointerPressed;

        // ── Gear icon → Settings Overlay ────────────────────────────────
        BtnSettings.Click += OnOpenConfig;
        BtnCloseSettings.Click += (_, _) => SettingsOverlay.IsVisible = false;
        BtnFullscreen.Click += OnBtnFullscreenClick;

        // ── Game carousel ──────────────────────────────────────────────
        GameCarousel.ItemsSource = Games;
        LoadLibrary();
        
        GameCarousel.SelectionChanged += OnCarouselSelectionChanged;
        GameCarousel.SelectedIndex = Games.Count > 0 ? 0 : -1;

        // ── Action buttons ─────────────────────────────────────────────
        BtnPlay.Click += OnBtnPlay;
        BtnStop.Click += OnBtnStop;
        BtnAddGameTop.Click += OnBtnAddGame;
        BtnAddGameEmpty.Click += OnBtnAddGame;

        // ── Real-time clock ────────────────────────────────────────────
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (_, _) => TxtClock.Text = DateTime.Now.ToString("HH:mm");
        clockTimer.Start();
        TxtClock.Text = DateTime.Now.ToString("HH:mm");

        var logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        logTimer.Tick += OnLogTimerTick;
        logTimer.Start();

        // ── Gamepad polling ────────────────────────────────────────────
        var gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        gamepadTimer.Tick += OnGamepadTick;
        gamepadTimer.Start();

        // ── Settings bindings ──────────────────────────────────────────
        _controllerConfig = new ControllerConfig();
        _controllerConfig.LoadFromBackend();

        SidebarList.SelectionChanged += OnSidebarSelectionChanged;


        InitializeConfigBindings();
        InitializeGpuName();

        ChkConsoleVisible.PropertyChanged += (s, e) => 
        {
            if (e.Property.Name == "IsChecked")
                ConsoleBorder.IsVisible = ChkConsoleVisible.IsChecked == true;
        };

        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel);

        InitializeBindings();

        UpdateEmptyState();

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
        if (SettingsOverlay.IsVisible)
        {
            base.OnKeyDown(e);
            return;
        }

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
            else if (Games.Count == 0)
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
        // Don't intercept gamepad inputs if not active or settings overlay is visible
        if (!IsActive || SettingsOverlay.IsVisible) return;

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
            else if (Games.Count == 0)
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
    //  Configuration Window & Overlay
    // ══════════════════════════════════════════════════════════════

    private void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        SettingsOverlay.IsVisible = true;
    }

    private void InitializeBindings()
    {
        var binds = _controllerConfig.Bindings;
        
        BindCross.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Cross]);
        BindCircle.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Circle]);
        BindSquare.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Square]);
        BindTriangle.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Triangle]);
        
        BindDpadUp.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.DpadUp]);
        BindDpadDown.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.DpadDown]);
        BindDpadLeft.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.DpadLeft]);
        BindDpadRight.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.DpadRight]);

        BindL1.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.L1]);
        BindR1.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.R1]);
        BindL2.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.L2]);
        BindR2.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.R2]);

        BindLeftStickUp.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.LeftStickUp]);
        BindLeftStickDown.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.LeftStickDown]);
        BindLeftStickLeft.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.LeftStickLeft]);
        BindLeftStickRight.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.LeftStickRight]);

        BindRightStickUp.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.RightStickUp]);
        BindRightStickDown.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.RightStickDown]);
        BindRightStickLeft.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.RightStickLeft]);
        BindRightStickRight.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.RightStickRight]);

        BindL3.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.L3]);
        BindR3.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.R3]);
        BindOptions.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Options]);
        BindCreate.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.Create]);
        BindPsButton.Content = ControllerConfig.GetBindingName(binds[PsControllerButton.PsButton]);

        AttachBinding(BindCross, PsControllerButton.Cross); AttachBinding(BindCircle, PsControllerButton.Circle);
        AttachBinding(BindSquare, PsControllerButton.Square); AttachBinding(BindTriangle, PsControllerButton.Triangle);
        AttachBinding(BindDpadUp, PsControllerButton.DpadUp); AttachBinding(BindDpadDown, PsControllerButton.DpadDown);
        AttachBinding(BindDpadLeft, PsControllerButton.DpadLeft); AttachBinding(BindDpadRight, PsControllerButton.DpadRight);
        AttachBinding(BindL1, PsControllerButton.L1); AttachBinding(BindR1, PsControllerButton.R1);
        AttachBinding(BindL2, PsControllerButton.L2); AttachBinding(BindR2, PsControllerButton.R2);
        AttachBinding(BindLeftStickUp, PsControllerButton.LeftStickUp); AttachBinding(BindLeftStickDown, PsControllerButton.LeftStickDown);
        AttachBinding(BindLeftStickLeft, PsControllerButton.LeftStickLeft); AttachBinding(BindLeftStickRight, PsControllerButton.LeftStickRight);
        AttachBinding(BindRightStickUp, PsControllerButton.RightStickUp); AttachBinding(BindRightStickDown, PsControllerButton.RightStickDown);
        AttachBinding(BindRightStickLeft, PsControllerButton.RightStickLeft); AttachBinding(BindRightStickRight, PsControllerButton.RightStickRight);
        AttachBinding(BindL3, PsControllerButton.L3); AttachBinding(BindR3, PsControllerButton.R3);
        AttachBinding(BindOptions, PsControllerButton.Options);
        AttachBinding(BindCreate, PsControllerButton.Create); AttachBinding(BindPsButton, PsControllerButton.PsButton);
    }

    private void AttachBinding(Button btn, PsControllerButton propName)
    {
        btn.Click -= OnBindBtnClicked; // Remove old handlers
        btn.Tag = propName;
        btn.Click += OnBindBtnClicked;
    }

    private void OnBindBtnClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_bindingButton != null)
        {
            InitializeBindings(); // Reset previous if clicked another
        }
        
        _bindingProperty = (PsControllerButton)btn.Tag!;
        _bindingButton = btn;
        btn.Content = "[Press a Key...]";
    }

    private void OnSidebarSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.SelectedItem is not ListBoxItem item) return;
        var tag = item.Tag?.ToString() ?? string.Empty;

        PanelGraphics.IsVisible = tag == "Graphics";
        PanelAudio.IsVisible    = tag == "Audio";
        PanelControls.IsVisible = tag == "Controls";
        PanelHotkeys.IsVisible  = tag == "Hotkeys";
        PanelVisual.IsVisible   = tag == "Debug";
    }

    private Button? _bindingHotkeyButton;
    private string? _bindingHotkeyName;

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bindingHotkeyButton != null && _bindingHotkeyName != null)
        {
            // Strictly enforce F1-F12 keys only
            if (e.Key >= Key.F1 && e.Key <= Key.F12)
            {
                int newVk = 0x70 + (int)(e.Key - Key.F1);
                ApplyHotkeyBinding(newVk);
            }
            e.Handled = true;
            return;
        }

        if (_bindingButton != null && _bindingProperty.HasValue)
        {
            var vk = ControllerConfig.KeyToVirtualKey(e.Key);
            if (vk != 0) ApplyBinding(vk);
            e.Handled = true;
            return;
        }

        var pressedVk = ControllerConfig.KeyToVirtualKey(e.Key);
        if (pressedVk != 0)
        {
            var config = CraziiEmuConfig.Instance;
            if (pressedVk == config.HotkeyVerboseConsole)
            {
                ChkConsoleVisible.IsChecked = !(ChkConsoleVisible.IsChecked == true);
                e.Handled = true;
            }
            else if (pressedVk == config.HotkeyMetricsOverlay)
            {
                CraziiEmu.Libs.VideoOut.Overlay.OverlayRenderer.CycleMode();
                e.Handled = true;
            }
        }
    }

    private Avalonia.Point _lastMousePos;

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bindingButton != null && _bindingProperty.HasValue)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed) ApplyBinding(InputMap.MouseLeft);
            else if (props.IsRightButtonPressed) ApplyBinding(InputMap.MouseRight);
            else if (props.IsMiddleButtonPressed) ApplyBinding(InputMap.MouseMiddle);
            e.Handled = true;
        }
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        // Removed mouse movement bindings per user request
        _lastMousePos = e.GetPosition(this);
    }

    private void OnRestoreDefaultControlsClicked(object? sender, RoutedEventArgs e)
    {
        _controllerConfig.SetGlobalDefaults();
        _controllerConfig.SaveToBackend();
        InitializeBindings();
    }

    private void ApplyBinding(int newKey)
    {
        var targetBtn = _bindingProperty!.Value;
        var map = _controllerConfig.Bindings;

        // Duplicate Swapping Algorithm
        if (map.ContainsValue(newKey))
        {
            var conflictKey = map.First(x => x.Value == newKey).Key;
            if (conflictKey != targetBtn)
            {
                var oldKey = map[targetBtn];
                map[targetBtn] = newKey;
                map[conflictKey] = oldKey;
            }
        }
        else
        {
            map[targetBtn] = newKey;
        }

        _controllerConfig.SaveToBackend();
        _bindingButton = null;
        _bindingProperty = null;
        InitializeBindings();
    }


    // ══════════════════════════════════════════════════════════════
    //  Carousel
    // ══════════════════════════════════════════════════════════════

    private async void OnCarouselSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GameCarousel.SelectedItem is GameItem selected)
        {
            _selectedExecutablePath = selected.ExecutablePath;
            UpdateCarouselFooter(selected.Title);

            string? picPath = null;
            if (!string.IsNullOrEmpty(selected.ExecutablePath))
            {
                var directory = System.IO.Path.GetDirectoryName(selected.ExecutablePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    picPath = System.IO.Path.Combine(directory, "sce_sys", "pic0.png");
                    if (!System.IO.File.Exists(picPath))
                        picPath = null;
                }
            }

            if (picPath != null)
            {
                try 
                { 
                    var bitmap = await System.Threading.Tasks.Task.Run(() => new Avalonia.Media.Imaging.Bitmap(picPath));
                    // Only apply if the selection hasn't moved on
                    if (_selectedExecutablePath == selected.ExecutablePath)
                    {
                        WallpaperImage.Source = bitmap;
                    }
                }
                catch { WallpaperImage.Source = null; }
            }
            else
            {
                WallpaperImage.Source = null;
            }
        }
    }

    /// <summary>
    /// Updates the game-info footer (subtitle, title, action buttons) to match the
    /// currently selected carousel card.
    /// </summary>
    /// <param name="title">Game title to display.</param>
    private void UpdateCarouselFooter(string? title)
    {
        BtnPlay.IsVisible = true;
        TxtSelectedSubtitle.Text = "PS5";

        string displayTitle = title ?? "Unknown Title";
        int bracketIndex = displayTitle.IndexOf(" [");
        if (bracketIndex > 0)
        {
            displayTitle = displayTitle.Substring(0, bracketIndex);
        }

        TxtSelectedTitle.Text = displayTitle;
    }

    private void UpdateEmptyState()
    {
        bool isEmpty = Games.Count == 0;
        EmptyStatePrompt.IsVisible = isEmpty;
        MainScrollViewer.IsVisible = !isEmpty;
        
        // Hide footer if empty
        if (isEmpty)
        {
            TxtSelectedTitle.Text = "";
            TxtSelectedSubtitle.Text = "";
            BtnPlay.IsVisible = false;
        }
    }

    private void SaveLibrary()
    {
        try
        {
            var libraryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CraziiEmu", "library.json");
            var dir = System.IO.Path.GetDirectoryName(libraryPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var gamesToSave = Games.ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(gamesToSave, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(libraryPath, json);
        }
        catch (Exception ex)
        {
            AppendConsole($"[Library] Failed to save library: {ex.Message}");
        }
    }

    private void LoadLibrary()
    {
        try
        {
            var libraryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CraziiEmu", "library.json");
            if (System.IO.File.Exists(libraryPath))
            {
                var json = System.IO.File.ReadAllText(libraryPath);
                var loadedGames = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<GameItem>>(json);
                if (loadedGames != null)
                {
                    foreach (var g in loadedGames)
                    {
                        if (!string.IsNullOrEmpty(g.BoxartPath) && System.IO.File.Exists(g.BoxartPath))
                        {
                            try { g.CoverArt = new Avalonia.Media.Imaging.Bitmap(g.BoxartPath); }
                            catch { }
                        }
                        Games.Add(g);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendConsole($"[Library] Failed to load library: {ex.Message}");
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Emulation Execution
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens a folder picker so the user can add a game folder to the library.
    /// </summary>
    private async void OnBtnAddGame(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = "Add Game — Select Game Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folderPath = folders[0].Path.LocalPath;
            var ebootPath = System.IO.Path.Combine(folderPath, "eboot.bin");

            if (!System.IO.File.Exists(ebootPath))
            {
                AppendConsole($"[Library] Could not find eboot.bin in folder: {folderPath}");
                return;
            }

            var path = ebootPath;

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

            var filename = System.IO.Path.GetFileName(folderPath);
            var title = filename;
            var titleId = string.Empty;
            var version = string.Empty;

            var paramPath = System.IO.Path.Combine(folderPath, "sce_sys", "param.json");
            if (System.IO.File.Exists(paramPath))
            {
                var data = System.IO.File.ReadAllBytes(paramPath);
                var meta = Ps5ParamJsonReader.TryReadPs5Param(data);
                
                if (!string.IsNullOrEmpty(meta.Title)) title = meta.Title;
                if (!string.IsNullOrEmpty(meta.TitleId)) titleId = meta.TitleId;
                if (!string.IsNullOrEmpty(meta.Version)) version = meta.Version;
            }
            
            var coverPath = FindCoverFor(path);
            Avalonia.Media.Imaging.Bitmap? coverArt = null;
            if (coverPath != null)
            {
                try
                {
                    coverArt = new Avalonia.Media.Imaging.Bitmap(coverPath);
                }
                catch
                {
                    // Ignore decode errors
                }
            }

            var newGame = new GameItem 
            { 
                Title = !string.IsNullOrEmpty(version) ? $"{title} [{titleId}] v{version}" : title, 
                ExecutablePath = path,
                BoxartPath = coverPath ?? string.Empty,
                CoverArt = coverArt
            };
            
            Games.Add(newGame);
            AppendConsole($"[Library] Added: {path}");
            
            SaveLibrary();
            UpdateEmptyState();

            // Auto-select the newly added game
            GameCarousel.SelectedIndex = Games.Count - 1;
        }
    }

    /// <summary>
    /// Finds the cover art shipped with the game: sce_sys/icon0.png next to
    /// the executable (falling back to pic0.png).
    /// </summary>
    private static string? FindCoverFor(string ebootPath)
    {
        var directory = System.IO.Path.GetDirectoryName(ebootPath);
        if (directory is null)
        {
            return null;
        }

        var sceSys = System.IO.Path.Combine(directory, "sce_sys");
        foreach (var candidate in new[] { "icon0.png", "pic0.png" })
        {
            var coverPath = System.IO.Path.Combine(sceSys, candidate);
            if (System.IO.File.Exists(coverPath))
            {
                return coverPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Handles the Play button: boots the emulation engine on background threads
    /// using CraziiEmuRuntime.
    /// </summary>
    private System.Diagnostics.Process? _gameProcess;

    private void OnBtnPlay(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedExecutablePath))
        {
            AppendConsole("[Emulation] No executable loaded — add a game first.");
            return;
        }

        if (_gameProcess is not null && !_gameProcess.HasExited)
        {
            AppendConsole("[Emulation] Game is already running.");
            return;
        }

        AppendConsole("[Emulation] Starting boot sequence…");

        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                AppendConsole("[Emulation] Boot failed: Could not determine executable path.");
                return;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = processPath,
                Arguments = $"--play-game \"{_selectedExecutablePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _gameProcess = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _gameProcess.OutputDataReceived += (s, ev) => 
            {
                if (!string.IsNullOrEmpty(ev.Data))
                {
                    Dispatcher.UIThread.Post(() => AppendConsole(ev.Data, "#AAAAAA"));
                }
            };

            _gameProcess.ErrorDataReceived += (s, ev) => 
            {
                if (!string.IsNullOrEmpty(ev.Data))
                {
                    Dispatcher.UIThread.Post(() => AppendConsole(ev.Data, "#FFAAAA"));
                }
            };

            _gameProcess.Exited += (s, ev) =>
            {
                var exitCode = _gameProcess.ExitCode;
                Dispatcher.UIThread.Post(() => 
                {
                    if (exitCode != 0)
                    {
                        AppendConsole($"[Emulation] Halted unexpectedly (Exit Code: {exitCode}).", "#FF5555");
                    }
                    else
                    {
                        AppendConsole("[Emulation] Finished successfully.");
                    }

                    BtnPlay.IsVisible = true;
                    BtnStop.IsVisible = false;
                    _gameProcess?.Dispose();
                    _gameProcess = null;
                });
            };

            _gameProcess.Start();
            _gameProcess.BeginOutputReadLine();
            _gameProcess.BeginErrorReadLine();

            BtnPlay.IsVisible = false;
            BtnStop.IsVisible = true;
            ConsoleMessages.Clear();
            AppendConsole("[Emulation] Running in sub-process.");
        }
        catch (Exception ex)
        {
            AppendConsole($"[Emulation] Boot failed: {ex.Message}");
        }
    }

    private void OnBtnStop(object? sender, RoutedEventArgs e)
    {
        if (_gameProcess is not null && !_gameProcess.HasExited)
        {
            AppendConsole("[Emulation] Force terminating game...");
            try
            {
                _gameProcess.Kill();
            }
            catch (Exception ex)
            {
                AppendConsole($"[Emulation] Failed to terminate game: {ex.Message}");
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Console Log
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Appends a timestamped message to the console output area.
    /// Thread-safe — may be called from any thread.
    /// </summary>
    internal void AppendConsole(string message, string color = "White")
    {
        var line = new ConsoleLine { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", Color = color };
        InsertConsoleLine(line);
    }

    private ScrollViewer? _consoleScroller;

    private void InsertConsoleLine(ConsoleLine line)
    {
        _logQueue.Enqueue(line);
    }

    private void OnLogTimerTick(object? sender, EventArgs e)
    {
        if (_logQueue.IsEmpty) return;

        bool isAtBottom = true;
        
        if (_consoleScroller == null)
        {
            _consoleScroller = ConsoleOutput.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }
        
        if (_consoleScroller != null)
        {
            isAtBottom = _consoleScroller.Offset.Y >= _consoleScroller.Extent.Height - _consoleScroller.Viewport.Height - 10;
        }

        int count = 0;
        ConsoleLine? lastLine = null;
        while (count < 2000 && _logQueue.TryDequeue(out var line))
        {
            ConsoleMessages.Add(line);
            lastLine = line;
            count++;
        }
        
        while (ConsoleMessages.Count > 10000)
        {
            ConsoleMessages.RemoveAt(0);
        }
        
        if (isAtBottom && lastLine != null)
        {
            ConsoleOutput.ScrollIntoView(lastLine);
        }
    }

    private async void OnBtnCopyConsole(object? sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine, ConsoleMessages.Select(m => m.Text));
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
            AppendConsole("[UI] Console logs copied to clipboard.", "#00FF00");
        }
    }

    private void OnBtnClearConsole(object? sender, RoutedEventArgs e)
    {
        ConsoleMessages.Clear();
    }

    private async void OnBtnExportConsole(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider != null && topLevel.StorageProvider.CanSave)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Console Logs",
                DefaultExtension = "txt",
                SuggestedFileName = $"CraziiEmu_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            });

            if (file != null)
            {
                // Get last 100 lines
                var lines = ConsoleMessages.Skip(Math.Max(0, ConsoleMessages.Count - 100)).Select(m => m.Text);
                var text = string.Join(Environment.NewLine, lines);
                try
                {
                    await using var stream = await file.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(text);
                    AppendConsole($"[UI] Logs exported to {file.Name}.", "#00FF00");
                }
                catch (Exception ex)
                {
                    AppendConsole($"[UI] Failed to export logs: {ex.Message}", "#FF0000");
                }
            }
        }
    }



    // Removed SetWallpaper

    private void InitializeConfigBindings()
    {
        var config = CraziiEmuConfig.Instance;
        
        if (config.UiFullscreenOnStartup)
        {
            WindowState = Avalonia.Controls.WindowState.FullScreen;
        }

        ChkUiFullscreen.IsChecked = config.UiFullscreenOnStartup;
        ChkUiFullscreen.IsCheckedChanged += (s, e) => 
        { 
            config.UiFullscreenOnStartup = ChkUiFullscreen.IsChecked == true; 
            config.Save(); 
        };

        CmbGraphicsApi.SelectedIndex = config.GraphicsApi == "OpenGL" ? 1 : 0;
        CmbGraphicsApi.SelectionChanged += (s, e) => 
        {
            config.GraphicsApi = CmbGraphicsApi.SelectedIndex == 1 ? "OpenGL" : "Vulkan";
            config.Save();
        };

        PopulateResolutions(CmbGraphicsScale, config.Resolution);
        CmbGraphicsScale.SelectionChanged += (s, e) =>
        {
            if (CmbGraphicsScale.SelectedItem is string selRes)
            {
                config.Resolution = selRes;
                config.Save();
            }
        };

        ChkAudio.IsChecked = config.EnableAudio;
        ChkAudio.IsCheckedChanged += (s, e) => { config.EnableAudio = ChkAudio.IsChecked == true; config.Save(); };
        
        SldVolume.Value = config.MasterVolume;
        SldVolume.ValueChanged += (s, e) => { config.MasterVolume = (float)SldVolume.Value; config.Save(); };

        CmbMetricsOverlay.SelectedIndex = Math.Clamp(config.MetricsOverlayMode, 0, 3);
        CmbMetricsOverlay.SelectionChanged += (s, e) =>
        {
            config.MetricsOverlayMode = CmbMetricsOverlay.SelectedIndex;
            config.Save();
            CraziiEmu.Libs.VideoOut.Overlay.OverlayRenderer.Mode = (CraziiEmu.Libs.VideoOut.Overlay.OverlayMode)config.MetricsOverlayMode;
        };

        InitializeHotkeyBindings();
    }

    private static void PopulateResolutions(ComboBox cmb, string selectedResolution)
    {
        var list = new System.Collections.Generic.List<string>();
        try
        {
            var displays = CraziiEmu.Libs.VideoOut.HostDisplayCatalog.Query();
            if (displays.Count > 0)
            {
                foreach (var mode in displays[0].Modes)
                {
                    if (mode.Width > 0 && mode.Height > 0)
                    {
                        var resStr = $"{mode.Width}x{mode.Height}";
                        if (!list.Contains(resStr, StringComparer.OrdinalIgnoreCase))
                        {
                            list.Add(resStr);
                        }
                    }
                }
            }
        }
        catch { }

        string[] standards = ["3840x2160", "2560x1440", "1920x1080", "1600x900", "1280x720"];
        foreach (var std in standards)
        {
            if (!list.Contains(std, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(std);
            }
        }

        var ordered = list
            .Select(r =>
            {
                var parts = r.Split(['x', 'X', '×', ' '], StringSplitOptions.RemoveEmptyEntries);
                int w = int.TryParse(parts[0], out var pw) ? pw : 0;
                int h = parts.Length > 1 && int.TryParse(parts[1], out var ph) ? ph : 0;
                return (Text: r, Area: w * h, W: w, H: h);
            })
            .OrderByDescending(x => x.Area)
            .ThenByDescending(x => x.W)
            .Select(x => x.Text)
            .ToList();

        cmb.ItemsSource = ordered;
        var foundIndex = ordered.FindIndex(r => string.Equals(r, selectedResolution, StringComparison.OrdinalIgnoreCase));
        if (foundIndex >= 0)
        {
            cmb.SelectedIndex = foundIndex;
        }
        else
        {
            var idx1080 = ordered.FindIndex(r => r.Equals("1920x1080", StringComparison.OrdinalIgnoreCase));
            cmb.SelectedIndex = idx1080 >= 0 ? idx1080 : 0;
        }
    }

    private void InitializeHotkeyBindings()
    {
        var config = CraziiEmuConfig.Instance;
        BindHotkeyOverlay.Content = GetFKeyName(config.HotkeyMetricsOverlay);
        BindHotkeyConsole.Content = GetFKeyName(config.HotkeyVerboseConsole);

        AttachHotkeyBinding(BindHotkeyOverlay, "MetricsOverlay");
        AttachHotkeyBinding(BindHotkeyConsole, "VerboseConsole");
    }

    private void AttachHotkeyBinding(Button btn, string hotkeyName)
    {
        btn.Click -= OnBindHotkeyBtnClicked;
        btn.Tag = hotkeyName;
        btn.Click += OnBindHotkeyBtnClicked;
    }

    private void OnBindHotkeyBtnClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_bindingButton != null || _bindingHotkeyButton != null)
        {
            InitializeBindings();
            InitializeHotkeyBindings();
        }

        _bindingHotkeyName = (string)btn.Tag!;
        _bindingHotkeyButton = btn;
        btn.Content = "[Press F Key...]";
    }

    private static string GetFKeyName(int vk)
    {
        if (vk >= 0x70 && vk <= 0x7B)
        {
            return $"F{vk - 0x70 + 1}";
        }
        return "F3";
    }

    private void OnRestoreDefaultHotkeysClicked(object? sender, RoutedEventArgs e)
    {
        var config = CraziiEmuConfig.Instance;
        config.HotkeyMetricsOverlay = 0x72; // F3
        config.HotkeyVerboseConsole = 0x73; // F4
        config.Save();
        InitializeHotkeyBindings();
    }

    private void ApplyHotkeyBinding(int newVk)
    {
        var config = CraziiEmuConfig.Instance;
        var hotkeyName = _bindingHotkeyName!;

        if (hotkeyName == "MetricsOverlay")
        {
            if (config.HotkeyVerboseConsole == newVk)
            {
                config.HotkeyVerboseConsole = config.HotkeyMetricsOverlay;
            }
            config.HotkeyMetricsOverlay = newVk;
        }
        else if (hotkeyName == "VerboseConsole")
        {
            if (config.HotkeyMetricsOverlay == newVk)
            {
                config.HotkeyMetricsOverlay = config.HotkeyVerboseConsole;
            }
            config.HotkeyVerboseConsole = newVk;
        }

        config.Save();
        _bindingHotkeyButton = null;
        _bindingHotkeyName = null;
        InitializeHotkeyBindings();
    }

    private void InitializeGpuName()
    {
        string gpuName = "Default System GPU";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["Name"] != null)
                    {
                        gpuName = obj["Name"].ToString() ?? "Default System GPU";
                        break;
                    }
                }
            }
            catch { }
        }
        
        TxtGraphicsDevice.Text = gpuName;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try
        {
            if (_gameProcess is not null && !_gameProcess.HasExited)
            {
                _gameProcess.Kill();
            }
        }
        catch { }
    }
}

public class ConsoleTextWriter : System.IO.TextWriter
{
    private readonly Action<ConsoleLine> _onLine;
    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public ConsoleTextWriter(Action<ConsoleLine> onLine)
    {
        _onLine = onLine;
    }

    public override void WriteLine(string? value)
    {
        if (value != null)
        {
            var color = "White";
            if (value.Contains("[ERROR]") || value.Contains("[CRASH]") || value.Contains("[FATAL]")) color = "Red";
            else if (value.Contains("[WARN]")) color = "Yellow";
            else if (value.Contains("[TRACE]") || value.Contains("[DEBUG]")) color = "Gray";

            var line = new ConsoleLine { Text = value, Color = color };
            
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                _onLine(line);
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _onLine(line));
        }
    }
}
