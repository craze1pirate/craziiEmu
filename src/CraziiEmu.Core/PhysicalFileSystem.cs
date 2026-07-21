// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core;

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool Exists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(path);
    }

    public bool TryReadAllBytes(string path, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!File.Exists(path))
            return false;

        try
        {
            data = File.ReadAllBytes(path);
            return true;
        }
        catch
        {
            data = Array.Empty<byte>();
            return false;
        }
    }
}
