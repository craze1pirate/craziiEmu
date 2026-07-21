// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Runtime.InteropServices;
using System.Threading;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;

namespace CraziiEmu.Core.HLE;

/// <summary>
/// High-Level Emulation (HLE) module for libkernel.sprx core system calls.
/// </summary>
public class LibKernelHle
{
    private readonly VirtualMemoryManager _vmm;
    private readonly DynamicLinker _linker;

    // Retain delegates to prevent garbage collection
    private readonly UsleepDelegate _usleepDelegate;
    private readonly GetTimeOfDayDelegate _getTimeOfDayDelegate;
    private readonly AllocateDirectMemoryDelegate _allocateDirectMemoryDelegate;

    /// <summary>
    /// Delegate signature for sceKernelUsleep.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UsleepDelegate(uint microseconds);

    /// <summary>
    /// Delegate signature for sceKernelGettimeofday.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GetTimeOfDayDelegate(ulong timeval_addr, ulong timezone_addr);

    /// <summary>
    /// Delegate signature for sceKernelAllocateDirectMemory.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int AllocateDirectMemoryDelegate(ulong search_start, ulong search_end, ulong size, ulong alignment, int memory_type, ulong physical_addr_out);

    /// <summary>
    /// Initializes a new instance of the <see cref="LibKernelHle"/> class.
    /// </summary>
    /// <param name="vmm">The virtual memory manager instance.</param>
    /// <param name="linker">The dynamic linker instance.</param>
    public LibKernelHle(VirtualMemoryManager vmm, DynamicLinker linker)
    {
        _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));
        _linker = linker ?? throw new ArgumentNullException(nameof(linker));

        _usleepDelegate = Usleep;
        _getTimeOfDayDelegate = GetTimeOfDay;
        _allocateDirectMemoryDelegate = AllocateDirectMemory;

        RegisterStub("sceKernelUsleep", _usleepDelegate);
        RegisterStub("sceKernelGettimeofday", _getTimeOfDayDelegate);
        RegisterStub("sceKernelAllocateDirectMemory", _allocateDirectMemoryDelegate);
    }

    private void RegisterStub(string symbolName, Delegate handler)
    {
        ulong address = (ulong)Marshal.GetFunctionPointerForDelegate(handler);
        _linker.RegisterHleStub("libkernel.sprx", symbolName, address);
    }

    private void Usleep(uint microseconds)
    {
        Thread.Sleep((int)(microseconds / 1000));
    }

    private int GetTimeOfDay(ulong timeval_addr, ulong timezone_addr)
    {
        var now = DateTimeOffset.UtcNow;
        ulong seconds = (ulong)now.ToUnixTimeSeconds();
        uint microseconds = (uint)(now.Millisecond * 1000);

        if (timeval_addr != 0)
        {
            var span = _vmm.GetSpan(timeval_addr, 12);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(0, 8), seconds);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), microseconds);
        }

        return 0; // Success
    }

    private int AllocateDirectMemory(ulong search_start, ulong search_end, ulong size, ulong alignment, int memory_type, ulong physical_addr_out)
    {
        ulong allocatedAddress = _vmm.AllocateCpu(size);
        
        if (physical_addr_out != 0)
        {
            var span = _vmm.GetSpan(physical_addr_out, 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span, allocatedAddress);
        }

        return 0; // Success
    }
}
