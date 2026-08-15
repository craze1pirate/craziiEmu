// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;
using CraziiEmu.HLE.Host.Windows;

namespace CraziiEmu.HLE.Host;

/// <summary>
/// Process-wide access point for the Windows host platform backend.
/// </summary>
public static class HostPlatform
{
    private static readonly Lazy<IHostPlatform> Instance = new(Create);

    public static IHostPlatform Current => Instance.Value;

    private static IHostPlatform Create()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return new WindowsHostPlatform();
        }

        throw new PlatformNotSupportedException(
            "CraziiEmu requires an x86-64 process on Windows.");
    }
}
