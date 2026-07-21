// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;

namespace CraziiEmu.Core.Loader;

public class VmmAdapter : IVirtualMemory, IGuestMemoryAllocator
{
    private ulong _guestAllocationArenaBase;
    private ulong _guestAllocationOffset;
    private readonly object _guestAllocationGate = new();

    private readonly VirtualMemoryManager _vmm;

    public VmmAdapter(VirtualMemoryManager vmm)
    {
        _vmm = vmm;
    }

    public void Clear()
    {
    }

    public void Map(ulong virtualAddress, ulong memorySize, ulong fileOffset, ReadOnlySpan<byte> fileData, ProgramHeaderFlags protection)
    {
        if (virtualAddress >= 64UL * 1024 * 1024 * 1024)
        {
            _vmm.MapHighAddress(virtualAddress, memorySize);
        }

        // Force commit the pages
        var span = _vmm.GetSpan(virtualAddress, (int)memorySize);
        if (fileData.Length > 0)
        {
            fileData.CopyTo(span);
        }
    }

    public IReadOnlyList<VirtualMemoryRegion> SnapshotRegions()
    {
        return Array.Empty<VirtualMemoryRegion>();
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        try
        {
            _vmm.GetSpan(address, destination.Length).CopyTo(destination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
    {
        try
        {
            source.CopyTo(_vmm.GetSpan(address, source.Length));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryAllocateGuestMemory(ulong size, ulong alignment, out ulong address)
    {
        address = 0;
        if (size == 0 || alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            return false;
        }

        lock (_guestAllocationGate)
        {
            if (_guestAllocationArenaBase == 0)
            {
                _guestAllocationArenaBase = 0x600000000000;
                _vmm.MapHighAddress(_guestAllocationArenaBase, 1024 * 1024 * 1024);
                _guestAllocationOffset = 0;
            }

            var alignedOffset = (_guestAllocationOffset + alignment - 1) & ~(alignment - 1);
            if (alignedOffset > 1024 * 1024 * 1024 || size > 1024 * 1024 * 1024 - alignedOffset)
            {
                return false;
            }

            address = _guestAllocationArenaBase + alignedOffset;
            _guestAllocationOffset = alignedOffset + size;
            return true;
        }
    }

    public bool TryFreeGuestMemory(ulong address)
    {
        return true;
    }

    public bool TryReadString(ulong address, int maxLength, out string value)
    {
        value = string.Empty;
        try
        {
            var span = _vmm.GetSpan(address, maxLength);
            int length = span.IndexOf((byte)0);
            if (length == -1) length = maxLength;
            value = System.Text.Encoding.UTF8.GetString(span.Slice(0, length));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RegisterFaultHandler(ulong start, ulong end, Action<CpuMemoryFaultInfo> handler) { }
    public void UnregisterFaultHandler(ulong start, ulong end) { }
    public ulong? GetPhysicalAddress(ulong virtualAddress) => virtualAddress;
}
