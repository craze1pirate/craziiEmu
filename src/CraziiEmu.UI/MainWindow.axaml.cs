// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Linq;
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
    private string? _firmwareDirectoryPath;
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

        // ── Settings bindings ──────────────────────────────────────────
        _controllerConfig = new ControllerConfig();
        _controllerConfig.LoadFromBackend();

        SidebarList.SelectionChanged += OnSidebarSelectionChanged;
        BtnBrowseFirmware.Click      += OnBtnBrowseFirmware;
        BtnBrowseWallpaper.Click     += OnBtnBrowseWallpaper;
        BtnClearWallpaper.Click      += OnBtnClearWallpaper;

        ChkConsoleVisible.PropertyChanged += (s, e) => 
        {
            if (e.Property.Name == "IsChecked")
                ConsoleBorder.IsVisible = ChkConsoleVisible.IsChecked == true;
        };

        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

        InitializeBindings();

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
    //  Configuration Window & Overlay
    // ══════════════════════════════════════════════════════════════

    private void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        SettingsOverlay.IsVisible = true;
    }

    private void InitializeBindings()
    {
        var binds = _controllerConfig.Bindings;
        
        BindCross.Content = binds[PsControllerButton.Cross].ToString();
        BindCircle.Content = binds[PsControllerButton.Circle].ToString();
        BindSquare.Content = binds[PsControllerButton.Square].ToString();
        BindTriangle.Content = binds[PsControllerButton.Triangle].ToString();
        
        BindDpadUp.Content = binds[PsControllerButton.DpadUp].ToString();
        BindDpadDown.Content = binds[PsControllerButton.DpadDown].ToString();
        BindDpadLeft.Content = binds[PsControllerButton.DpadLeft].ToString();
        BindDpadRight.Content = binds[PsControllerButton.DpadRight].ToString();

        BindL1.Content = binds[PsControllerButton.L1].ToString();
        BindR1.Content = binds[PsControllerButton.R1].ToString();
        BindL2.Content = binds[PsControllerButton.L2].ToString();
        BindR2.Content = binds[PsControllerButton.R2].ToString();

        BindLeftStickUp.Content = binds[PsControllerButton.LeftStickUp].ToString();
        BindLeftStickDown.Content = binds[PsControllerButton.LeftStickDown].ToString();
        BindLeftStickLeft.Content = binds[PsControllerButton.LeftStickLeft].ToString();
        BindLeftStickRight.Content = binds[PsControllerButton.LeftStickRight].ToString();

        BindRightStickUp.Content = binds[PsControllerButton.RightStickUp].ToString();
        BindRightStickDown.Content = binds[PsControllerButton.RightStickDown].ToString();
        BindRightStickLeft.Content = binds[PsControllerButton.RightStickLeft].ToString();
        BindRightStickRight.Content = binds[PsControllerButton.RightStickRight].ToString();

        BindL3.Content = binds[PsControllerButton.L3].ToString();
        BindR3.Content = binds[PsControllerButton.R3].ToString();
        BindOptions.Content = binds[PsControllerButton.Options].ToString();
        BindCreate.Content = binds[PsControllerButton.Create].ToString();
        BindPsButton.Content = binds[PsControllerButton.PsButton].ToString();

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
        PanelGeneral.IsVisible  = tag == "General";
        PanelSystem.IsVisible   = tag == "System";
        PanelCPU.IsVisible      = tag == "CPU";
        PanelGraphics.IsVisible = tag == "Graphics";
        PanelAudio.IsVisible    = tag == "Audio";
        PanelControls.IsVisible = tag == "Controls";
        PanelVisual.IsVisible   = tag == "Visual";
    }

    private async void OnBtnBrowseFirmware(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Decrypted Firmware Directory", AllowMultiple = false });
        if (folders.Count > 0) 
        { 
            var path = folders[0].Path.LocalPath; 
            TxtFirmwarePath.Text = path; 
            _firmwareDirectoryPath = path;
            AppendConsole($"[System] Firmware directory: {path}");
        }
    }

    private async void OnBtnBrowseWallpaper(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Custom Wallpaper", AllowMultiple = false });
        if (files.Count > 0) 
        { 
            var path = files[0].Path.LocalPath; 
            TxtWallpaperPath.Text = path; 
            SetWallpaper(path);
        }
    }

    private void OnBtnClearWallpaper(object? sender, RoutedEventArgs e)
    {
        TxtWallpaperPath.Text = string.Empty;
        SetWallpaper(string.Empty);
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bindingButton != null && _bindingProperty.HasValue)
        {
            var newKey = e.Key;
            var targetBtn = _bindingProperty.Value;
            var map = _controllerConfig.Bindings;

            // Duplicate Swapping Algorithm
            if (map.ContainsValue(newKey))
            {
                var conflictKey = map.First(x => x.Value == newKey).Key;
                if (conflictKey != targetBtn)
                {
                    // Conflict detected: Swap!
                    var oldKey = map[targetBtn];
                    map[targetBtn] = newKey;
                    map[conflictKey] = oldKey;
                }
            }
            else
            {
                // No conflict
                map[targetBtn] = newKey;
            }

            _controllerConfig.SaveToBackend();

            _bindingButton = null;
            _bindingProperty = null;
            InitializeBindings();
            e.Handled = true;
        }
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

            string? picPath = null;
            if (!selected.IsAddCard && !string.IsNullOrEmpty(selected.ExecutablePath))
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
                try { WallpaperImage.Source = new Bitmap(picPath); }
                catch { RestoreCustomWallpaper(); }
            }
            else
            {
                RestoreCustomWallpaper();
            }
        }
    }

    private void RestoreCustomWallpaper()
    {
        var customPath = TxtWallpaperPath?.Text;
        if (!string.IsNullOrEmpty(customPath) && System.IO.File.Exists(customPath))
        {
            try { WallpaperImage.Source = new Bitmap(customPath); }
            catch { WallpaperImage.Source = null; }
        }
        else
        {
            if (WallpaperImage != null) WallpaperImage.Source = null;
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
                IsAddCard = false, 
                Title = !string.IsNullOrEmpty(version) ? $"{title} [{titleId}] v{version}" : title, 
                ExecutablePath = path,
                CoverArt = coverArt
            };
            
            Games.Add(newGame);
            AppendConsole($"[Library] Added: {path}");
            
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
