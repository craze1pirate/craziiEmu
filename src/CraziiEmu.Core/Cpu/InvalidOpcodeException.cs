// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;

namespace CraziiEmu.Core.Cpu;

/// <summary>
/// The exception that is thrown when the CPU emulator encounters an unsupported or invalid opcode sequence.
/// </summary>
public class InvalidOpcodeException : Exception
{
    /// <summary>
    /// Gets the Instruction Pointer (RIP) where the fault occurred.
    /// </summary>
    public ulong Rip { get; }

    /// <summary>
    /// Gets the first byte of the unhandled opcode sequence.
    /// </summary>
    public byte OpcodeByte { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidOpcodeException"/> class.
    /// </summary>
    /// <param name="rip">The instruction pointer at the time of the fault.</param>
    /// <param name="opcodeByte">The raw byte that failed to decode.</param>
    public InvalidOpcodeException(ulong rip, byte opcodeByte)
        : base($"Invalid or unsupported x86-64 opcode '0x{opcodeByte:X2}' encountered at RIP 0x{rip:X16}.")
    {
        Rip = rip;
        OpcodeByte = opcodeByte;
    }
}
