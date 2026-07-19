// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
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

    public Action<string>? OnFirmwareDirectoryChanged;
    public Action<bool>? OnConsoleVisibilityChanged;

    public ConfigWindow()
    {
        InitializeComponent();
        
        _controllerConfig = new ControllerConfig();
        _controllerConfig.LoadFromBackend();

        SidebarList.SelectionChanged += OnSidebarSelectionChanged;
        BtnBrowseFirmware.Click      += OnBtnBrowseFirmware;

        ChkConsoleVisible.PropertyChanged += (s, e) => 
        {
            if (e.Property.Name == "IsChecked")
                OnConsoleVisibilityChanged?.Invoke(ChkConsoleVisible.IsChecked == true);
        };

        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);

        InitializeBindings();
        InitializeConfigBindings();
    }

    private void InitializeConfigBindings()
    {
        var config = CraziiEmuConfig.Instance;
        
        ChkLimitSpeed.IsChecked = config.LimitSpeed;
        ChkLimitSpeed.IsCheckedChanged += (s, e) => { config.LimitSpeed = ChkLimitSpeed.IsChecked == true; config.Save(); };

        ChkMulticore.IsChecked = config.EnableMulticore;
        ChkMulticore.IsCheckedChanged += (s, e) => { config.EnableMulticore = ChkMulticore.IsChecked == true; config.Save(); };

        TxtFirmwarePath.Text = config.DecryptedFirmwarePath;

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
        PanelGeneral.IsVisible  = tag == "General";
        PanelSystem.IsVisible   = tag == "System";
        PanelCPU.IsVisible      = tag == "CPU";
        PanelGraphics.IsVisible = tag == "Graphics";
        PanelAudio.IsVisible    = tag == "Audio";
        PanelControls.IsVisible = tag == "Controls";
        PanelVisual.IsVisible   = tag == "Debug";
    }

    private async void OnBtnBrowseFirmware(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Decrypted Firmware Directory", AllowMultiple = false });
        if (folders.Count > 0) 
        { 
            var path = folders[0].Path.LocalPath; 
            TxtFirmwarePath.Text = path; 
            CraziiEmuConfig.Instance.DecryptedFirmwarePath = path;
            CraziiEmuConfig.Instance.Save();
            OnFirmwareDirectoryChanged?.Invoke(path); 
        }
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
}
