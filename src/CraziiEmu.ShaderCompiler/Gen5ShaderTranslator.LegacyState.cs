// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CraziiEmu.ShaderCompiler;

public static partial class Gen5ShaderTranslator
{
    /// <summary>
    /// Bitmask (256 bits) of scalar registers whose values the program can
    /// observe: scalar source operands (widened for 64-bit pairs), the
    /// descriptor/sampler/address ranges named by instruction controls, and
    /// the implicit state registers. Deterministic per instruction stream, so
    /// the SPIR-V that loads exactly these registers from the per-draw
    /// initial-state buffer is byte-stable across draws.
    /// </summary>
    public static ulong[] ComputeConsumedScalarMask(Gen5ShaderProgram program)
    {
        var mask = new ulong[4];
        AddConsumedScalar(mask, 106, 2);
        AddConsumedScalar(mask, 124, 2);
        AddConsumedScalar(mask, 126, 2);
        foreach (var instruction in program.Instructions)
        {
            foreach (var source in instruction.Sources)
            {
                if (source.Kind == Gen5OperandKind.ScalarRegister)
                {
                    AddConsumedScalar(mask, source.Value, 2);
                }
            }

            // Scalar memory bases can be a 4-dword buffer descriptor.
            if (instruction.Encoding is Gen5ShaderEncoding.Smem or Gen5ShaderEncoding.Smrd &&
                instruction.Sources.Count > 0 &&
                instruction.Sources[0].Kind == Gen5OperandKind.ScalarRegister)
            {
                AddConsumedScalar(mask, instruction.Sources[0].Value, 4);
            }

            switch (instruction.Control)
            {
                case Gen5ImageControl image:
                    AddConsumedScalar(mask, image.ScalarResource, 8);
                    AddConsumedScalar(mask, image.ScalarSampler, 4);
                    break;
                case Gen5ScalarMemoryControl { DynamicOffsetRegister: { } offsetRegister }:
                    AddConsumedScalar(mask, offsetRegister, 2);
                    break;
                case Gen5GlobalMemoryControl global:
                    AddConsumedScalar(mask, global.ScalarAddress, 2);
                    break;
                case Gen5BufferMemoryControl buffer:
                    AddConsumedScalar(mask, buffer.ScalarResource, 4);
                    break;
            }
        }

        return mask;
    }

    private static void AddConsumedScalar(ulong[] mask, uint register, uint count)
    {
        for (uint index = 0; index < count; index++)
        {
            var target = register + index;
            if (target < 256)
            {
                mask[target >> 6] |= 1UL << (int)(target & 63);
            }
        }
    }

    public static bool IsScalarConsumed(ulong[] mask, uint register) =>
        register < 256 && (mask[register >> 6] & (1UL << (int)(register & 63))) != 0;

    private const uint PsUserDataRegister = 0x0C;
    private const uint VsUserDataRegister = 0x4C;
    private const uint GsUserDataRegister = 0x8C;
    private const uint EsUserDataRegister = 0xCC;
    private const uint ComputeUserDataRegister = 0x240;
    private const uint ComputePgmRsrc2Register = 0x213;
    private const int MaximumHardwareUserSgprs = 64;
    private static readonly ConditionalWeakTable<object, ShaderDecodeCache> _decodeCaches = new();

    private sealed class ShaderDecodeCache
    {
        public object Gate { get; } = new();
        public Dictionary<ulong, Gen5ShaderProgram> Programs { get; } = new();
        public Dictionary<ulong, Gen5ShaderMetadata?> Metadata { get; } = new();
    }

    private static readonly uint[] FullscreenBarycentricEs =
    [
        0x7E020280, 0x7E000280, 0x7E060280, 0x7E040280,
        0x7E080281, 0xC0800300, 0x00000104, 0xBF810000,
    ];

    private static readonly uint[] Gen5RectListExportEs =
    [
        0x7E000280, 0x7E020280, 0xC4001A84, 0x00000100,
        0xC4001A03, 0x00000000, 0xC0800300, 0x00000100,
        0xBF810000,
    ];

    private static readonly uint[] Gen5QuadExportEs =
    [
        0x7E000280, 0x7E020280, 0xC4001A84, 0x00000100,
        0xC4001A03, 0x00000000, 0xC0800300, 0x00000100,
        0xBF810000,
    ];

    private static readonly uint[] FullscreenBarycentricPs =
    [
        0x7E0002F2, 0x7E020280, 0xD5690005, 0x000208C2,
        0xD7460003, 0x03050302, 0x7E040B02, 0x7E080B04,
        0x4A0A0A81, 0x7E060B03, 0x7E0A0B05, 0xF80008CF,
        0x00000503, 0xF800020F, 0x01010402, 0xBF810000,
    ];

