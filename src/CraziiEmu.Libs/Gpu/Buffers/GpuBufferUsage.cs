// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Libs.Gpu.Buffers;

public enum GpuBufferUsage : byte
{
    DeviceLocal,
    Upload,
    Download,
    Stream,
}
