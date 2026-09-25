// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.ShaderCompiler.Resources;

public sealed partial class ResourceTracker
{
    private const uint DenseIndirectImageShift = 5;
    private const uint MaxDenseIndirectImageEntries = 4096;

    private bool TryMakeDenseIndirectImage(ScalarValue handle, uint pc, out IndirectImagePlan plan)
    {
        plan = null!;
        if (handle.Kind != ScalarValueKind.ImageHandle || handle.Operands.Length != 8)
            return false;

        var reads = new ScalarValue[8];
        var memoryIndices = new int[8];
        ScalarValue? heapHandle = null;
        ScalarValue? based = null;
        uint tableImmediate = 0;
        var canSuppressMemoryReads = true;

        for (var dword = 0; dword < 8; dword++)
        {
            var read = handle.Operands[dword];
            if (read.Kind != ScalarValueKind.ScalarAddressWord || read.MemoryIndex < 0 ||
                read.MemoryIndex >= _plan.Memory.Count || !MemoryIndexBelongsTo(read.MemoryIndex, read))
                return false;

            var memory = _plan.Memory[read.MemoryIndex];
            if (memory.Kind != MemoryResourceKind.ScalarAddress || memory.DataBits != 32 || memory.DataDwords != 1)
                return false;

            var offset = read.Operands[1];
            uint extra = 0;
            if (based is null)
            {
                based = offset;
            }
            else if (!_graph.Equivalent(offset, based))
            {
                if (offset.Kind != ScalarValueKind.Operation || offset.Operation != ScalarOperation.IAdd32 || offset.Operands.Length != 2)
                    return false;

                ScalarValue inner;
                if (offset.Operands[1].IsConstant)
                {
                    extra = offset.Operands[1].ConstantU32;
                    inner = offset.Operands[0];
                }
                else if (offset.Operands[0].IsConstant)
                {
                    extra = offset.Operands[0].ConstantU32;
                    inner = offset.Operands[1];
                }
                else
                {
                    return false;
                }

                if (!_graph.Equivalent(inner, based))
                    return false;
            }

            var componentOffset = checked((uint)dword * sizeof(uint));
            if ((ulong)extra + memory.Offset < componentOffset)
                return false;
            var immediate = extra + memory.Offset - componentOffset;
            if (dword == 0)
                tableImmediate = immediate;
            else if (immediate != tableImmediate)
                return false;

            var currentHandle = read.Operands[0];
            if (currentHandle.Kind != ScalarValueKind.AddressHandle ||
                (heapHandle is not null && !_graph.Equivalent(currentHandle, heapHandle)))
                return false;

            heapHandle = currentHandle;
            reads[dword] = read;
            memoryIndices[dword] = read.MemoryIndex;
            canSuppressMemoryReads &= HasOnlyImageConsumers(memory, handle);
        }

        if (based is null || heapHandle is null)
            return false;

        uint tableOffset = tableImmediate;
        var scaled = based;
        if (scaled.Kind == ScalarValueKind.Operation && scaled.Operation == ScalarOperation.IAdd32 && scaled.Operands.Length == 2)
        {
            if (scaled.Operands[1].IsConstant)
            {
                tableOffset = unchecked(tableOffset + scaled.Operands[1].ConstantU32);
                scaled = scaled.Operands[0];
            }
            else if (scaled.Operands[0].IsConstant)
            {
                tableOffset = unchecked(tableOffset + scaled.Operands[0].ConstantU32);
                scaled = scaled.Operands[1];
            }
            else
            {
                return false;
            }
        }

        if (scaled.Kind != ScalarValueKind.Operation || scaled.Operation != ScalarOperation.ShiftLeft32 ||
            scaled.Operands.Length != 2 || !scaled.Operands[1].IsConstant ||
            scaled.Operands[1].ConstantU32 != DenseIndirectImageShift)
            return false;

        var key = scaled.Operands[0];
        var bound = DenseKeyBound(key);
        if (bound == 0)
            return false;

        foreach (var read in reads)
            if (!UsesOnly(read, [handle]))
                return false;

        if (!MakeRuntimeAddressSource(heapHandle, pc, out var heapSourceIndex, out var heapSource))
            return false;

        var imageDwords = Enumerable.Repeat(key, 8).ToArray();
        imageDwords[0] = heapSource.Dwords[0];
        imageDwords[1] = heapSource.Dwords[1];
        var imageSource = new DescriptorSource
        {
            Dwords = imageDwords,
            IndirectImage = new IndirectImageSelector(0, heapSourceIndex, 0, 0, 0)
            {
                Dense = true,
                TableOffset = tableOffset,
                DynamicOffsetBase = unchecked(tableOffset - tableImmediate),
                KeyBound = bound,
            },
        };

        plan = new IndirectImagePlan
        {
            Handle = handle,
            Source = InternSource(imageSource),
            Key = reads[0],
            KeyIsAddressOffset = true,
            HeapSource = heapSourceIndex,
            SuppressMemoryReads = canSuppressMemoryReads,
            Memory = memoryIndices,
            Reads = reads,
        };
        return true;
    }

    private uint DenseKeyBound(ScalarValue key)
    {
        if (key.Kind == ScalarValueKind.Operation && key.Operation == ScalarOperation.FindLowestBit32 &&
            _graph.HasNonZeroBitScanInput(key))
            return 32;

        if (!_uses.TryGetValue(key, out var uses))
            return 0;

        foreach (var use in uses)
        {
            if (use.Kind != ScalarValueKind.Operation || use.Operation != ScalarOperation.ULessThan32 || use.Operands.Length != 2 ||
                !ReferenceEquals(use.Operands[0], key) || !use.Operands[1].IsConstant)
                continue;

            var limit = use.Operands[1].ConstantU32;
            if (limit is > 0 and <= MaxDenseIndirectImageEntries)
                return limit;
        }

        return 0;
    }
}
