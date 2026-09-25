// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.ShaderCompiler;
using CraziiEmu.ShaderCompiler.Resources;

namespace CraziiEmu.ShaderCompiler.Vulkan;

public static partial class Gen5SpirvTranslator
{
    public static bool TryCompilePixelShader(
        Gen5ShaderState state,
        Gen5ShaderEvaluation evaluation,
        Gen5PixelOutputKind outputKind,
        out Gen5SpirvShader shader,
        out string error,
        int globalBufferBase = 0,
        int totalGlobalBufferCount = -1,
        int imageBindingBase = 0,
        int initialScalarBufferIndex = -1,
        int pixelRenderTargetSlot = 0,
        uint pixelInputEnable = 0,
        uint pixelInputAddress = 0,
        IReadOnlyList<uint>? pixelInputCntl = null,
        uint pixelInputNum = 32,
        ulong storageBufferOffsetAlignment = 1) =>
        TryCompilePixelShader(
            state,
            evaluation,
            [new Gen5PixelOutputBinding((uint)pixelRenderTargetSlot, 0, outputKind)],
            out shader,
            out error,
            globalBufferBase,
            totalGlobalBufferCount,
            imageBindingBase,
            initialScalarBufferIndex,
            pixelInputEnable,
            pixelInputAddress,
            pixelInputCntl,
            pixelInputNum,
            storageBufferOffsetAlignment);

    public static bool TryCompilePixelShader(
        Gen5ShaderState state,
        Gen5ShaderEvaluation evaluation,
        IReadOnlyList<Gen5PixelOutputBinding> outputs,
        out Gen5SpirvShader shader,
        out string error,
        int globalBufferBase = 0,
        int totalGlobalBufferCount = -1,
        int imageBindingBase = 0,
        int initialScalarBufferIndex = -1,
        uint pixelInputEnable = 0,
        uint pixelInputAddress = 0,
        IReadOnlyList<uint>? pixelInputCntl = null,
        uint pixelInputNum = 32,
        ulong storageBufferOffsetAlignment = 1)
    {
        if (outputs.Count > 8 || outputs.Any(output => output.GuestSlot > 7))
        {
            shader = default!;
            error = "pixel outputs must contain at most eight guest slots in the 0..7 range";
            return false;
        }

        if (outputs.Select(output => output.GuestSlot).Distinct().Count() != outputs.Count ||
            outputs.Select(output => output.HostLocation).Distinct().Count() != outputs.Count)
        {
            shader = default!;
            error = "pixel output guest slots and host locations must be unique";
            return false;
        }

        if (!outputs
                .OrderBy(output => output.HostLocation)
                .Select((output, index) => output.HostLocation == (uint)index)
                .All(isDense => isDense))
        {
            shader = default!;
            error = "pixel output host locations must be dense in the 0..N-1 range";
            return false;
        }

        try
        {
            var plan = ShaderResourcePlan.Extract(state.Program, ShaderStage.Pixel, state.Program.Address, state.UserDataScalarRegisterBase, (uint)state.UserData.Count);
            var resources = ResourceMaterializer.ApplyTo(plan, ResourceSpecialization.Default(plan.Info));
            var layout = BindingLayout.Allocate(
                resources.Info,
                BindingLayout.CollectUserDataRegisters(state.Program, state.UserDataScalarRegisterBase, (uint)state.UserData.Count),
                BindingLayout.UsesGlobalDataShare(state.Program),
                ShaderCompileRequest.RequiresFlattenedTable(plan, resources),
                BindingLayout.ReadsShaderBase(state.Program));

            var request = new ShaderCompileRequest(plan, resources, layout)
            {
                PixelOutputs = outputs,
                PixelInputEnable = pixelInputEnable,
                PixelInputAddress = pixelInputAddress,
                PixelInputCntl = pixelInputCntl,
            };

            return TryCompileProgram(request, out shader, out error);
        }
        catch (Exception ex)
        {
            shader = default!;
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCompileVertexShader(
        Gen5ShaderState state,
        Gen5ShaderEvaluation evaluation,
        out Gen5SpirvShader shader,
        out string error,
        int globalBufferBase = 0,
        int totalGlobalBufferCount = -1,
        int imageBindingBase = 0,
        int initialScalarBufferIndex = -1,
        int requiredVertexOutputCount = 0,
        ulong storageBufferOffsetAlignment = 1)
    {
        try
        {
            var plan = ShaderResourcePlan.Extract(state.Program, ShaderStage.Vertex, state.Program.Address, state.UserDataScalarRegisterBase, (uint)state.UserData.Count);
            var resources = ResourceMaterializer.ApplyTo(plan, ResourceSpecialization.Default(plan.Info));
            var layout = BindingLayout.Allocate(
                resources.Info,
                BindingLayout.CollectUserDataRegisters(state.Program, state.UserDataScalarRegisterBase, (uint)state.UserData.Count),
                BindingLayout.UsesGlobalDataShare(state.Program),
                ShaderCompileRequest.RequiresFlattenedTable(plan, resources),
                BindingLayout.ReadsShaderBase(state.Program));

            var request = new ShaderCompileRequest(plan, resources, layout)
            {
                RequiredVertexOutputCount = requiredVertexOutputCount,
            };

            return TryCompileProgram(request, out shader, out error);
        }
        catch (Exception ex)
        {
            shader = default!;
            error = ex.Message;
            return false;
        }
    }

    public static bool TryCompileComputeShader(
        Gen5ShaderState state,
        Gen5ShaderEvaluation evaluation,
        uint localSizeX,
        uint localSizeY,
        uint localSizeZ,
        out Gen5SpirvShader shader,
        out string error,
        int totalGlobalBufferCount = -1,
        int initialScalarBufferIndex = -1,
        uint waveLaneCount = 32,
        ulong storageBufferOffsetAlignment = 1)
    {
        try
        {
            var plan = ShaderResourcePlan.Extract(state.Program, ShaderStage.Compute, state.Program.Address, state.UserDataScalarRegisterBase, (uint)state.UserData.Count);
            var resources = ResourceMaterializer.ApplyTo(plan, ResourceSpecialization.Default(plan.Info));
            var layout = BindingLayout.Allocate(
                resources.Info,
                BindingLayout.CollectUserDataRegisters(state.Program, state.UserDataScalarRegisterBase, (uint)state.UserData.Count),
                BindingLayout.UsesGlobalDataShare(state.Program),
                ShaderCompileRequest.RequiresFlattenedTable(plan, resources),
                BindingLayout.ReadsShaderBase(state.Program));

            var request = new ShaderCompileRequest(plan, resources, layout)
            {
                LocalSizeX = Math.Max(localSizeX, 1),
                LocalSizeY = Math.Max(localSizeY, 1),
                LocalSizeZ = Math.Max(localSizeZ, 1),
                WaveSize = waveLaneCount,
                ComputeSystemRegisters = state.ComputeSystemRegisters,
            };

            return TryCompileProgram(request, out shader, out error);
        }
        catch (Exception ex)
        {
            shader = default!;
            error = ex.Message;
            return false;
        }
    }

    public static uint GetPixelParameterLocation(uint attribute, IReadOnlyList<uint>? pixelInputCntl) =>
        pixelInputCntl is not null && attribute < pixelInputCntl.Count
            ? pixelInputCntl[(int)attribute] & 0x1Fu
            : attribute;

    public static bool IsPixelParameterFlat(uint attribute, IReadOnlyList<uint>? pixelInputCntl) =>
        pixelInputCntl is not null && attribute < pixelInputCntl.Count &&
        (pixelInputCntl[(int)attribute] & (1u << 5)) != 0;
}
