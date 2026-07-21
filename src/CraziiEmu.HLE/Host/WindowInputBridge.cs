// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE.Host;

/// <summary>
/// Provides gamepad state from a window-level input source (keyboard/mouse
/// mapped to virtual gamepad buttons). Higher layers (e.g. the Libs presenter)
/// register a source via <see cref="SetSource"/>; lower layers (e.g.
/// <see cref="Windows.WindowsHostInput"/>) read it as a fallback when no
/// physical gamepad is connected.
/// </summary>
public static class WindowInputBridge
{
    /// <summary>
    /// Callback that fills <paramref name="destination"/> with mapped gamepad
    /// state and returns how many entries were written (0 or 1).
    /// </summary>
    public delegate int GetGamepadStatesDelegate(Span<HostGamepadState> destination);

    private static volatile GetGamepadStatesDelegate? _source;

    /// <summary>
    /// The currently registered window input source, or <c>null</c> if none
    /// has been registered yet.
    /// </summary>
    public static GetGamepadStatesDelegate? Source => _source;

    /// <summary>
    /// Registers a window-level input source. Called once during presenter
    /// initialization.
    /// </summary>
    public static void SetSource(GetGamepadStatesDelegate source)
    {
        _source = source;
    }
}
