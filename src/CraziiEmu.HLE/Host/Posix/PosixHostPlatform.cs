// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE.Host.Posix;

internal sealed class PosixHostPlatform : IHostPlatform
{
    public IHostMemory Memory { get; } = new PosixHostMemory();

    public IHostThreading Threading { get; } = new PosixHostThreading();

    public IHostSymbolResolver Symbols { get; } = new PosixHostSymbolResolver();

    public IHostAudioOutput Audio { get; } = new PosixHostAudio();

    public IHostInput Input { get; } = new PosixHostInput();
}
