// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using System.Text.Json;

namespace CraziiEmu.HLE.Configuration;

/// <summary>
/// Global configuration manager for CraziiEmu.
/// </summary>
public class CraziiEmuConfig
{
    private static CraziiEmuConfig? _instance;
    private static readonly string ConfigFilePath = "config.json";

    public static CraziiEmuConfig Instance
    {
        get
        {
            if (_instance == null)
            {
                Load();
            }
            return _instance!;
        }
    }

    public InputMap Input { get; set; } = new InputMap();

    public bool EnableAudio { get; set; } = true;
    public float MasterVolume { get; set; } = 100f;

    public string GraphicsApi { get; set; } = "Vulkan";
    public float ResolutionScale { get; set; } = 1.0f;
    public bool UiFullscreenOnStartup { get; set; } = false;

    public int MetricsOverlayMode { get; set; } = 0; // 0 = None, 1 = Minimal Mode, 2 = Standard Mode, 3 = Developer Mode
    public int HotkeyMetricsOverlay { get; set; } = 0x72; // VK 0x72 = F3
    public int HotkeyVerboseConsole { get; set; } = 0x73; // VK 0x73 = F4

    public int GetResolutionScaleIndex()
    {
        if (ResolutionScale < 0.75f) return 0;       // 0.66x (720p)
        if (ResolutionScale < 0.90f) return 1;       // 0.83x (900p)
        if (ResolutionScale < 1.15f) return 2;       // 1.0x (1080p) [Native]
        if (ResolutionScale < 1.65f) return 3;       // 1.33x (1440p / 2K)
        return 4;                                    // 2.0x (4K)
    }

    public void SetResolutionScaleFromIndex(int index)
    {
        ResolutionScale = index switch
        {
            0 => 0.666667f,
            1 => 0.833333f,
            2 => 1.0f,
            3 => 1.333333f,
            4 => 2.0f,
            _ => 1.0f,
        };
    }
    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                _instance = JsonSerializer.Deserialize<CraziiEmuConfig>(json) ?? new CraziiEmuConfig();
            }
            else
            {
                _instance = new CraziiEmuConfig();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CONFIG] Failed to load config.json: {ex.Message}");
            _instance = new CraziiEmuConfig();
        }
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CONFIG] Failed to save config.json: {ex.Message}");
        }
    }
}

public class InputMap
{
    public const int MouseLeft = 0x100;
    public const int MouseRight = 0x101;
    public const int MouseMiddle = 0x102;
    public const int MouseXNeg = 0x110;
    public const int MouseXPos = 0x111;
    public const int MouseYNeg = 0x112;
    public const int MouseYPos = 0x113;

    // D-Pad
    public int DpadUp { get; set; } = 0x26;    // Up Arrow
    public int DpadDown { get; set; } = 0x28;  // Down Arrow
    public int DpadLeft { get; set; } = 0x25;  // Left Arrow
    public int DpadRight { get; set; } = 0x27; // Right Arrow

    // Face Buttons
    public int Cross { get; set; } = 0x20;     // Space
    public int Circle { get; set; } = 0xA0;    // LShift
    public int Square { get; set; } = 0x46;    // F
    public int Triangle { get; set; } = 0x45;  // E

    // Bumpers / Triggers
    public int L1 { get; set; } = 0x51;        // Q
    public int R1 { get; set; } = 0x52;        // R
    public int L2 { get; set; } = 0x09;        // Tab
    public int R2 { get; set; } = MouseLeft;   // Left Mouse

    // System
    public int Options { get; set; } = 0x1B;   // Escape
    public int Create { get; set; } = 0x08;    // Backspace
    public int PsButton { get; set; } = 0x24;  // Home

    // Stick Clicks
    public int L3 { get; set; } = 0xA2;        // LCtrl
    public int R3 { get; set; } = 0x56;        // V

    // Analog Sticks
    public int LeftStickLeft { get; set; } = 0x41;  // A
    public int LeftStickRight { get; set; } = 0x44; // D
    public int LeftStickUp { get; set; } = 0x57;    // W
    public int LeftStickDown { get; set; } = 0x53;  // S

    public int RightStickLeft { get; set; } = MouseXNeg;  // Mouse X (-)
    public int RightStickRight { get; set; } = MouseXPos; // Mouse X (+)
    public int RightStickUp { get; set; } = MouseYNeg;    // Mouse Y (-)
    public int RightStickDown { get; set; } = MouseYPos;  // Mouse Y (+)
}

