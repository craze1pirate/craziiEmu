// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// The exception that is thrown when the shader decoder encounters an unrecognized or invalid opcode.
/// </summary>
public class UnsupportedInstructionException : Exception
{
    /// <summary>
    /// Gets the raw 32-bit instruction that failed to decode.
    /// </summary>
    public uint RawInstruction { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedInstructionException"/> class.
    /// </summary>
    /// <param name="rawInstruction">The 32-bit invalid instruction.</param>
    public UnsupportedInstructionException(uint rawInstruction)
        : base($"Encountered an unsupported or corrupted RDNA2 instruction: 0x{rawInstruction:X8}")
    {
        RawInstruction = rawInstruction;
    }
}