    public static bool TryTranslate(
        CpuContext ctx,
        ulong exportShaderAddress,
        ulong pixelShaderAddress,
        uint psInputEna,
        uint psInputAddr,
        out GuestDrawKind drawKind)
    {
        drawKind = GuestDrawKind.None;
        if (exportShaderAddress == 0 ||
            pixelShaderAddress == 0 ||
            psInputEna != 0x00000002 ||
            psInputAddr != 0x00000002 ||
            !MatchesProgram(ctx, exportShaderAddress, FullscreenBarycentricEs) ||
            !MatchesProgram(ctx, pixelShaderAddress, FullscreenBarycentricPs))
        {
            return false;
        }

        drawKind = GuestDrawKind.FullscreenBarycentric;
        return true;
    }

    public static bool IsFullscreenExportShader(CpuContext ctx, ulong exportShaderAddress) =>
        exportShaderAddress != 0 &&
        (MatchesProgram(ctx, exportShaderAddress, FullscreenBarycentricEs) ||
         MatchesProgram(ctx, exportShaderAddress, Gen5RectListExportEs) ||
         MatchesProgram(ctx, exportShaderAddress, Gen5QuadExportEs));

    public static bool TryCreateState(
        CpuContext ctx,
        ulong shaderAddress,
        ulong shaderHeaderAddress,
        IReadOnlyDictionary<uint, uint> shaderRegisters,
        uint userDataBaseRegister,
        out Gen5ShaderState state,
        out string error,
        Gen5ComputeSystemRegisters? computeSystemRegisters = null,
        uint userDataScalarRegisterBase = 0)
    {
        ValidateUserSgprCountDecoding();
        state = default!;
        error = string.Empty;
        var cache = _decodeCaches.GetValue(ctx.Memory, static _ => new ShaderDecodeCache());
        Gen5ShaderProgram? program;
        lock (cache.Gate)
        {
            cache.Programs.TryGetValue(shaderAddress, out program);
        }

        if (program is null)
        {
            if (!TryDecodeProgram(ctx, shaderAddress, out program, out error))
            {
                return false;
            }

            lock (cache.Gate)
            {
                cache.Programs.TryAdd(shaderAddress, program);
            }
        }

        Gen5ShaderMetadata? metadata = null;
        if (shaderHeaderAddress != 0)
        {
            var metadataCached = false;
            lock (cache.Gate)
            {
                metadataCached = cache.Metadata.TryGetValue(shaderHeaderAddress, out metadata);
            }

            if (!metadataCached)
            {
                if (Gen5ShaderMetadataReader.TryRead(
                        ctx,
                        shaderHeaderAddress,
                        out var decodedMetadata))
                {
                    metadata = decodedMetadata;
                }

                lock (cache.Gate)
                {
                    cache.Metadata.TryAdd(shaderHeaderAddress, metadata);
                }
            }
        }

        if (!TryDecodeUserSgprCount(
                userDataBaseRegister,
                shaderRegisters,
                out var userSgprCount,
                out var rsrc2Register,
                out var rsrc2))
        {
            error = $"missing rsrc2 register 0x{rsrc2Register:X3}";
            return false;
        }

        var userData = new List<uint>(userSgprCount);
        for (uint index = 0; index < userSgprCount; index++)
        {
            var register = userDataBaseRegister + index;
            shaderRegisters.TryGetValue(register, out var value);
            userData.Add(value);
        }

        state = new Gen5ShaderState(
            program,
            userData,
            metadata,
            computeSystemRegisters,
            userDataScalarRegisterBase);
        return true;
    }

    private static bool TryDecodeUserSgprCount(
        uint userDataBaseRegister,
        IReadOnlyDictionary<uint, uint> shaderRegisters,
        out int count,
        out uint rsrc2Register,
        out uint rsrc2)
    {
        rsrc2Register = userDataBaseRegister == ComputeUserDataRegister
            ? ComputePgmRsrc2Register
            : userDataBaseRegister - 1;
        if (!shaderRegisters.TryGetValue(rsrc2Register, out rsrc2))
        {
            count = 0;
            return false;
        }

        count = checked((int)((rsrc2 >> 1) & 0x1Fu));
        // GFX10 PS/VS/GS expose a sixth USER_SGPR bit. ES and compute do not.
        // AGC's logical user-data layout (including its SRT pointer and back
        // user data) describes memory reached through these SGPRs; it does not
        // increase the hardware register window.
        var hasUserSgprMsb = userDataBaseRegister is
            PsUserDataRegister or VsUserDataRegister or GsUserDataRegister;
        if (hasUserSgprMsb && (rsrc2 & (1u << 27)) != 0)
        {
            count |= 1 << 5;
        }

        if (!(userDataBaseRegister is
                PsUserDataRegister or
                VsUserDataRegister or
                GsUserDataRegister or
                EsUserDataRegister or
                ComputeUserDataRegister) ||
            count > MaximumHardwareUserSgprs)
        {
            count = 0;
            return false;
        }

        return true;
    }

