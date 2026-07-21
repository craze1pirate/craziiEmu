// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE.Host;
using CraziiEmu.HLE.Host.Posix;
using CraziiEmu.HLE.Configuration;
using Silk.NET.Input;
using System;
using System.Collections.Generic;

namespace CraziiEmu.Libs.Pad;

/// <summary>
/// Keyboard and gamepad state sampled from the presenter's window, feeding
/// the POSIX host input seam. Incorporates CraziiEmuConfig bindings.
/// </summary>
public static class HostWindowInput
{
    private static readonly object Gate = new();
    private static readonly HashSet<Key> Pressed = new();
    private static volatile bool _connected;

    // Mouse state
    private static bool _mouseLeftDown;
    private static bool _mouseRightDown;
    private static bool _mouseMiddleDown;
    private static float _mouseDeltaX;
    private static float _mouseDeltaY;
    private static System.Numerics.Vector2 _lastMousePos;

    // Latest window-gamepad snapshot in the host seam's conventions.
    private static bool _gamepadConnected;
    private static string? _gamepadName;
    private static HostGamepadButtons _gamepadButtons;
    private static byte _gamepadLeftX = 128;
    private static byte _gamepadLeftY = 128;
    private static byte _gamepadRightX = 128;
    private static byte _gamepadRightY = 128;
    private static byte _gamepadL2;
    private static byte _gamepadR2;

    public static bool IsConnected => _connected;

