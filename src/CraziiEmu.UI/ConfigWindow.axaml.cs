// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Linq;
using System.Management;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CraziiEmu.Core.HLE;
using CraziiEmu.HLE.Configuration;
using CraziiEmu.UI.Input;

namespace CraziiEmu.UI;

public partial class ConfigWindow : Window
{
    private Button? _bindingButton;
    private PsControllerButton? _bindingProperty;
    private readonly ControllerConfig _controllerConfig;

    private Button? _bindingHotkeyButton;
    private string? _bindingHotkeyName;

    public Action<bool>? OnConsoleVisibilityChanged;

    public ConfigWindow()
    {
        InitializeComponent();
        
        _controllerConfig = new ControllerConfig();
        _controllerConfig.LoadFromBackend();

        SidebarList.SelectionChanged += OnSidebarSelectionChanged;


        ChkConsoleVisible.PropertyChanged += (s, e) => 
        {
            if (e.Property.Name == "IsChecked")
                OnConsoleVisibilityChanged?.Invoke(ChkConsoleVisible.IsChecked == true);
        };

        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnGlobalPointerMoved, RoutingStrategies.Tunnel);

        InitializeBindings();
        InitializeConfigBindings();
    }

    private void InitializeConfigBindings()
    {
        var config = CraziiEmuConfig.Instance;
        

        string gpuName = "Default System GPU";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("select * from Win32_VideoController");
                string? bestGpu = null;
                int bestScore = -1;

                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    int score = 10;
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx") || lower.Contains("quadro"))
                    {
                        score = 100;
                    }
                    else if (lower.Contains("radeon rx") || lower.Contains("radeon pro") || (lower.Contains("amd") && !lower.Contains("graphics") && !lower.Contains("apu")))
                    {
                        score = 90;
                    }
                    else if (lower.Contains("arc") && lower.Contains("intel"))
                    {
                        score = 80;
                    }
                    else if (lower.Contains("radeon"))
                    {
                        score = 50;
                    }
                    else if (lower.Contains("intel"))
                    {
                        score = 30;
                    }
                    else if (lower.Contains("basic display") || lower.Contains("virtual") || lower.Contains("vmware"))
                    {
                        score = 5;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGpu = name;
                    }
                }

                if (!string.IsNullOrWhiteSpace(bestGpu))
                {
                    gpuName = bestGpu;
                }
            }
            catch { }
        }
        
        TxtGraphicsDevice.Text = gpuName;

        CmbGraphicsApi.SelectedIndex = config.GraphicsApi == "OpenGL" ? 1 : 0;
        CmbGraphicsApi.SelectionChanged += (s, e) => 
        {
            config.GraphicsApi = CmbGraphicsApi.SelectedIndex == 1 ? "OpenGL" : "Vulkan";
            config.Save();
        };

        CmbGraphicsScale.SelectedIndex = config.GetResolutionScaleIndex();
        CmbGraphicsScale.SelectionChanged += (s, e) =>
        {
            if (CmbGraphicsScale.SelectedIndex >= 0)
            {
                config.SetResolutionScaleFromIndex(CmbGraphicsScale.SelectedIndex);
                config.Save();
            }
        };

        CmbDisplayScaling.SelectedIndex = config.GetDisplayScalingIndex();
        CmbDisplayScaling.SelectionChanged += (s, e) =>
        {
            if (CmbDisplayScaling.SelectedIndex >= 0)
            {
                config.SetDisplayScalingFromIndex(CmbDisplayScaling.SelectedIndex);
                config.Save();
            }
        };

        ChkAudio.IsChecked = config.EnableAudio;
        SldVolume.IsEnabled = config.EnableAudio;
        ChkAudio.IsCheckedChanged += (s, e) => 
        { 
            config.EnableAudio = ChkAudio.IsChecked == true; 
            SldVolume.IsEnabled = config.EnableAudio;
            config.Save(); 
        };
        
        SldVolume.Value = config.MasterVolume;
        SldVolume.ValueChanged += (s, e) => { config.MasterVolume = (float)SldVolume.Value; config.Save(); };

        CmbMetricsOverlay.SelectedIndex = Math.Clamp(config.MetricsOverlayMode, 0, 3);
        CmbMetricsOverlay.SelectionChanged += (s, e) =>
        {
            config.MetricsOverlayMode = CmbMetricsOverlay.SelectedIndex;
            config.Save();
            CraziiEmu.Libs.VideoOut.Overlay.OverlayRenderer.Mode = (CraziiEmu.Libs.VideoOut.Overlay.OverlayMode)config.MetricsOverlayMode;
        };

        CmbRenderDocCapture.SelectedIndex = config.EnableRenderDocCapture ? 1 : 0;
        CmbRenderDocCapture.SelectionChanged += (s, e) =>
        {
            config.EnableRenderDocCapture = CmbRenderDocCapture.SelectedIndex == 1;
            config.Save();
        };

        InitializeHotkeyBindings();
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
        btn.Click -= OnBindBtnClicked; // Remove old handlers to prevent memory leaks if called twice
        btn.Tag = propName;
        btn.Click += OnBindBtnClicked;
    }

    private void OnBindBtnClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_bindingButton != null || _bindingHotkeyButton != null)
        {
            InitializeBindings(); // Reset previous if clicked another
            InitializeHotkeyBindings();
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
}
