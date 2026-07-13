// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;

namespace CraziiEmu.Core.Loader;

public interface ISelfLoader
{
    SelfImage Load(ReadOnlySpan<byte> imageData, IVirtualMemory virtualMemory);

    SelfImage Load(ReadOnlySpan<byte> imageData, IVirtualMemory virtualMemory, IFileSystem? fs, string? mountRoot);

    SelfImage Load(ReadOnlySpan<byte> imageData, IVirtualMemory virtualMemory, IModuleManager moduleManager);

    SelfImage Load(ReadOnlySpan<byte> imageData, IVirtualMemory virtualMemory, IModuleManager moduleManager, IFileSystem? fs, string? mountRoot);

    SelfImage LoadAdditional(ReadOnlySpan<byte> imageData, IVirtualMemory virtualMemory, IModuleManager moduleManager, IFileSystem? fs, string? mountRoot);
}
