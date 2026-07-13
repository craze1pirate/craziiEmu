// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// A prototype decoder that parses a micro-subset of RDNA2 ISA bytecode into an Abstract Syntax Tree (AST).
/// </summary>
public class Rdna2ShaderDecoder
{
    /// <summary>
    /// Decodes the provided raw bytecode into a list of parsed shader instructions.
    /// </summary>
    /// <param name="bytecode">The raw shader binary data.</param>
    /// <returns>A list of decoded instructions.</returns>
    /// <exception cref="UnsupportedInstructionException">Thrown if an unrecognized opcode is parsed.</exception>
    public List<ShaderInstruction> Decode(ReadOnlySpan<byte> bytecode)
    {
        var instructions = new List<ShaderInstruction>();
        var span32 = MemoryMarshal.Cast<byte, uint>(bytecode);

        for (int i = 0; i < span32.Length; i++)
        {
            uint instr = span32[i];
            
            // Extract Opcode [31:26]
            byte rawOpcode = (byte)((instr >> 26) & 0x3F);

            if (!Enum.IsDefined(typeof(ShaderOpcode), rawOpcode))
            {
                throw new UnsupportedInstructionException(instr);
            }

            var opcode = (ShaderOpcode)rawOpcode;

            if (opcode == ShaderOpcode.SEndPgm)
            {
                // For s_endpgm, remaining bits are ignored
                instructions.Add(new ShaderInstruction(opcode, 0, 0, 0));
                break; // End of program reached
            }

            // Extract Dest Reg [25:18]
            byte dest = (byte)((instr >> 18) & 0xFF);
            
            // Extract Src0 Reg [17:10]
            byte src0 = (byte)((instr >> 10) & 0xFF);
            
            // Extract Src1 Reg [9:2]
            byte src1 = (byte)((instr >> 2) & 0xFF);

            instructions.Add(new ShaderInstruction(opcode, dest, src0, src1));
        }

        return instructions;
    }
}