    private static void ValidateUserSgprCountDecoding()
    {
        static int Decode(uint register, uint rsrc2)
        {
            var registers = new Dictionary<uint, uint>();
            var rsrc2Register = register == ComputeUserDataRegister
                ? ComputePgmRsrc2Register
                : register - 1;
            registers[rsrc2Register] = rsrc2;
            return TryDecodeUserSgprCount(register, registers, out var count, out _, out _)
                ? count
                : -1;
        }

        Debug.Assert(Decode(PsUserDataRegister, 0) == 0);
        Debug.Assert(Decode(PsUserDataRegister, 5u << 1) == 5);
        Debug.Assert(Decode(PsUserDataRegister, (5u << 1) | (1u << 27)) == 37);
        Debug.Assert(Decode(VsUserDataRegister, (2u << 1) | (1u << 27)) == 34);
        Debug.Assert(Decode(GsUserDataRegister, (4u << 1) | (1u << 27)) == 36);
        Debug.Assert(Decode(EsUserDataRegister, (7u << 1) | (1u << 27)) == 7);
        Debug.Assert(Decode(ComputeUserDataRegister, (11u << 1) | (1u << 27)) == 11);
    }

    public static string DescribeState(Gen5ShaderState state)
    {
        var userData = string.Join(
            ',',
            state.UserData.Select((value, index) => $"s{index}=0x{value:X8}"));
        var systemRegisters = state.ComputeSystemRegisters is { } compute
            ? $" compute[{DescribeComputeSystemRegisters(compute)}]"
            : string.Empty;
        if (state.Metadata is not { } metadata)
        {
            return
                $"ud_base=s{state.UserDataScalarRegisterBase} hw_ud={state.UserData.Count} " +
                $"ud[{userData}]" +
                $"{systemRegisters} metadata=missing";
        }

        var direct = string.Join(
            ',',
            metadata.DirectResources.Select(resource => $"{resource.Key}:{resource.Value}"));
        var resources = string.Join(
            ',',
            metadata.Resources.Select(resource =>
                $"{resource.Kind}[{resource.Slot}]@{resource.OffsetDwords}" +
                (resource.SizeFlag ? "+" : string.Empty)));
        return
            $"ud_base=s{state.UserDataScalarRegisterBase} hw_ud={state.UserData.Count} " +
            $"ud[{userData}]" +
            $"{systemRegisters} metadata[eud={metadata.ExtendedUserDataSizeDwords}," +
            $"srt={metadata.ShaderResourceTableSizeDwords},direct={direct},resources={resources}]";
    }

    private static string DescribeComputeSystemRegisters(Gen5ComputeSystemRegisters registers) =>
        $"x={DescribeRegister(registers.WorkGroupXRegister)}," +
        $"y={DescribeRegister(registers.WorkGroupYRegister)}," +
        $"z={DescribeRegister(registers.WorkGroupZRegister)}," +
        $"size={DescribeRegister(registers.ThreadGroupSizeRegister)}";

    private static string DescribeRegister(uint? register) =>
        register.HasValue ? $"s{register.Value}" : "-";

    private static bool MatchesProgram(CpuContext ctx, ulong address, ReadOnlySpan<uint> expected)
    {
        var bytes = new byte[expected.Length * sizeof(uint)];
        if (!ctx.Memory.TryRead(address, bytes))
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint))) != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    public static bool RequiresStorageImage(
        Gen5ImageBinding binding,
        IReadOnlyList<Gen5ImageBinding> stageBindings)
    {
        if (IsStorageImageOperation(binding.Opcode))
        {
            return true;
        }

        if (!IsImageLoadOperation(binding.Opcode))
        {
            return false;
        }

        // IMAGE_LOAD itself is read-only and maps naturally to OpImageFetch,
        // including for block-compressed textures which Vulkan cannot expose
        // as storage images. Keep it as storage only when the same resolved
        // descriptor is also written in this shader stage, preserving coherent
        // read/write access through one storage-image representation.
        return stageBindings.Any(candidate =>
            IsStorageImageOperation(candidate.Opcode) &&
            binding.ResourceDescriptor.SequenceEqual(candidate.ResourceDescriptor));
    }

    public static bool IsArrayedImageBinding(Gen5ImageBinding binding)
    {
        var type = binding.ResourceDescriptor.Count >= 4
            ? (binding.ResourceDescriptor[3] >> 28) & 0xFu
            : 0u;
        var isArrayType = type is 12 or 13; // kColor1DArray, kColor2DArray
        return (binding.Control.IsArray || isArrayType) &&
            (binding.Opcode.StartsWith("ImageSample", StringComparison.Ordinal) ||
             binding.Opcode.StartsWith("ImageGather4", StringComparison.Ordinal));
    }
}
