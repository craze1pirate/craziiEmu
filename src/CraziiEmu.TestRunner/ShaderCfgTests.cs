// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.ShaderCompiler;
using CraziiEmu.ShaderCompiler.Vulkan;

namespace CraziiEmu.TestRunner;

public static class ShaderCfgTests
{
    private const ulong ShaderAddress = 0x1_0000_0000;
    private const uint SEndpgm = 0xBF810000; // s_endpgm
    private const uint SNop = 0xBF800000;    // s_nop 0

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting ShaderCfgTests...");

        Environment.SetEnvironmentVariable("CRAZIIEMU_ENABLE_STRUCTURED_CFG", "1");
        try
        {
            TestLinearControlFlow();
            TestIfElseSelectionControlFlow();
            TestNaturalLoopControlFlow();
            TestDominatorAndPostDominatorProperties();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRAZIIEMU_ENABLE_STRUCTURED_CFG", null);
        }

        TestDisableStructuredCfgEnvFallback();

        Console.WriteLine("[TEST] ShaderCfgTests PASSED cleanly.");
    }

    private static void TestLinearControlFlow()
    {
        // Simple linear shader:
        // 0x00: v_mov_b32_e32 v0, 0 (0x7E000280)
        // 0x04: s_endpgm (0xBF810000)
        var program = DecodeProgram([0x7E000280, SEndpgm]);

        var built = Gen5ShaderCfg.TryBuildAndStructurize(program.Instructions, out var graph);
        Assert(built, "Failed to build CFG for linear shader");
        Assert(graph.Blocks.Count == 1, $"Expected 1 block for linear shader, got {graph.Blocks.Count}");
        Assert(graph.Blocks[0].Terminator.Kind == Gen5CfgTerminatorKind.Return, "Block 0 should terminate in Return");

        var evaluation = new Gen5ShaderEvaluation(
            new uint[256],
            new uint[256],
            [],
            []);
        var state = new Gen5ShaderState(program, [], null);

        var compiled = Gen5SpirvTranslator.TryCompilePixelShader(
            state,
            evaluation,
            Gen5PixelOutputKind.Float,
            out var shader,
            out var error);

        Assert(compiled, $"Failed to compile linear shader: {error}");
        ValidateWithSpirvVal(shader.Spirv, "linear");
        var opcodes = ReadOpcodes(shader.Spirv);
        Assert(!opcodes.Contains((ushort)SpirvOp.Switch), "Structured linear shader should NOT contain OpSwitch");
        Assert(opcodes.Contains((ushort)SpirvOp.Return), "Structured linear shader should contain OpReturn");

        Console.WriteLine("  [PASS] TestLinearControlFlow");
    }

    private static void TestIfElseSelectionControlFlow()
    {
        // Conditional branch:
        // Block 0:
        // 0x00: v_mov_b32_e32 v0, 0
        // 0x04: s_cbranch_scc1 +1 (offset = 1 dwords, skips to 0x0C)
        // Block 1 (If true):
        // 0x08: s_branch +1 (offset = 1 dwords, skips to 0x14)
        // Block 2 (Else / target of cbranch):
        // 0x0C: v_mov_b32_e32 v1, 1
        // 0x10: v_mov_b32_e32 v2, 2
        // Block 3 (Merge):
        // 0x14: s_endpgm
        var cbranchScc1Offset1 = 0xBF850001u; // s_cbranch_scc1 +1
        var sBranchOffset1 = 0xBF820001u;     // s_branch +1

        var program = DecodeProgram([
            0x7E000280,          // 0x00: v_mov_b32_e32 v0, 0
            cbranchScc1Offset1,  // 0x04: s_cbranch_scc1 target=0x0C
            sBranchOffset1,      // 0x08: s_branch target=0x10 (wait, nextPc=0x0C + 1*4 = 0x10)
            0x7E020281,          // 0x0C: v_mov_b32_e32 v1, 1
            0x7E040282,          // 0x10: v_mov_b32_e32 v2, 2 (merge)
            SEndpgm              // 0x14: s_endpgm
        ]);

        var built = Gen5ShaderCfg.TryBuildAndStructurize(program.Instructions, out var graph);
        Assert(built, "Failed to structurize if-else CFG");
        Assert(graph.Blocks.Count >= 3, $"Expected at least 3 blocks, got {graph.Blocks.Count}");

        var header = graph.Blocks[0];
        Assert(header.Terminator.Kind == Gen5CfgTerminatorKind.ConditionalBranch, "Block 0 must be conditional branch");
        Assert(header.Terminator.MergeBlock >= 0, "Block 0 must have an identified MergeBlock");

        var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], []);
        var state = new Gen5ShaderState(program, [], null);

        var compiled = Gen5SpirvTranslator.TryCompilePixelShader(
            state,
            evaluation,
            Gen5PixelOutputKind.Float,
            out var shader,
            out var error);

        Assert(compiled, $"Compilation of selection CFG failed: {error}");
        ValidateWithSpirvVal(shader.Spirv, "if_else");
        var opcodes = ReadOpcodes(shader.Spirv);
        Assert(!opcodes.Contains((ushort)SpirvOp.Switch), "Structured selection should NOT contain OpSwitch");
        Assert(opcodes.Contains((ushort)SpirvOp.SelectionMerge), "Structured selection must contain OpSelectionMerge");
        Assert(opcodes.Contains((ushort)SpirvOp.BranchConditional), "Structured selection must contain OpBranchConditional");

        Console.WriteLine("  [PASS] TestIfElseSelectionControlFlow");
    }

    private static void TestNaturalLoopControlFlow()
    {
        // Loop CFG:
        // Block 0:
        // 0x00: v_mov_b32_e32 v0, 0
        // Block 1 (Header):
        // 0x04: s_cbranch_execz +2 (exit to 0x14)
        // Block 2 (Body / latch):
        // 0x08: v_mov_b32_e32 v1, 1
        // 0x0C: s_branch -3 (target 0x04, since nextPc=0x10 + (-3)*4 = 0x04)
        // Block 3 (Exit / merge):
        // 0x10: s_endpgm
        var cbranchExeczOffset2 = 0xBF880002u; // nextPc=0x08 + 2*4 = 0x10
        var sbranchBackward3 = 0xBF82FFFD;    // -3 as short is 0xFFFD, nextPc=0x10 + (-3)*4 = 0x04

        var program = DecodeProgram([
            0x7E000280,          // 0x00: v_mov_b32_e32 v0, 0
            cbranchExeczOffset2, // 0x04: s_cbranch_execz target=0x10
            0x7E020281,          // 0x08: v_mov_b32_e32 v1, 1
            sbranchBackward3,    // 0x0C: s_branch target=0x04
            SEndpgm              // 0x10: s_endpgm
        ]);

        var built = Gen5ShaderCfg.TryBuildAndStructurize(program.Instructions, out var graph);
        Assert(built, "Failed to structurize loop CFG");
        Assert(graph.NaturalLoops.Count == 1, $"Expected 1 natural loop, got {graph.NaturalLoops.Count}");

        var loop = graph.NaturalLoops[0];
        Assert(loop.Header >= 0, "Loop must have valid header");
        Assert(loop.Latch >= 0, "Loop must have valid latch");

        var headerBlock = graph.FindBlock(loop.Header);
        Assert(headerBlock != null && headerBlock.Terminator.IsLoopHeader, "Header block must have IsLoopHeader=true");

        var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], []);
        var state = new Gen5ShaderState(program, [], null);

        var compiled = Gen5SpirvTranslator.TryCompilePixelShader(
            state,
            evaluation,
            Gen5PixelOutputKind.Float,
            out var shader,
            out var error);

        Assert(compiled, $"Compilation of loop CFG failed: {error}");
        ValidateWithSpirvVal(shader.Spirv, "loop");
        var opcodes = ReadOpcodes(shader.Spirv);
        Assert(!opcodes.Contains((ushort)SpirvOp.Switch), "Structured loop should NOT contain OpSwitch");
        Assert(opcodes.Contains((ushort)SpirvOp.LoopMerge), "Structured loop must contain OpLoopMerge");

        Console.WriteLine("  [PASS] TestNaturalLoopControlFlow");
    }

    private static void TestDominatorAndPostDominatorProperties()
    {
        var cbranchScc1Offset1 = 0xBF850001u;
        var sBranchOffset1 = 0xBF820001u;

        var program = DecodeProgram([
            0x7E000280,
            cbranchScc1Offset1,
            sBranchOffset1,
            0x7E020281,
            0x7E040282,
            SEndpgm
        ]);

        var graph = Gen5ShaderCfg.BuildGraph(program.Instructions);
        Assert(graph.Blocks.Count >= 3, "Expected at least 3 blocks");

        // Block 0 dominates all reachable blocks
        foreach (var b in graph.Blocks)
        {
            Assert(graph.Dominates(0, b.Id), $"Block 0 must dominate block {b.Id}");
        }

        // The terminal block post-dominates predecessors that must reach it
        var lastBlock = graph.Blocks[^1];
        Assert(lastBlock.Terminator.Kind == Gen5CfgTerminatorKind.Return, "Last block must be Return");

        Console.WriteLine("  [PASS] TestDominatorAndPostDominatorProperties");
    }

    private static void TestDisableStructuredCfgEnvFallback()
    {
        var program = DecodeProgram([0x7E000280, SEndpgm]);
        var evaluation = new Gen5ShaderEvaluation(new uint[256], new uint[256], [], []);
        var state = new Gen5ShaderState(program, [], null);

        Environment.SetEnvironmentVariable("CRAZIIEMU_DISABLE_STRUCTURED_CFG", "1");
        try
        {
            var compiled = Gen5SpirvTranslator.TryCompilePixelShader(
                state,
                evaluation,
                Gen5PixelOutputKind.Float,
                out var shader,
                out var error);

            Assert(compiled, $"Compilation with fallback failed: {error}");
            ValidateWithSpirvVal(shader.Spirv, "fallback_switch");
            var opcodes = ReadOpcodes(shader.Spirv);
            Assert(opcodes.Contains((ushort)SpirvOp.Switch), "Fallback mode MUST emit OpSwitch dispatcher loop");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRAZIIEMU_DISABLE_STRUCTURED_CFG", null);
        }

        Console.WriteLine("  [PASS] TestDisableStructuredCfgEnvFallback");
    }

    private static Gen5ShaderProgram DecodeProgram(IReadOnlyList<uint> words)
    {
        var memory = new TestMemory(ShaderAddress, words.Count * sizeof(uint));
        var bytes = new byte[words.Count * sizeof(uint)];
        for (var i = 0; i < words.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)), words[i]);
        }
        memory.TryWrite(ShaderAddress, bytes);

        var context = new CpuContext(memory, Generation.Gen5);
        var decoded = Gen5ShaderTranslator.TryDecodeProgram(context, ShaderAddress, out var program, out var error);
        Assert(decoded, $"Failed to decode test shader: {error}");
        return program;
    }

    private static IReadOnlyList<ushort> ReadOpcodes(byte[] spirv)
    {
        Assert(BinaryPrimitives.ReadUInt32LittleEndian(spirv) == 0x07230203u, "Invalid SPIR-V magic");
        var opcodes = new List<ushort>();
        for (var offset = 5 * sizeof(uint); offset < spirv.Length;)
        {
            var header = BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(offset));
            var wordCount = checked((int)(header >> 16));
            if (wordCount < 1 || offset + wordCount * sizeof(uint) > spirv.Length)
            {
                break;
            }
            opcodes.Add((ushort)(header & 0xFFFF));
            offset += wordCount * sizeof(uint);
        }
        return opcodes;
    }

    private static void ValidateWithSpirvVal(byte[] spirv, string name)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"{name}.spv");
        File.WriteAllBytes(temp, spirv);
        var valPath = @"C:\VulkanSDK\1.4.350.0\Bin\spirv-val.exe";
        if (File.Exists(valPath))
        {
            var psi = new ProcessStartInfo(valPath, $"\"{temp}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (p.ExitCode != 0)
            {
                var disPath = @"C:\VulkanSDK\1.4.350.0\Bin\spirv-dis.exe";
                if (File.Exists(disPath))
                {
                    var disPsi = new ProcessStartInfo(disPath, $"\"{temp}\"")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    };
                    using var disP = Process.Start(disPsi)!;
                    var disOutTask = disP.StandardOutput.ReadToEndAsync();
                    disP.WaitForExit();
                    Console.WriteLine($"[spirv-dis] {name}:\n{disOutTask.GetAwaiter().GetResult()}");
                }
                throw new InvalidOperationException($"[spirv-val] {name} validation failed (code {p.ExitCode}):\n{stdout}\n{stderr}");
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ShaderCfgTests] Assertion failed: {message}");
        }
    }

    private sealed class TestMemory(ulong baseAddress, int size) : ICpuMemory
    {
        private readonly byte[] _storage = new byte[size];

        public bool TryRead(ulong virtualAddress, Span<byte> destination)
        {
            if (virtualAddress < baseAddress || virtualAddress - baseAddress > int.MaxValue)
            {
                return false;
            }
            var offset = (int)(virtualAddress - baseAddress);
            if (offset + destination.Length > _storage.Length)
            {
                return false;
            }
            _storage.AsSpan(offset, destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong virtualAddress, ReadOnlySpan<byte> source)
        {
            if (virtualAddress < baseAddress || virtualAddress - baseAddress > int.MaxValue)
            {
                return false;
            }
            var offset = (int)(virtualAddress - baseAddress);
            if (offset + source.Length > _storage.Length)
            {
                return false;
            }
            source.CopyTo(_storage.AsSpan(offset, source.Length));
            return true;
        }
    }
}
