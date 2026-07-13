// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Text;

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// A prototype compiler that translates the Shader AST into host-readable GLSL.
/// </summary>
public class GlslShaderCompiler
{
    /// <summary>
    /// Iterates over the Abstract Syntax Tree (AST) and generates a valid, compilable GLSL fragment shader body.
    /// </summary>
    /// <param name="instructions">The decoded shader instructions.</param>
    /// <returns>A string containing the generated GLSL code.</returns>
    public string TranslateToGlsl(List<ShaderInstruction> instructions)
    {
        if (instructions == null)
            throw new ArgumentNullException(nameof(instructions));

        var sb = new StringBuilder();
        
        // Wrap output in a valid GLSL main block
        sb.AppendLine("void main() {");

        foreach (var instr in instructions)
        {
            if (instr.Opcode == ShaderOpcode.SEndPgm)
            {
                break;
            }

            string dest = $"v{instr.DestRegister}";
            string src0 = $"v{instr.Src0Register}";
            string src1 = $"v{instr.Src1Register}";

            sb.Append("    float ").Append(dest).Append(" = ");

            switch (instr.Opcode)
            {
                case ShaderOpcode.VAddF32:
                    sb.Append(src0).Append(" + ").Append(src1).AppendLine(";");
                    break;
                case ShaderOpcode.VMulF32:
                    sb.Append(src0).Append(" * ").Append(src1).AppendLine(";");
                    break;
                default:
                    // Should be unreachable due to decoder safety, but included for completeness
                    throw new NotSupportedException($"Cannot compile unsupported opcode: {instr.Opcode}");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }
}
