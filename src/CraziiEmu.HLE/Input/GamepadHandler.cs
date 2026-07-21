// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.HLE.Input;

/// <summary>
/// Simple XInput wrapper for native Windows gamepad support without heavy dependencies.
/// Ideal for reading full gamepad state (buttons, analog sticks, triggers).
/// </summary>
public static class GamepadHandler
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState91(uint dwUserIndex, out XINPUT_STATE pState);

    /// <summary>
    /// Gamepad button bitmask constants (XInput).
    /// </summary>
    public const ushort DPAD_UP = 0x0001;
    public const ushort DPAD_DOWN = 0x0002;
    public const ushort DPAD_LEFT = 0x0004;
    public const ushort DPAD_RIGHT = 0x0008;
    public const ushort START = 0x0010;
    public const ushort BACK = 0x0020;
    public const ushort LEFT_THUMB = 0x0040;
    public const ushort RIGHT_THUMB = 0x0080;
    public const ushort LEFT_SHOULDER = 0x0100;
    public const ushort RIGHT_SHOULDER = 0x0200;
    public const ushort BTN_A = 0x1000;
    public const ushort BTN_B = 0x2000;
    public const ushort BTN_X = 0x4000;
    public const ushort BTN_Y = 0x8000;

    /// <summary>
    /// Gets the current full gamepad state.
    /// Returns true if a gamepad is connected, false otherwise.
    /// </summary>
    public static bool GetState(out XINPUT_GAMEPAD pad, uint userIndex = 0)
    {
        uint result = 1;
        XINPUT_STATE state = default;
        
        try 
        { 
            result = XInputGetState14(userIndex, out state); 
        }
        catch 
        { 
            try 
            { 
                result = XInputGetState91(userIndex, out state); 
            } 
            catch { } 
        }

        if (result == 0)
        {
            pad = state.Gamepad;
            return true;
        }
        
        pad = default;
        return false;
    }

    /// <summary>
    /// Gets the current button state for the specified gamepad index.
    /// Returns 0 if no gamepad is connected.
    /// </summary>
    public static ushort GetButtons(uint userIndex = 0)
    {
        if (GetState(out var pad, userIndex))
        {
            return pad.wButtons;
        }
        return 0;
    }
}
