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
        if (_bindingButton != null && _bindingProperty.HasValue)
        {
            var currentPos = e.GetPosition(this);
            if (_lastMousePos != default)
            {
                var dx = currentPos.X - _lastMousePos.X;
                var dy = currentPos.Y - _lastMousePos.Y;
                if (Math.Abs(dx) > 10) ApplyBinding(dx > 0 ? InputMap.MouseXPos : InputMap.MouseXNeg);
                else if (Math.Abs(dy) > 10) ApplyBinding(dy > 0 ? InputMap.MouseYPos : InputMap.MouseYNeg);
            }
            _lastMousePos = currentPos;
            e.Handled = true;
        }
        else
        {
            _lastMousePos = e.GetPosition(this);
        }
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
