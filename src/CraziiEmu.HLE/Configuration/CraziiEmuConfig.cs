// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
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
    // D-Pad
    public int DpadUp { get; set; } = 0x31;    // 1
    public int DpadDown { get; set; } = 0x32;  // 2
    public int DpadLeft { get; set; } = 0x33;  // 3
    public int DpadRight { get; set; } = 0x34; // 4

    // Face Buttons
    public int Cross { get; set; } = 0x20;     // Space
    public int Circle { get; set; } = 0xA0;    // LShift
    public int Square { get; set; } = 0x46;    // F
    public int Triangle { get; set; } = 0x45;  // E

    // Bumpers / Triggers
    public int L1 { get; set; } = 0x51;        // Q
    public int R1 { get; set; } = 0x52;        // R
    public int L2 { get; set; } = 0x09;        // Tab
    public int R2 { get; set; } = 0x0D;        // Enter

    // System
    public int Options { get; set; } = 0x1B;   // Escape
    public int Create { get; set; } = 0x08;    // Backspace
    public int PsButton { get; set; } = 0x24;  // Home

    // Stick Clicks
    public int L3 { get; set; } = 0xA2;        // LCtrl
    public int R3 { get; set; } = 0xA3;        // RCtrl

    // Analog Sticks
    public int LeftStickLeft { get; set; } = 0x41;  // A
    public int LeftStickRight { get; set; } = 0x44; // D
    public int LeftStickUp { get; set; } = 0x57;    // W
    public int LeftStickDown { get; set; } = 0x53;  // S

    public int RightStickLeft { get; set; } = 0x25;  // Left Arrow
    public int RightStickRight { get; set; } = 0x27; // Right Arrow
    public int RightStickUp { get; set; } = 0x26;    // Up Arrow
    public int RightStickDown { get; set; } = 0x28;  // Down Arrow
}

