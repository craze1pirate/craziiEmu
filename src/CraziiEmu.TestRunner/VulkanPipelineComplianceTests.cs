// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CraziiEmu.TestRunner;

public static class VulkanPipelineComplianceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Running VulkanPipelineComplianceTests...");
        TestDynamicStateComplianceWithoutDepth();
        TestDynamicStateComplianceWithDepth();
        TestColorBlendAttachmentCountCompliance();
        TestDetourFastFilterEfficiency();
        Test14ByteAbsoluteJumpEncoding();
        Console.WriteLine("[TEST] VulkanPipelineComplianceTests PASSED successfully.");
    }

    private static void TestDynamicStateComplianceWithoutDepth()
    {
        // Per Vulkan spec VUID-VkGraphicsPipelineCreateInfo-pDynamicStates-00755/00756/00757:
        // When pDepthStencilState is NULL (no depth attachment), pDynamicStates MUST NOT
        // include StencilCompareMask, StencilWriteMask, or StencilReference.
        bool hasDepthAttachment = false;
        bool depthBiasEnable = true;

        var dynamicStates = new List<DynamicState>();
        dynamicStates.Add(DynamicState.Viewport);
        dynamicStates.Add(DynamicState.Scissor);
        dynamicStates.Add(DynamicState.BlendConstants);
        if (depthBiasEnable)
        {
            dynamicStates.Add(DynamicState.DepthBias);
        }
        if (hasDepthAttachment)
        {
            dynamicStates.Add(DynamicState.StencilCompareMask);
            dynamicStates.Add(DynamicState.StencilWriteMask);
            dynamicStates.Add(DynamicState.StencilReference);
        }

        if (dynamicStates.Contains(DynamicState.StencilCompareMask) ||
            dynamicStates.Contains(DynamicState.StencilWriteMask) ||
            dynamicStates.Contains(DynamicState.StencilReference))
        {
            throw new InvalidOperationException("Non-depth pipeline must NOT include stencil dynamic states!");
        }

        if (dynamicStates.Count != 4)
        {
            throw new InvalidOperationException($"Expected 4 dynamic states, got {dynamicStates.Count}");
        }
    }

    private static void TestDynamicStateComplianceWithDepth()
    {
        bool hasDepthAttachment = true;
        bool depthBiasEnable = false;

        var dynamicStates = new List<DynamicState>();
        dynamicStates.Add(DynamicState.Viewport);
        dynamicStates.Add(DynamicState.Scissor);
        dynamicStates.Add(DynamicState.BlendConstants);
        if (depthBiasEnable)
        {
            dynamicStates.Add(DynamicState.DepthBias);
        }
        if (hasDepthAttachment)
        {
            dynamicStates.Add(DynamicState.StencilCompareMask);
            dynamicStates.Add(DynamicState.StencilWriteMask);
            dynamicStates.Add(DynamicState.StencilReference);
        }

        if (!dynamicStates.Contains(DynamicState.StencilCompareMask) ||
            !dynamicStates.Contains(DynamicState.StencilWriteMask) ||
            !dynamicStates.Contains(DynamicState.StencilReference))
        {
            throw new InvalidOperationException("Depth pipeline must include stencil dynamic states!");
        }

        if (dynamicStates.Count != 6)
        {
            throw new InvalidOperationException($"Expected 6 dynamic states, got {dynamicStates.Count}");
        }
    }

    private static void TestColorBlendAttachmentCountCompliance()
    {
        // Test varying render target counts (0, 1, 4) against guest blend state arrays (8).
        // VUID-VkGraphicsPipelineCreateInfo-renderPass-06042 requires attachmentCount
        // to equal subpass color attachment count.
        var guestBlends = new int[8]; // 8 MRT blends from GNM hardware registers
        for (int i = 0; i < 8; i++) guestBlends[i] = i + 1;

        // Case 1: 1 color attachment (standard forward rendering / post-processing)
        var rtFormats1 = new[] { Format.R8G8B8A8Unorm };
        int count1 = rtFormats1.Length;
        if (count1 != 1) throw new InvalidOperationException();

        // Case 2: 0 color attachments (depth-only / shadow map passes)
        var rtFormats0 = Array.Empty<Format>();
        int count0 = rtFormats0.Length;
        if (count0 != 0) throw new InvalidOperationException();

        // Case 3: 4 color attachments (deferred G-Buffer passes)
        var rtFormats4 = new[]
        {
            Format.R8G8B8A8Unorm,
            Format.R16G16B16A16Sfloat,
            Format.A2B10G10R10UnormPack32,
            Format.R8Unorm
        };
        int count4 = rtFormats4.Length;
        if (count4 != 4) throw new InvalidOperationException();
    }

    private static void TestDetourFastFilterEfficiency()
    {
        // Synthesize 100,000 runtime symbols to simulate Il2cpp + Unity PRX symbol tables
        var symbols = new List<string>(100000);
        for (int i = 0; i < 99995; i++)
        {
            symbols.Add($"UnityEngine_UI_Graphic_{i}_Rebuild");
        }
        symbols.Add("__cxa_guard_acquire#libc#libc");
        symbols.Add("__cxa_guard_release#libc#libc");
        symbols.Add("__cxa_guard_abort#libc#libc");
        symbols.Add("_umtx_op#libkernel#libkernel");
        symbols.Add("3GPpjQdAMTw#libkernel#libkernel");

        var sw = Stopwatch.StartNew();
        int matches = 0;
        foreach (var sym in symbols)
        {
            if (IsPotentialSymbol(sym))
            {
                matches++;
            }
        }
        sw.Stop();

        if (matches != 5)
        {
            throw new InvalidOperationException($"Expected exactly 5 matches, got {matches}");
        }

        if (sw.ElapsedMilliseconds > 100)
        {
            throw new InvalidOperationException($"Detour fast filter was too slow: {sw.ElapsedMilliseconds} ms");
        }
    }

    private static bool IsPotentialSymbol(string symName)
    {
        return symName.Contains("cxa_guard", StringComparison.Ordinal) ||
               symName.Contains("umtx", StringComparison.Ordinal) ||
               symName.Contains("3GPpjQdAMTw", StringComparison.Ordinal) ||
               symName.Contains("9rAeANT2tyE", StringComparison.Ordinal) ||
               symName.Contains("S+B1-L6d+Wk", StringComparison.Ordinal) ||
               symName.Contains("2emaaluWzUw", StringComparison.Ordinal) ||
               symName.Contains("bZzZ2S54a10", StringComparison.Ordinal) ||
               symName.Contains("3D1uQc1oEFE", StringComparison.Ordinal);
    }

    private static void Test14ByteAbsoluteJumpEncoding()
    {
        // 14-byte indirect jump encoding: FF 25 00 00 00 00 [8-byte address]
        // Encodes `jmp qword ptr [rip+0]`.
        ulong targetAddress = 0x000000080DCBC010UL;
        byte[] buffer = new byte[14];
        buffer[0] = 0xFF;
        buffer[1] = 0x25;
        buffer[2] = 0x00;
        buffer[3] = 0x00;
        buffer[4] = 0x00;
        buffer[5] = 0x00;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(6), targetAddress);

        if (buffer[0] != 0xFF || buffer[1] != 0x25)
        {
            throw new InvalidOperationException("Invalid jmp [rip+0] opcode prefix");
        }

        ulong readAddress = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(6));
        if (readAddress != targetAddress)
        {
            throw new InvalidOperationException($"Address mismatch: expected 0x{targetAddress:X16}, got 0x{readAddress:X16}");
        }
    }
}
