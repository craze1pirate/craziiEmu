// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.HLE.Host.Windows;

/// <summary>
/// Windows input backend: DualSense over raw HID plus XInput controllers for gamepads,
/// user32 for the keyboard-fallback queries. Rumble fans out to every reader; lightbar
/// only exists on the DualSense.
/// </summary>
internal sealed partial class WindowsHostInput : IHostInput
{
    public void EnsureStarted()
    {
        WindowsDualSenseReader.EnsureStarted();
        WindowsXInputReader.EnsureStarted();
    }

    public int GetGamepadStates(Span<HostGamepadState> destination)
    {
        var count = 0;
        if (count < destination.Length && WindowsDualSenseReader.TryGetState(out var dualSense))
        {
            destination[count++] = dualSense;
        }

        if (count < destination.Length && WindowsXInputReader.TryGetState(out var xinput))
        {
            destination[count++] = xinput;
        }

        if (count < destination.Length)
        {
            var windowSource = HostWindowInputSource.Current;
            if (windowSource != null)
            {
                var added = windowSource.GetGamepadStates(destination[count..]);
                count += added;
            }
            else
            {
                Span<HostGamepadState> windowState = stackalloc HostGamepadState[1];
                if (WindowInputBridge.Source?.Invoke(windowState) > 0)
                {
                    destination[count++] = windowState[0];
                }
            }
        }

        return count;
    }

    public string? DescribeConnectedGamepad()
    {
        if (WindowsDualSenseReader.TryGetState(out _))
        {
            return "DualSense";
        }

        if (WindowsXInputReader.TryGetState(out _))
        {
            return "Xbox controller";
        }

        return HostWindowInputSource.Current?.DescribeConnectedGamepad();
    }

    public void SetRumble(byte largeMotor, byte smallMotor)
    {
        WindowsDualSenseReader.SetRumble(largeMotor, smallMotor);
        WindowsXInputReader.SetRumble(largeMotor, smallMotor);
        HostWindowInputSource.Current?.SetRumble(largeMotor, smallMotor);
    }

    public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger)
    {
        WindowsXInputReader.SetTriggerRumble(leftTrigger, rightTrigger);
        HostWindowInputSource.Current?.SetTriggerRumble(leftTrigger, rightTrigger);
    }

    public void SetAdaptiveTriggerEffect(
        HostAdaptiveTriggerEffect? leftTrigger,
        HostAdaptiveTriggerEffect? rightTrigger)
    {
        HostWindowInputSource.Current?.SetAdaptiveTriggerEffect(leftTrigger, rightTrigger);
    }

    public void SetLightbar(byte red, byte green, byte blue)
    {
        WindowsDualSenseReader.SetLightbar(red, green, blue);
        HostWindowInputSource.Current?.SetLightbar(red, green, blue);
    }

    public void ResetLightbar()
    {
        WindowsDualSenseReader.ResetLightbar();
        HostWindowInputSource.Current?.ResetLightbar();
    }

    public bool IsHostWindowFocused()
    {
        if (HostWindowInputSource.Current?.HasKeyboardFocus ?? false)
        {
            return true;
        }

        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == (uint)Environment.ProcessId)
        {
            return true;
        }

        // The GUI runs the emulator in an isolated child process. Its native
        // Vulkan surface is a child of the GUI window, so the foreground
        // window belongs to the launcher process rather than this one.
        var embeddedHostWindow = HostSessionControl.EmbeddedHostWindow;
        var hostTopLevelWindow = embeddedHostWindow == 0
            ? 0
            : GetAncestor(embeddedHostWindow, GetAncestorRoot);
        return hostTopLevelWindow != 0 && foregroundWindow == hostTopLevelWindow;
    }

    public bool IsKeyDown(int virtualKey)
    {
        if (HostWindowInputSource.Current?.IsKeyDown(virtualKey) ?? false)
        {
            return true;
        }

        if (virtualKey == 0x100) // InputMap.MouseLeft
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0;
        }
        if (virtualKey == 0x101) // InputMap.MouseRight
        {
            return (GetAsyncKeyState(0x02) & 0x8000) != 0;
        }
        if (virtualKey == 0x102) // InputMap.MouseMiddle
        {
            return (GetAsyncKeyState(0x04) & 0x8000) != 0;
        }
        if (virtualKey == 0xA0) // VK_LSHIFT
        {
            return ((GetAsyncKeyState(0xA0) | GetAsyncKeyState(0x10)) & 0x8000) != 0;
        }
        if (virtualKey == 0xA2) // VK_LCONTROL
        {
            return ((GetAsyncKeyState(0xA2) | GetAsyncKeyState(0x11)) & 0x8000) != 0;
        }

        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint hWnd, uint gaFlags);

    private const uint GetAncestorRoot = 2;
}
