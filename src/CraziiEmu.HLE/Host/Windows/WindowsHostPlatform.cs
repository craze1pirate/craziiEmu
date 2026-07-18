// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE.Host.Windows;

internal sealed class WindowsHostPlatform : IHostPlatform
{
    public IHostMemory Memory { get; } = new WindowsHostMemory();

    public IHostThreading Threading { get; } = new WindowsHostThreading();

    public IHostSymbolResolver Symbols { get; } = new WindowsHostSymbolResolver();

    public IHostAudioOutput Audio { get; } = new WindowsWaveOutAudio();

    public IHostInput Input { get; } = new WindowsHostInput();
}
