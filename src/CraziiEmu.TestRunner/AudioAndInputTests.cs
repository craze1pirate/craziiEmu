// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using CraziiEmu.HLE.Configuration;

namespace CraziiEmu.TestRunner;

public static class AudioAndInputTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting AudioAndInputTests...");
        var config = new CraziiEmuConfig();
        config.EnableAudio = true;
        config.MasterVolume = 80f;
        Console.WriteLine($"AudioEnabled=true, Vol=80 => Gain={config.GetMasterGain()}");
        if (Math.Abs(config.GetMasterGain() - 0.8f) > 0.001f)
        {
            throw new InvalidOperationException("Gain mismatch for 80%");
        }

        config.EnableAudio = false;
        Console.WriteLine($"AudioEnabled=false, Vol=80 => Gain={config.GetMasterGain()}");
        if (config.GetMasterGain() != 0f)
        {
            throw new InvalidOperationException("Gain should be 0 when audio is disabled");
        }

        var input = config.Input;
        Console.WriteLine($"Default controls: {input.DescribeControls()}");
        if (!input.DescribeControls().Contains("Space = Cross")) throw new InvalidOperationException("Default mapping missing Space = Cross");
        if (!input.DescribeControls().Contains("Left Shift = Circle")) throw new InvalidOperationException("Default mapping missing Left Shift = Circle");
        if (!input.DescribeControls().Contains("F = Square")) throw new InvalidOperationException("Default mapping missing F = Square");
        if (!input.DescribeControls().Contains("WASD = left stick")) throw new InvalidOperationException("Default mapping missing WASD = left stick");

        // Customize mapping
        input.Square = 0xA0; // Rebind Square to Left Shift
        input.Cross = 0x0D;  // Rebind Cross to Enter
        Console.WriteLine($"  [INFO] Custom controls: {input.DescribeControls()}");
        if (!input.DescribeControls().Contains("Left Shift = Square")) throw new InvalidOperationException("Custom mapping missing Left Shift = Square");
        if (!input.DescribeControls().Contains("Enter = Cross")) throw new InvalidOperationException("Custom mapping missing Enter = Cross");

        Console.WriteLine("[TEST] AudioAndInputTests PASSED cleanly.");
    }
}
