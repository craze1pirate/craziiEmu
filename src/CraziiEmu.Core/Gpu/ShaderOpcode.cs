// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// Defines the set of supported RDNA2 micro-ISA operations for shader translation.
/// </summary>
public enum ShaderOpcode : byte
{
    /// <summary>
    /// Vector Float Add (v_add_f32).
    /// </summary>
    VAddF32 = 0x01,

    /// <summary>
    /// Vector Float Multiply (v_mul_f32).
    /// </summary>
    VMulF32 = 0x02,

    /// <summary>
    /// End Program (s_endpgm).
    /// </summary>
    SEndPgm = 0x3F
}
