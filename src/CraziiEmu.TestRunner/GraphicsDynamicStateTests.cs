// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Generic;
using CraziiEmu.Libs.Agc;
using CraziiEmu.Libs.VideoOut;
using Silk.NET.Vulkan;

namespace CraziiEmu.TestRunner;

public static class GraphicsDynamicStateTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running GraphicsDynamicStateTests...");

        TestConvertPolygonOffsetConstantFactor();
        TestDecodeDepthStateBiasAndStencil();
        TestGetBaseVertexUserDataFallback();

        Console.WriteLine("[TEST] GraphicsDynamicStateTests PASSED successfully.");
    }

    private static void TestConvertPolygonOffsetConstantFactor()
    {
        // 1. Float format depth buffer: factor should be unchanged regardless of host format
        float guestFactor = 1.25f;
        float resultFloat = VulkanVideoPresenter.ConvertPolygonOffsetConstantFactor(
            guestFactor,
            negNumDbBits: -32,
            dbIsFloatFmt: true,
            Format.D32Sfloat);
        Assert(Math.Abs(resultFloat - 1.25f) < 1e-6f, "Float depth format should preserve guestFactor unchanged");

        // 2. Fixed format with matching host D16: hostDepthBits=16, negNumDbBits=-16 -> ScaleB(1.0, 0) == 1.0
        float resultD16 = VulkanVideoPresenter.ConvertPolygonOffsetConstantFactor(
            1.0f,
            negNumDbBits: -16,
            dbIsFloatFmt: false,
            Format.D16Unorm);
        Assert(Math.Abs(resultD16 - 1.0f) < 1e-6f, "Matching D16 depth format should scale to 1.0");

        // 3. Fixed format with matching host D24: hostDepthBits=24, negNumDbBits=-24 -> ScaleB(2.0, 0) == 2.0
        float resultD24 = VulkanVideoPresenter.ConvertPolygonOffsetConstantFactor(
            2.0f,
            negNumDbBits: -24,
            dbIsFloatFmt: false,
            Format.D24UnormS8Uint);
        Assert(Math.Abs(resultD24 - 2.0f) < 1e-6f, "Matching D24 depth format should scale to 2.0");

        // 4. Fixed format with guest D24 (-24) on host D16 (16): shift = 16 - 24 = -8 -> 256.0 * 2^-8 = 1.0
        float resultMismatch = VulkanVideoPresenter.ConvertPolygonOffsetConstantFactor(
            256.0f,
            negNumDbBits: -24,
            dbIsFloatFmt: false,
            Format.D16Unorm);
        Assert(Math.Abs(resultMismatch - 1.0f) < 1e-6f, "D24 on D16 host should scale by 2^-8");

        Console.WriteLine("  [PASS] TestConvertPolygonOffsetConstantFactor");
    }

    private static void TestDecodeDepthStateBiasAndStencil()
    {
        var registers = new Dictionary<uint, uint>
        {
            // PaSuScModeCntl: bit 11 (poly_offset_front_enable) | bit 12 (poly_offset_back_enable)
            [0x205] = (1u << 11) | (1u << 12),
            // PaSuPolyOffsetClamp: float 0.5f
            [0x2DF] = BitConverter.SingleToUInt32Bits(0.5f),
            // PaSuPolyOffsetFrontScale: float 16.0f -> slopeFactor = 16.0 / 16.0 = 1.0f
            [0x2E0] = BitConverter.SingleToUInt32Bits(16.0f),
            // PaSuPolyOffsetFrontOffset: float 2.5f
            [0x2E1] = BitConverter.SingleToUInt32Bits(2.5f),
            // PaSuPolyOffsetDbFmtCntl: bit 8 (DB_IS_FLOAT_FMT) = 1
            [0x2DE] = 1u << 8,
            // DbStencilRefMask: ref=0x42, mask=0xFE, writeMask=0x7F
            [0x10C] = 0x42u | (0xFEu << 8) | (0x7Fu << 16)
        };

        var depthState = AgcExports.DecodeDepthState(registers);

        Assert(depthState.DepthBiasEnable, "DepthBiasEnable should be true when PaSuScModeCntl has poly offset bits set");
        Assert(Math.Abs(depthState.DepthBiasSlopeFactor - 1.0f) < 1e-6f, "DepthBiasSlopeFactor should equal scale / 16.0f");
        Assert(Math.Abs(depthState.DepthBiasConstantFactor - 2.5f) < 1e-6f, "DepthBiasConstantFactor should equal frontOffset in float format");
        Assert(Math.Abs(depthState.DepthBiasClamp - 0.5f) < 1e-6f, "DepthBiasClamp should match PaSuPolyOffsetClamp");
        Assert(depthState.StencilReference == 0x42u, "StencilReference should match ref byte in DbStencilRefMask");
        Assert(depthState.StencilCompareMask == 0xFEu, "StencilCompareMask should match mask byte in DbStencilRefMask");
        Assert(depthState.StencilWriteMask == 0x7Fu, "StencilWriteMask should match write mask byte in DbStencilRefMask");

        Console.WriteLine("  [PASS] TestDecodeDepthStateBiasAndStencil");
    }

    private static void TestGetBaseVertexUserDataFallback()
    {
        var empty = new Dictionary<uint, uint>();

        // Case 1: GeIndxOffset register (0x24A) is non-zero -> returns GeIndxOffset directly
        var ucRegs1 = new Dictionary<uint, uint>
        {
            [0x24A] = 42u // GeIndxOffset
        };
        Assert(AgcExports.GetBaseVertex(ucRegs1, empty) == 42, "Expected GeIndxOffset register value when present");

        // Case 2: GeIndxOffset is zero or absent, but GsUserDataRegister + 8 has base vertex in shRegisters
        var shRegs2 = new Dictionary<uint, uint>
        {
            [0x8C + 8] = 128u // GsUserDataRegister base (0x8C) + NggUserDataScalarRegisterBase (8)
        };
        Assert(AgcExports.GetBaseVertex(empty, shRegs2) == 128, "Expected base vertex resolved from GsUserDataRegister fallback");

        Console.WriteLine("  [PASS] TestGetBaseVertexUserDataFallback");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception($"[ASSERTION FAILED] {message}");
        }
    }
}
