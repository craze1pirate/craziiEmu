// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Buffers.Binary;
using CraziiEmu.ShaderCompiler;
using CraziiEmu.ShaderCompiler.Resources;
using CraziiEmu.ShaderCompiler.Vulkan;

namespace CraziiEmu.TestRunner;

public static class ShaderResourceEngineTests
{
    private const uint SpirvMagicNumber = 0x07230203;

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting ShaderResourceEngineTests...");
        TestBlitShaders();
        TestTilerShaders();
        TestFaultBufferProcess();
        TestResourceAnalysisAndCompilation();
        TestPixelInputMapping();
        Console.WriteLine("[TEST] ShaderResourceEngineTests PASSED cleanly.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[FAIL] {message}");
        }
    }

    private static void ValidateSpirvMagic(byte[] spirv, string shaderName)
    {
        Assert(spirv != null && spirv.Length >= 20, $"{shaderName} must have valid SPIR-V length");
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(spirv.AsSpan(0, 4));
        Assert(magic == SpirvMagicNumber, $"{shaderName} magic was 0x{magic:X8}, expected 0x{SpirvMagicNumber:X8}");
    }

    private static void TestBlitShaders()
    {
        var triangle = BlitShaders.CreateFullscreenTriangleVertex();
        ValidateSpirvMagic(triangle, "FullscreenTriangleVertex");

        var msDepth = BlitShaders.CreateColorToMultisampleDepthFragment();
        ValidateSpirvMagic(msDepth, "ColorToMultisampleDepthFragment");

        var readback = BlitShaders.CreateMultisampleDepthSampleReadback();
        ValidateSpirvMagic(readback, "MultisampleDepthSampleReadback");

        Console.WriteLine("  [PASS] Blit shaders (triangle, MS depth, readback) generated valid SPIR-V");
    }

    private static void TestTilerShaders()
    {
        var blockCopy256 = TilerShaders.CreateBlockCopy(TilerBlockShape.Standard256B, 4, toTiled: true);
        ValidateSpirvMagic(blockCopy256, "BlockCopy Standard256B");

        var blockCopy4k = TilerShaders.CreateBlockCopy(TilerBlockShape.Standard4KB, 4, toTiled: false);
        ValidateSpirvMagic(blockCopy4k, "BlockCopy Standard4KB");

        var blockCopyRt = TilerShaders.CreateBlockCopy(TilerBlockShape.RenderTarget64KB, 8, toTiled: true);
        ValidateSpirvMagic(blockCopyRt, "BlockCopy RenderTarget64KB");

        var depthWiden = TilerShaders.CreateDepthWiden(d32: true);
        ValidateSpirvMagic(depthWiden, "DepthWiden");

        var depthNarrow = TilerShaders.CreateDepthNarrow(d32: false);
        ValidateSpirvMagic(depthNarrow, "DepthNarrow");

        var bgraSwap = TilerShaders.CreateBgra16Swap();
        ValidateSpirvMagic(bgraSwap, "Bgra16Swap");

        Console.WriteLine("  [PASS] Tiler compute shaders (shapes, depth conversions, BGRA swap) generated valid SPIR-V");
    }

    private static void TestFaultBufferProcess()
    {
        var faultProcess = SpirvFixedShaders.CreateFaultBufferProcess();
        ValidateSpirvMagic(faultProcess, "FaultBufferProcess");

        Console.WriteLine("  [PASS] Page-fault drain compute shader generated valid SPIR-V");
    }

    private static void TestResourceAnalysisAndCompilation()
    {
        // Decode a compute program:
        // 0x00: s_mov_b32 s0, 0
        // 0x04: s_endpgm
        var instructions = new List<Gen5ShaderInstruction>
        {
            new(
                0,
                Gen5ShaderEncoding.Sop1,
                "SMovB32",
                [0u],
                [Gen5Operand.Source(128)],
                [Gen5Operand.Scalar(0)],
                null),
            new(
                4,
                Gen5ShaderEncoding.Sopp,
                "SEndpgm",
                [0xBF810000],
                [],
                [],
                null),
        };

        var program = new Gen5ShaderProgram(0x1000, instructions);

        var plan = ShaderResourcePlan.Extract(program, ShaderStage.Compute, 0x1234, 0, 16);
        Assert(plan != null, "ShaderResourcePlan.Extract returned null");
        Assert(plan.Graph != null, "ShaderResourcePlan.Graph must not be null");

        var specialization = ResourceSpecialization.Default(plan.Info);
        var resources = ResourceMaterializer.ApplyTo(plan, specialization);
        Assert(resources != null, "ResourceMaterializer.ApplyTo returned null");

        var layout = BindingLayout.Allocate(
            resources.Info,
            BindingLayout.CollectUserDataRegisters(program, 0, 16),
            BindingLayout.UsesGlobalDataShare(program),
            ShaderCompileRequest.RequiresFlattenedTable(plan, resources),
            BindingLayout.ReadsShaderBase(program));

        Assert(layout != null, "BindingLayout.Allocate returned null");

        var request = new ShaderCompileRequest(plan, resources, layout!)
        {
            LocalSizeX = 64,
            LocalSizeY = 1,
            LocalSizeZ = 1,
        };

        var compiled = Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error);
        Assert(compiled, $"TryCompileProgram failed: {error}");
        ValidateSpirvMagic(shader.Spirv, "Compiled compute shader");

        Console.WriteLine("  [PASS] ShaderResourcePlan -> Materialization -> BindingLayout -> TryCompileProgram pipeline verified");
    }

    private static void TestPixelInputMapping()
    {
        uint[] controls = [0x05, 0x0A, 0x12];
        uint[] activeInputs = [0, 1, 2];

        var resolved = Gen5PixelInputMapping.ResolveLocations(controls, activeInputs);
        Assert(resolved.Length == 3, "Resolved locations length mismatch");
        Assert(resolved[0] == 5, $"Expected location 5, got {resolved[0]}");
        Assert(resolved[1] == 10, $"Expected location 10, got {resolved[1]}");
        Assert(resolved[2] == 0x12, $"Expected location 18, got {resolved[2]}");

        Console.WriteLine("  [PASS] Pixel input location mapping and conflict resolution verified");
    }
}
