// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// Represents a decoded instruction within the Shader Abstract Syntax Tree (AST).
/// </summary>
/// <param name="Opcode">The recognized instruction operation.</param>
/// <param name="DestRegister">The destination vector register index.</param>
/// <param name="Src0Register">The first source vector register index.</param>
/// <param name="Src1Register">The second source vector register index.</param>
public record struct ShaderInstruction(
    ShaderOpcode Opcode,
    byte DestRegister,
    byte Src0Register,
    byte Src1Register
);