    public static void Attach(IInputContext input)
    {
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                lock (Gate) { Pressed.Add(key); }
            };
            keyboard.KeyUp += (_, key, _) =>
            {
                lock (Gate) { Pressed.Remove(key); }
            };
        }

        if (input.Keyboards.Count > 0)
        {
            _connected = true;
        }

        foreach (var gamepad in input.Gamepads)
        {
            AttachGamepad(gamepad);
        }

        foreach (var mouse in input.Mice)
        {
            AttachMouse(mouse);
        }

        input.ConnectionChanged += (device, connected) =>
        {
            if (device is IGamepad gamepad)
            {
                if (connected) AttachGamepad(gamepad);
                else
                {
                    lock (Gate)
                    {
                        _gamepadConnected = false;
                        _gamepadName = null;
                        _gamepadButtons = HostGamepadButtons.None;
                        _gamepadLeftX = 128; _gamepadLeftY = 128;
                        _gamepadRightX = 128; _gamepadRightY = 128;
                        _gamepadL2 = 0; _gamepadR2 = 0;
                    }
                }
            }
            else if (device is IMouse mouse)
            {
                if (connected) AttachMouse(mouse);
            }
        };

        PosixHostInput.SetSource(new WindowInputSource());
    }

    private static void AttachMouse(IMouse mouse)
    {
        mouse.MouseDown += (_, button) =>
        {
            lock (Gate)
            {
                if (button == MouseButton.Left) _mouseLeftDown = true;
                if (button == MouseButton.Right) _mouseRightDown = true;
                if (button == MouseButton.Middle) _mouseMiddleDown = true;
            }
        };
        mouse.MouseUp += (_, button) =>
        {
            lock (Gate)
            {
                if (button == MouseButton.Left) _mouseLeftDown = false;
                if (button == MouseButton.Right) _mouseRightDown = false;
                if (button == MouseButton.Middle) _mouseMiddleDown = false;
            }
        };
        mouse.MouseMove += (_, pos) =>
        {
            lock (Gate)
            {
                if (_lastMousePos != default)
                {
                    _mouseDeltaX += (pos.X - _lastMousePos.X);
                    _mouseDeltaY += (pos.Y - _lastMousePos.Y);
                }
                _lastMousePos = pos;
            }
        };
    }

    public static bool IsKeyDown(Key key)
    {
        lock (Gate)
        {
            return Pressed.Contains(key);
        }
    }

    private sealed class WindowInputSource : IPosixWindowInputSource
    {
        public bool HasKeyboardFocus => _connected;

        public bool IsKeyDown(int virtualKey)
        {
            return TryMapVirtualKey(virtualKey, out var key) && HostWindowInput.IsKeyDown(key);
        }

        public int GetGamepadStates(Span<HostGamepadState> destination)
        {
            lock (Gate)
            {
                if (!_gamepadConnected && !_connected)
                {
                    return 0;
                }

                if (destination.Length == 0) return 0;

                var config = CraziiEmuConfig.Instance.Input;
                HostGamepadButtons buttons = _gamepadButtons;

                bool IsMappedDown(int vk)
                {
                    if (vk == InputMap.MouseLeft) return _mouseLeftDown;
                    if (vk == InputMap.MouseRight) return _mouseRightDown;
                    if (vk == InputMap.MouseMiddle) return _mouseMiddleDown;
                    if (vk >= 0x110 && vk <= 0x113)
                    {
                        if (vk == InputMap.MouseXNeg) return _mouseDeltaX < -2f;
                        if (vk == InputMap.MouseXPos) return _mouseDeltaX > 2f;
                        if (vk == InputMap.MouseYNeg) return _mouseDeltaY < -2f;
                        if (vk == InputMap.MouseYPos) return _mouseDeltaY > 2f;
                    }
                    return TryMapVirtualKey(vk, out var k) && HostWindowInput.IsKeyDown(k);
                }

                if (IsMappedDown(config.Cross)) buttons |= HostGamepadButtons.Cross;
                if (IsMappedDown(config.Circle)) buttons |= HostGamepadButtons.Circle;
                if (IsMappedDown(config.Square)) buttons |= HostGamepadButtons.Square;
                if (IsMappedDown(config.Triangle)) buttons |= HostGamepadButtons.Triangle;
                if (IsMappedDown(config.DpadUp)) buttons |= HostGamepadButtons.Up;
                if (IsMappedDown(config.DpadDown)) buttons |= HostGamepadButtons.Down;
                if (IsMappedDown(config.DpadLeft)) buttons |= HostGamepadButtons.Left;
                if (IsMappedDown(config.DpadRight)) buttons |= HostGamepadButtons.Right;
                if (IsMappedDown(config.L1)) buttons |= HostGamepadButtons.L1;
                if (IsMappedDown(config.R1)) buttons |= HostGamepadButtons.R1;
                if (IsMappedDown(config.L2)) buttons |= HostGamepadButtons.L2;
                if (IsMappedDown(config.R2)) buttons |= HostGamepadButtons.R2;
                if (IsMappedDown(config.L3)) buttons |= HostGamepadButtons.L3;
                if (IsMappedDown(config.R3)) buttons |= HostGamepadButtons.R3;
                if (IsMappedDown(config.Options)) buttons |= HostGamepadButtons.Options;
                
                // Map Create to Touchpad click
                if (IsMappedDown(config.Create)) buttons |= HostGamepadButtons.TouchPad;

                byte lx = 128, ly = 128, rx = 128, ry = 128;
                if (_gamepadConnected)
                {
                    lx = _gamepadLeftX; ly = _gamepadLeftY; rx = _gamepadRightX; ry = _gamepadRightY;
                }

                if (IsMappedDown(config.LeftStickLeft)) lx = 0;
                if (IsMappedDown(config.LeftStickRight)) lx = 255;
                if (IsMappedDown(config.LeftStickUp)) ly = 0;
                if (IsMappedDown(config.LeftStickDown)) ly = 255;

                if (IsMappedDown(config.RightStickLeft)) rx = 0;
                if (IsMappedDown(config.RightStickRight)) rx = 255;
                if (IsMappedDown(config.RightStickUp)) ry = 0;
                if (IsMappedDown(config.RightStickDown)) ry = 255;

                _mouseDeltaX *= 0.5f;
                _mouseDeltaY *= 0.5f;

                destination[0] = new HostGamepadState(
                    Connected: true,
                    Buttons: buttons,
                    LeftX: lx,
                    LeftY: ly,
                    RightX: rx,
                    RightY: ry,
                    LeftTrigger: (buttons & HostGamepadButtons.L2) != 0 ? (byte)255 : _gamepadL2,
                    RightTrigger: (buttons & HostGamepadButtons.R2) != 0 ? (byte)255 : _gamepadR2);
                
                return 1;
            }
        }

        public string? DescribeConnectedGamepad()
        {
            lock (Gate)
            {
                if (_gamepadConnected) return _gamepadName ?? "GLFW gamepad";
                if (_connected) return "Keyboard / Mouse (Mapped)";
                return null;
            }
        }
    }

    private static bool TryMapVirtualKey(int vk, out Key key)
    {
        key = vk switch
        {
            0x08 => Key.Backspace,
            0x09 => Key.Tab,
            0x0D => Key.Enter,
            0x1B => Key.Escape,
            0x20 => Key.Space,
            0x24 => Key.Home,
            0x25 => Key.Left,
            0x26 => Key.Up,
            0x27 => Key.Right,
            0x28 => Key.Down,
            0xA0 => Key.ShiftLeft,
            0xA1 => Key.ShiftRight,
            0xA2 => Key.ControlLeft,
            0xA3 => Key.ControlRight,
            0xA4 => Key.AltLeft,
            0xA5 => Key.AltRight,
            >= 0x30 and <= 0x39 => Key.Number0 + (vk - 0x30),
            >= 0x41 and <= 0x5A => Key.A + (vk - 0x41),
            _ => Key.Unknown,
        };
        return key != Key.Unknown;
    }

    private static void AttachGamepad(IGamepad gamepad)
    {
        lock (Gate)
        {
            _gamepadConnected = true;
            _gamepadName = gamepad.Name;
        }

        gamepad.ButtonDown += (_, button) =>
        {
            var bit = MapButton(button.Name);
            if (bit == HostGamepadButtons.None) return;
            lock (Gate) { _gamepadButtons |= bit; }
        };
        gamepad.ButtonUp += (_, button) =>
        {
            var bit = MapButton(button.Name);
            if (bit == HostGamepadButtons.None) return;
            lock (Gate) { _gamepadButtons &= ~bit; }
        };
        gamepad.ThumbstickMoved += (_, thumbstick) =>
        {
            var x = ToStickByte(thumbstick.X);
            var y = ToStickByte(thumbstick.Y);
            lock (Gate)
            {
                if (thumbstick.Index == 0)
                {
                    _gamepadLeftX = x;
                    _gamepadLeftY = y;
                }
                else
                {
                    _gamepadRightX = x;
                    _gamepadRightY = y;
                }
            }
        };
        gamepad.TriggerMoved += (_, trigger) =>
        {
            var value = (byte)Math.Clamp((int)((trigger.Position + 1.0f) * 0.5f * 255.0f), 0, 255);
            lock (Gate)
            {
                if (trigger.Index == 0)
                {
                    _gamepadL2 = value;
                    if (value > 64) _gamepadButtons |= HostGamepadButtons.L2;
                    else _gamepadButtons &= ~HostGamepadButtons.L2;
                }
                else
                {
                    _gamepadR2 = value;
                    if (value > 64) _gamepadButtons |= HostGamepadButtons.R2;
                    else _gamepadButtons &= ~HostGamepadButtons.R2;
                }
            }
        };
    }

    internal static byte ToStickByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round((value + 1.0f) * 127.5f), 0, 255);
    }

    private static HostGamepadButtons MapButton(ButtonName name) => name switch
    {
        ButtonName.A => HostGamepadButtons.Cross,
        ButtonName.B => HostGamepadButtons.Circle,
        ButtonName.X => HostGamepadButtons.Square,
        ButtonName.Y => HostGamepadButtons.Triangle,
        ButtonName.LeftBumper => HostGamepadButtons.L1,
        ButtonName.RightBumper => HostGamepadButtons.R1,
        ButtonName.Back => HostGamepadButtons.TouchPad,
        ButtonName.Start => HostGamepadButtons.Options,
        ButtonName.LeftStick => HostGamepadButtons.L3,
        ButtonName.RightStick => HostGamepadButtons.R3,
        ButtonName.DPadUp => HostGamepadButtons.Up,
        ButtonName.DPadRight => HostGamepadButtons.Right,
        ButtonName.DPadDown => HostGamepadButtons.Down,
        ButtonName.DPadLeft => HostGamepadButtons.Left,
        _ => HostGamepadButtons.None,
    };
}
