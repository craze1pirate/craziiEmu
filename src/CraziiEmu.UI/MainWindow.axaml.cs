// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Linq;
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

    private Thread? _cpuThread;
    private ICraziiEmuRuntime? _runtime;
    private readonly UiLogSink _logSink;
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
        BtnAddGameTop.Click += OnBtnAddGame;
        BtnAddGameEmpty.Click += OnBtnAddGame;

        // ── Real-time clock ────────────────────────────────────────────
        var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clockTimer.Tick += (_, _) => TxtClock.Text = DateTime.Now.ToString("HH:mm");
        clockTimer.Start();
        TxtClock.Text = DateTime.Now.ToString("HH:mm");

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
        PanelVisual.IsVisible   = tag == "Debug";
    }

    // Removed wallpaper methods

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bindingButton != null && _bindingProperty.HasValue)
        {
            var vk = ControllerConfig.KeyToVirtualKey(e.Key);
            if (vk != 0) ApplyBinding(vk);
            e.Handled = true;
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

            _runtime?.Dispose();
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
                    Dispatcher.UIThread.Post(() => 
                    {
                        BtnPlay.IsEnabled = true;
                    });
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
    internal void AppendConsole(string message, string color = "White")
    {
        var line = new ConsoleLine { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", Color = color };

        if (Dispatcher.UIThread.CheckAccess())
        {
            InsertConsoleLine(line);
        }
        else
        {
            Dispatcher.UIThread.Post(() => InsertConsoleLine(line));
        }
    }

    private ScrollViewer? _consoleScroller;

    private void InsertConsoleLine(ConsoleLine line)
    {
        bool isAtBottom = true;
        
        if (_consoleScroller == null)
        {
            _consoleScroller = ConsoleOutput.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }
        
        if (_consoleScroller != null)
        {
            isAtBottom = _consoleScroller.Offset.Y >= _consoleScroller.Extent.Height - _consoleScroller.Viewport.Height - 10;
        }

        ConsoleMessages.Add(line);
        
        if (ConsoleMessages.Count > 10000)
        {
            ConsoleMessages.RemoveAt(0);
        }
        
        if (isAtBottom)
        {
            ConsoleOutput.ScrollIntoView(line);
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

        if (config.ResolutionScale <= 1.0f) CmbGraphicsScale.SelectedIndex = 0;
        else if (config.ResolutionScale <= 2.0f) CmbGraphicsScale.SelectedIndex = 1;
        else CmbGraphicsScale.SelectedIndex = 2;
        
        CmbGraphicsScale.SelectionChanged += (s, e) =>
        {
            config.ResolutionScale = CmbGraphicsScale.SelectedIndex switch
            {
                0 => 1.0f,
                1 => 2.0f,
                2 => 3.0f,
                _ => 1.0f
            };
            config.Save();
        };

        ChkAudio.IsChecked = config.EnableAudio;
        ChkAudio.IsCheckedChanged += (s, e) => { config.EnableAudio = ChkAudio.IsChecked == true; config.Save(); };
        
        SldVolume.Value = config.MasterVolume;
        SldVolume.ValueChanged += (s, e) => { config.MasterVolume = (float)SldVolume.Value; config.Save(); };
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
        
        CmbGraphicsDevice.ItemsSource = new[] { gpuName };
        CmbGraphicsDevice.SelectedIndex = 0;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _runtime?.Dispose();
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
            var color = "Gray";
            if (value.Contains("[ERROR]")) color = "Red";
            else if (value.Contains("[WARNING]")) color = "Yellow";
            else if (value.Contains("[INFO]")) color = "White";

            var line = new ConsoleLine { Text = value, Color = color };
            
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                _onLine(line);
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _onLine(line));
        }
    }
}
