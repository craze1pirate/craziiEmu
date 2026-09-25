// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.ShaderCompiler;

namespace CraziiEmu.ShaderCompiler.Vulkan;

public static partial class Gen5SpirvTranslator
{
    private sealed partial class CompilationContext
    {
        private bool TryEmitStructuredControlFlow(
            Gen5CfgGraph cfgGraph,
            uint exitLabel,
            out string error)
        {
            error = string.Empty;
            var blockCount = cfgGraph.Blocks.Count;
            var blockLabels = new uint[blockCount];
            for (var i = 0; i < blockCount; i++)
            {
                blockLabels[i] = _module.AllocateId();
            }

            // Function entry branches directly to block 0
            _module.AddStatement(SpirvOp.Branch, blockLabels[0]);

            for (var i = 0; i < blockCount; i++)
            {
                var cfgBlock = cfgGraph.Blocks[i];
                _module.AddLabel(blockLabels[i]);

                for (var index = cfgBlock.StartIndex; index < cfgBlock.EndIndex; index++)
                {
                    var instruction = _request.Program.Instructions[index];
                    if (IsBranch(instruction.Opcode) || instruction.Opcode == "SEndpgm")
                    {
                        continue;
                    }

                    if (!TryEmitInstruction(instruction, out error))
                    {
                        error = $"pc=0x{instruction.Pc:X} {instruction.Opcode}: {error}";
                        return false;
                    }

                    CapturePixelVgprs(instruction);
                    CapturePixelVgprPoints(instruction);
                    MarkPixelPath(instruction);
                    CapturePixelExec(instruction);
                }

                var terminator = _request.Program.Instructions[cfgBlock.EndIndex - 1];
                uint condition = 0;
                if (cfgBlock.Terminator.Kind == Gen5CfgTerminatorKind.ConditionalBranch)
                {
                    if (!TryGetBranchCondition(terminator.Opcode, out condition))
                    {
                        error = $"pc=0x{terminator.Pc:X}: unknown branch condition for {terminator.Opcode}";
                        return false;
                    }
                }

                // If loop header, emit OpLoopMerge immediately before the branch instruction
                if (cfgBlock.Terminator.IsLoopHeader)
                {
                    var loopMerge = cfgBlock.Terminator.MergeBlock >= 0 && cfgBlock.Terminator.MergeBlock < blockCount
                        ? blockLabels[cfgBlock.Terminator.MergeBlock]
                        : exitLabel;
                    var loopContinue = cfgBlock.Terminator.ContinueBlock >= 0 && cfgBlock.Terminator.ContinueBlock < blockCount
                        ? blockLabels[cfgBlock.Terminator.ContinueBlock]
                        : blockLabels[i];

                    _module.AddStatement(SpirvOp.LoopMerge, loopMerge, loopContinue, 0);
                }

                switch (cfgBlock.Terminator.Kind)
                {
                    case Gen5CfgTerminatorKind.ConditionalBranch:
                    {
                        // In SPIR-V, a block with OpLoopMerge cannot also have OpSelectionMerge
                        if (!cfgBlock.Terminator.IsLoopHeader)
                        {
                            var selectionMerge = cfgBlock.Terminator.MergeBlock >= 0 && cfgBlock.Terminator.MergeBlock < blockCount
                                ? blockLabels[cfgBlock.Terminator.MergeBlock]
                                : exitLabel;
                            _module.AddStatement(SpirvOp.SelectionMerge, selectionMerge, 0);
                        }

                        var trueLabel = cfgBlock.Terminator.TrueBlock >= 0 && cfgBlock.Terminator.TrueBlock < blockCount
                            ? blockLabels[cfgBlock.Terminator.TrueBlock]
                            : exitLabel;
                        var falseLabel = cfgBlock.Terminator.FalseBlock >= 0 && cfgBlock.Terminator.FalseBlock < blockCount
                            ? blockLabels[cfgBlock.Terminator.FalseBlock]
                            : exitLabel;

                        _module.AddStatement(SpirvOp.BranchConditional, condition, trueLabel, falseLabel);
                        break;
                    }

                    case Gen5CfgTerminatorKind.Branch:
                    {
                        var targetLabel = cfgBlock.Terminator.TrueBlock >= 0 && cfgBlock.Terminator.TrueBlock < blockCount
                            ? blockLabels[cfgBlock.Terminator.TrueBlock]
                            : exitLabel;
                        _module.AddStatement(SpirvOp.Branch, targetLabel);
                        break;
                    }

                    case Gen5CfgTerminatorKind.Return:
                    {
                        _module.AddStatement(SpirvOp.Branch, exitLabel);
                        break;
                    }

                    default:
                    {
                        error = $"block=0x{cfgBlock.StartPc:X}: unsupported terminator kind {cfgBlock.Terminator.Kind}";
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
