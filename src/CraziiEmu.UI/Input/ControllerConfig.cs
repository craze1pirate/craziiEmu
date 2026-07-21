// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Input;
using CraziiEmu.HLE.Configuration;

namespace CraziiEmu.UI.Input;

public class ControllerConfig
{
    public Dictionary<PsControllerButton, int> Bindings { get; private set; }

    public ControllerConfig()
    {
        Bindings = new Dictionary<PsControllerButton, int>();
    }

    public void SetGlobalDefaults()
    {
        Bindings[PsControllerButton.Cross] = 0x20; // Space
        Bindings[PsControllerButton.Circle] = 0xA0; // LShift
        Bindings[PsControllerButton.Square] = 0x46; // F
        Bindings[PsControllerButton.Triangle] = 0x45; // E

        Bindings[PsControllerButton.L1] = 0x51; // Q
        Bindings[PsControllerButton.R1] = 0x52; // R
        Bindings[PsControllerButton.L2] = 0x09; // Tab
        Bindings[PsControllerButton.R2] = InputMap.MouseLeft; // LMB

        Bindings[PsControllerButton.DpadUp] = 0x26; // Up Arrow
        Bindings[PsControllerButton.DpadDown] = 0x28; // Down Arrow
        Bindings[PsControllerButton.DpadLeft] = 0x25; // Left Arrow
        Bindings[PsControllerButton.DpadRight] = 0x27; // Right Arrow

        Bindings[PsControllerButton.LeftStickUp] = 0x57; // W
        Bindings[PsControllerButton.LeftStickDown] = 0x53; // S
        Bindings[PsControllerButton.LeftStickLeft] = 0x41; // A
        Bindings[PsControllerButton.LeftStickRight] = 0x44; // D

        Bindings[PsControllerButton.RightStickUp] = InputMap.MouseYNeg;
        Bindings[PsControllerButton.RightStickDown] = InputMap.MouseYPos;
        Bindings[PsControllerButton.RightStickLeft] = InputMap.MouseXNeg;
        Bindings[PsControllerButton.RightStickRight] = InputMap.MouseXPos;

        Bindings[PsControllerButton.Options] = 0x1B; // Esc
        Bindings[PsControllerButton.Create] = 0x08; // Backspace
        Bindings[PsControllerButton.PsButton] = 0x24; // Home

        if (!Bindings.ContainsKey(PsControllerButton.L3)) Bindings[PsControllerButton.L3] = 0x43; // C
        if (!Bindings.ContainsKey(PsControllerButton.R3)) Bindings[PsControllerButton.R3] = 0x56; // V
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
                propInfo.SetValue(inputMap, kvp.Value);
            }
        }
        
        CraziiEmuConfig.Instance.Save();
    }

    public void LoadFromBackend()
    {
        var inputMap = CraziiEmuConfig.Instance.Input;
        var type = typeof(InputMap);

        foreach (PsControllerButton button in Enum.GetValues(typeof(PsControllerButton)))
        {
            var propInfo = type.GetProperty(button.ToString(), BindingFlags.Public | BindingFlags.Instance);
            if (propInfo != null && propInfo.CanRead)
            {
                if (propInfo.GetValue(inputMap) is int vk)
                {
                    Bindings[button] = vk;
                }
            }
        }
    }

    public static string GetBindingName(int vk)
    {
        return vk switch
        {
            InputMap.MouseLeft => "Mouse Left",
            InputMap.MouseRight => "Mouse Right",
            InputMap.MouseMiddle => "Mouse Middle",
            InputMap.MouseXNeg => "Mouse X (-)",
            InputMap.MouseXPos => "Mouse X (+)",
            InputMap.MouseYNeg => "Mouse Y (-)",
            InputMap.MouseYPos => "Mouse Y (+)",
            _ => VirtualKeyToKey(vk).ToString()
        };
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
