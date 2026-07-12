// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using CraziiEmu.Core.Gpu;
using Xunit;

namespace CraziiEmu.Core.Tests.Gpu;

/// <summary>
/// Contains unit tests for the RDNA2 micro-ISA shader decoder and GLSL translation.
/// </summary>
public class ShaderTranslatorTests
{
    /// <summary>
    /// Verifies that the decoder successfully extracts the opcode and register fields 
    /// using correct bit-masking and bit-shifting.
    /// </summary>
    [Fact]
    public void Decode_WithValidVAddF32AndSEndPgm_ReturnsCorrectAst()
    {
        // Arrange
        var decoder = new Rdna2ShaderDecoder();

        // Construct 32-bit instructions manually:
        // v_add_f32 v3, v1, v2
        // Opcode [31:26] = 0x01
        // Dest   [25:18] = 3
        // Src0   [17:10] = 1
        // Src1   [9:2]   = 2
        // Rest   [1:0]   = 0
        uint instr1 = (0x01U << 26) | (3U << 18) | (1U << 10) | (2U << 2);

        // s_endpgm
        // Opcode [31:26] = 0x3F
        uint instr2 = (0x3FU << 26);

        // Pack into a little-endian byte array
        byte[] bytecode = new byte[8];
        BitConverter.TryWriteBytes(bytecode.AsSpan(0, 4), instr1);
        BitConverter.TryWriteBytes(bytecode.AsSpan(4, 4), instr2);

        // Act
        List<ShaderInstruction> ast = decoder.Decode(bytecode);

        // Assert
        Assert.Equal(2, ast.Count);
        
        var vAdd = ast[0];
        Assert.Equal(ShaderOpcode.VAddF32, vAdd.Opcode);
        Assert.Equal(3, vAdd.DestRegister);
        Assert.Equal(1, vAdd.Src0Register);
        Assert.Equal(2, vAdd.Src1Register);

        var sEnd = ast[1];
        Assert.Equal(ShaderOpcode.SEndPgm, sEnd.Opcode);
    }

    /// <summary>
    /// Verifies that the AST is translated to valid, host-readable GLSL format.
    /// </summary>
    [Fact]
    public void TranslateToGlsl_WithValidAst_GeneratesCorrectGlsl()
    {
        // Arrange
        var compiler = new GlslShaderCompiler();
        var ast = new List<ShaderInstruction>
        {
            new ShaderInstruction(ShaderOpcode.VAddF32, 3, 1, 2),
            new ShaderInstruction(ShaderOpcode.SEndPgm, 0, 0, 0)
        };

        // Act
        string glsl = compiler.TranslateToGlsl(ast);

        // Assert
        // Standardize line endings for cross-platform matching
        string normalizedGlsl = glsl.Replace("\r\n", "\n");
        string expectedGlsl = "void main() {\n    float v3 = v1 + v2;\n}\n";

        Assert.Equal(expectedGlsl, normalizedGlsl);
    }

    /// <summary>
    /// Verifies that encountering an unrecognized opcode triggers a custom exception, 
    /// blocking corrupted bytecode from execution.
    /// </summary>
    [Fact]
    public void Decode_WithUnknownOpcode_ThrowsUnsupportedInstructionException()
    {
        // Arrange
        var decoder = new Rdna2ShaderDecoder();

        // To trigger unknown opcode, use an opcode not in ShaderOpcode. e.g. 0x05
        uint unknownOpcodeInstr = 0x05U << 26; 

        byte[] bytecode = new byte[4];
        BitConverter.TryWriteBytes(bytecode, unknownOpcodeInstr);

        // Act & Assert
        var ex = Assert.Throws<UnsupportedInstructionException>(() => decoder.Decode(bytecode));
        Assert.Equal(unknownOpcodeInstr, ex.RawInstruction);
    }
}
