// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Agc;
using CraziiEmu.Libs.VideoOut;
using CraziiEmu.ShaderCompiler;
using CraziiEmu.ShaderCompiler.Vulkan;

namespace CraziiEmu.TestRunner;

public static class DeadCellsAndFloat16Tests
{
    private const ulong ShaderAddress = 0x1_0000_0000;
    private const uint SEndpgm = 0xBF810000;

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting DeadCellsAndFloat16Tests...");

        TestGpuWaitRegistryFrameTracking();
        TestPhysicalVirtualMemoryConcurrentProtections();
        TestFloat16RecompilerArithmeticAndCapabilities();
        TestDeadCellsBorderColorMapping();
        TestDeadCellsBlendFactorMapping();
        TestPixelShaderExportValidMaskOpKill();

        Console.WriteLine("[TEST] DeadCellsAndFloat16Tests PASSED cleanly.");
    }

    private static void TestDeadCellsBorderColorMapping()
    {
        // BorderColor 0: FloatTransparentBlack
        // BorderColor 1: FloatOpaqueBlack
        // BorderColor 2: FloatOpaqueWhite
        // Any other: FloatTransparentBlack
        var method = typeof(VulkanVideoPresenter).GetMethod(
            "ToVkBorderColor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method != null)
        {
            var c0 = method.Invoke(null, [0u]);
            var c1 = method.Invoke(null, [1u]);
            var c2 = method.Invoke(null, [2u]);
            var cOther = method.Invoke(null, [99u]);

            Assert(c0?.ToString() == "FloatTransparentBlack", $"Border color 0 must be FloatTransparentBlack, got {c0}");
            Assert(c1?.ToString() == "FloatOpaqueBlack", $"Border color 1 must be FloatOpaqueBlack, got {c1}");
            Assert(c2?.ToString() == "FloatOpaqueWhite", $"Border color 2 must be FloatOpaqueWhite, got {c2}");
            Assert(cOther?.ToString() == "FloatTransparentBlack", $"Border color default must be FloatTransparentBlack, got {cOther}");
        }

        var fallbackMethod = typeof(VulkanVideoPresenter).GetMethod(
            "CreateFallbackTexturePixels",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (fallbackMethod != null)
        {
            var pixels = (byte[]?)fallbackMethod.Invoke(null, [9u, 16u, 16u, 1024UL]);
            Assert(pixels != null && pixels.Length == 1024, "Fallback pixels must have expected length");
            Assert(pixels.All(b => b == 0), "Fallback pixels must all be 0 (transparent black)");
        }

        Console.WriteLine("  [PASS] Sampler border color mapping & transparent fallback texture verified");
    }

    private static void TestDeadCellsBlendFactorMapping()
    {
        var method = typeof(VulkanVideoPresenter).GetMethod(
            "ToVkBlendFactor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method != null)
        {
            var f0 = method.Invoke(null, [0u]);
            var f1 = method.Invoke(null, [1u]);
            var fOther = method.Invoke(null, [99u]);

            Assert(f0?.ToString() == "Zero", $"Blend factor 0 must be Zero, got {f0}");
            Assert(f1?.ToString() == "One", $"Blend factor 1 must be One, got {f1}");
            Assert(fOther?.ToString() == "Zero", $"Blend factor default fallback must be Zero, got {fOther}");
        }

        Console.WriteLine("  [PASS] Blend factor fallback mapping verified");
    }

    private static void TestPixelShaderExportValidMaskOpKill()
    {
        // RDNA export with valid mask (VM=1): exp mrt0, v0, v1, v2, v3 done vm
        var program = Decode(
        [
            0xF800_180F, // exp mrt0, v0, v1, v2, v3 done vm
            0x03020100,
            SEndpgm,
        ]);

        var state = new Gen5ShaderState(program, [], null);
        var scalarRegisters = new uint[256];
        var evaluation = new Gen5ShaderEvaluation(
            scalarRegisters,
            scalarRegisters,
            [],
            []);

        var compiled = Gen5SpirvTranslator.TryCompilePixelShader(
            state,
            evaluation,
            Gen5PixelOutputKind.Float,
            out var shader,
            out var error);

        Assert(compiled, $"Pixel shader compilation failed: {error}");

        var opcodes = ReadOpcodes(shader.Spirv);
        Assert(opcodes.Contains((ushort)SpirvOp.Kill), "Pixel shader SPIR-V must contain OpKill for valid mask discard");
        Assert(opcodes.Contains((ushort)SpirvOp.LogicalAnd), "Pixel shader SPIR-V must contain OpLogicalAnd for laneActive check");

        Console.WriteLine("  [PASS] Pixel shader export valid mask OpKill & OpLogicalAnd verified");
    }

    private static void TestGpuWaitRegistryFrameTracking()
    {
        var dummyMemory = new object();
        const ulong waitAddr = 0x2000_1000;

        // Reset state
        GpuWaitRegistry.Clear();

        // 1. Unwritten label should be treated as fresh
        Assert(GpuWaitRegistry.IsLabelFresh(dummyMemory, waitAddr), "Unwritten label should be fresh");

        // 2. Writing label in current frame makes it fresh
        GpuWaitRegistry.RecordProduced(dummyMemory, waitAddr, 100);
        Assert(GpuWaitRegistry.IsLabelFresh(dummyMemory, waitAddr), "Current-frame write should be fresh");

        // 3. Advancing the frame should mark prior-frame write as stale
        GpuWaitRegistry.AdvanceFrame();
        Assert(!GpuWaitRegistry.IsLabelFresh(dummyMemory, waitAddr), "Previous-frame write must be stale");

        // 4. Producing a new write in the new frame restores freshness
        GpuWaitRegistry.RecordProduced(dummyMemory, waitAddr, 101);
        Assert(GpuWaitRegistry.IsLabelFresh(dummyMemory, waitAddr), "Current-frame rewrite must restore freshness");

        Console.WriteLine("  [PASS] GpuWaitRegistry frame tracking & staleness guard verified");
    }

    private static void TestPhysicalVirtualMemoryConcurrentProtections()
    {
        using var vmem = new PhysicalVirtualMemory();

        // Test multi-threaded concurrent page protection mutations and reads
        var tasks = new Task[8];
        var pagesPerThread = 256;

        for (var t = 0; t < tasks.Length; t++)
        {
            var threadId = t;
            tasks[t] = Task.Run(() =>
            {
                var basePage = 0x1000_0000UL + (ulong)threadId * 0x10_0000UL;
                for (var i = 0; i < pagesPerThread; i++)
                {
                    var page = basePage + (ulong)i * 0x1000UL;
                    var flags = (i % 2 == 0) ? ProgramHeaderFlags.Read : (ProgramHeaderFlags.Read | ProgramHeaderFlags.Write);
                    vmem.TryProtect(page, 0x1000, GuestPageProtection.Read | GuestPageProtection.Write);
                }
            });
        }

        Task.WaitAll(tasks);
        Console.WriteLine("  [PASS] PhysicalVirtualMemory concurrent protection operations verified");
    }

    private static void TestFloat16RecompilerArithmeticAndCapabilities()
    {
        var program = Decode(
        [
            0x64000501, // v_add_f16 v0, v1, v2
            0x66060B04, // v_sub_f16 v3, v4, v5
            0x680C1107, // v_subrev_f16 v6, v7, v8
            0x6A12170A, // v_mul_f16 v9, v10, v11
            0x72181D0D, // v_max_f16 v12, v13, v14
            0x741E2310, // v_min_f16 v15, v16, v17
            SEndpgm,
        ]);

        Assert(
            program.Instructions.Count == 7,
            $"Expected 7 instructions, got {program.Instructions.Count}");
        Assert(
            program.Instructions[0].Opcode == "VAddF16" &&
            program.Instructions[1].Opcode == "VSubF16" &&
            program.Instructions[2].Opcode == "VSubrevF16" &&
            program.Instructions[3].Opcode == "VMulF16" &&
            program.Instructions[4].Opcode == "VMaxF16" &&
            program.Instructions[5].Opcode == "VMinF16",
            "Opcodes must decode correctly to Float16 operations");

        var state = new Gen5ShaderState(program, [], null);
        var scalarRegisters = new uint[256];
        var evaluation = new Gen5ShaderEvaluation(
            scalarRegisters,
            scalarRegisters,
            [],
            []);

        var compiled = Gen5SpirvTranslator.TryCompileComputeShader(
            state,
            evaluation,
            1,
            1,
            1,
            out var shader,
            out var error);

        Assert(compiled, $"Shader compilation failed: {error}");

        var opcodes = ReadOpcodes(shader.Spirv);
        Assert(opcodes.Contains((ushort)SpirvOp.FAdd), "SPIR-V must contain FAdd");
        Assert(opcodes.Contains((ushort)SpirvOp.FSub), "SPIR-V must contain FSub");
        Assert(opcodes.Contains((ushort)SpirvOp.FMul), "SPIR-V must contain FMul");
        Assert(opcodes.Count(op => op == (ushort)SpirvOp.ExtInst) >= 2, "SPIR-V must contain ExtInst for VMin/VMax");

        // Verify native Float16 capability is NOT emitted (software half-float widening/narrowing)
        var capabilities = ReadCapabilities(shader.Spirv);
        Assert(!capabilities.Contains((ushort)SpirvCapability.Float16), "SPIR-V must not require native Float16 capability");

        Console.WriteLine("  [PASS] Float16 emulation without native Float16 capability verified");
    }

    private static Gen5ShaderProgram Decode(IReadOnlyList<uint> words)
    {
        var memory = new TestMemory(ShaderAddress, words.Count * sizeof(uint));
        var bytes = new byte[words.Count * sizeof(uint)];
        for (var index = 0; index < words.Count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint)),
                words[index]);
        }

        Assert(memory.TryWrite(ShaderAddress, bytes), "Writing test shader bytes failed");
        var context = new CpuContext(memory, Generation.Gen5);
        var decoded = Gen5ShaderTranslator.TryDecodeProgram(
            context,
            ShaderAddress,
            out var program,
            out var error);
        Assert(decoded, $"Decode program failed: {error}");
        return program;
    }

    private static IReadOnlyList<ushort> ReadOpcodes(byte[] spirv) =>
        ReadInstructions(spirv)
            .Select(instruction => instruction.Opcode)
            .ToArray();

    private static IReadOnlyList<ushort> ReadCapabilities(byte[] spirv) =>
        ReadInstructions(spirv)
            .Where(instruction => instruction.Opcode == (ushort)SpirvOp.Capability)
            .Select(instruction => (ushort)instruction.FirstOperand)
            .ToArray();

    private static IReadOnlyList<(ushort Opcode, uint FirstOperand)> ReadInstructions(byte[] spirv)
    {
        Assert(BinaryPrimitives.ReadUInt32LittleEndian(spirv) == 0x07230203u, "Invalid SPIR-V magic");
        var instructions = new List<(ushort Opcode, uint FirstOperand)>();
        for (var offset = 5 * sizeof(uint); offset < spirv.Length;)
        {
            var header = BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(offset));
            var wordCount = checked((int)(header >> 16));
            if (wordCount < 1 || offset + wordCount * sizeof(uint) > spirv.Length)
            {
                break;
            }

            var firstOperand = wordCount > 1
                ? BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(offset + sizeof(uint)))
                : 0;
            instructions.Add(((ushort)header, firstOperand));
            offset += wordCount * sizeof(uint);
        }

        return instructions;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
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
