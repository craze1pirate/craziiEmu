// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core;

public interface IFileSystem
{
    bool Exists(string path);

    bool TryReadAllBytes(string path, out byte[] data);
}
