// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Input;
using CraziiEmu.HLE.Configuration;

namespace CraziiEmu.UI.Input;

public class ControllerConfig
{
    public Dictionary<PsControllerButton, Key> Bindings { get; private set; }

    public ControllerConfig()
    {
        Bindings = new Dictionary<PsControllerButton, Key>
        {
            // Left Stick
            { PsControllerButton.LeftStickUp, Key.W },
            { PsControllerButton.LeftStickDown, Key.S },
            { PsControllerButton.LeftStickLeft, Key.A },
            { PsControllerButton.LeftStickRight, Key.D },
            { PsControllerButton.L3, Key.LeftCtrl },
            
            // Right Stick
            { PsControllerButton.RightStickUp, Key.Up },
            { PsControllerButton.RightStickDown, Key.Down },
            { PsControllerButton.RightStickLeft, Key.Left },
            { PsControllerButton.RightStickRight, Key.Right },
            { PsControllerButton.R3, Key.RightCtrl },
            
            // Face Buttons
            { PsControllerButton.Cross, Key.Space },
            { PsControllerButton.Circle, Key.LeftShift },
            { PsControllerButton.Square, Key.F },
            { PsControllerButton.Triangle, Key.E },
            
            // Shoulder Buttons
            { PsControllerButton.L1, Key.Q },
            { PsControllerButton.R1, Key.R },
            { PsControllerButton.L2, Key.Tab },
            { PsControllerButton.R2, Key.Enter },
            
            // D-Pad
            { PsControllerButton.DpadUp, Key.D1 },
            { PsControllerButton.DpadDown, Key.D2 },
            { PsControllerButton.DpadLeft, Key.D3 },
            { PsControllerButton.DpadRight, Key.D4 },
            
            // System
            { PsControllerButton.Options, Key.Escape },
            { PsControllerButton.Create, Key.Back },
            { PsControllerButton.PsButton, Key.Home }
        };
    }

    /// <summary>
    /// Updates the backend CraziiEmuConfig with the current bindings.
    /// </summary>
    public void SaveToBackend()
    {
        var inputMap = CraziiEmuConfig.Instance.Input;
        var type = typeof(InputMap);

        foreach (var kvp in Bindings)
        {
            var propInfo = type.GetProperty(kvp.Key.ToString(), BindingFlags.Public | BindingFlags.Instance);
            if (propInfo != null && propInfo.CanWrite)
            {
                int vk = KeyToVirtualKey(kvp.Value);
                if (vk != 0)
                {
                    propInfo.SetValue(inputMap, vk);
                }
            }
        }
        
        CraziiEmuConfig.Instance.Save();
    }

    /// <summary>
    /// Helper to convert Avalonia Key to Windows Virtual Key
    /// </summary>
    public static int KeyToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 0x41 + (key - Key.A);
        if (key >= Key.D0 && key <= Key.D9) return 0x30 + (key - Key.D0);
        return key switch
        {
            Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28, Key.Home => 0x24,
            Key.Enter => 0x0D, Key.Escape => 0x1B, Key.Space => 0x20, Key.Tab => 0x09,
            Key.Back => 0x08, Key.LeftShift => 0xA0, Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2, Key.RightCtrl => 0xA3, Key.LeftAlt => 0xA4, Key.RightAlt => 0xA5,
            _ => 0
        };
    }

    public void LoadFromBackend()
    {
        var inputMap = CraziiEmuConfig.Instance.Input;
        var type = typeof(InputMap);

        foreach (var key in Bindings.Keys)
        {
            var propInfo = type.GetProperty(key.ToString(), BindingFlags.Public | BindingFlags.Instance);
            if (propInfo != null && propInfo.CanRead)
            {
                if (propInfo.GetValue(inputMap) is int vk && vk != 0)
                {
                    Bindings[key] = VirtualKeyToKey(vk);
                }
            }
        }
    }

    public static Key VirtualKeyToKey(int vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return (Key)(Key.A + (vk - 0x41));
        if (vk >= 0x30 && vk <= 0x39) return (Key)(Key.D0 + (vk - 0x30));
        return vk switch
        {
            0x25 => Key.Left, 0x26 => Key.Up, 0x27 => Key.Right, 0x28 => Key.Down, 0x24 => Key.Home,
            0x0D => Key.Enter, 0x1B => Key.Escape, 0x20 => Key.Space, 0x09 => Key.Tab,
            0x08 => Key.Back, 0xA0 => Key.LeftShift, 0xA1 => Key.RightShift,
            0xA2 => Key.LeftCtrl, 0xA3 => Key.RightCtrl, 0xA4 => Key.LeftAlt, 0xA5 => Key.RightAlt,
            _ => Key.None
        };
    }
}
