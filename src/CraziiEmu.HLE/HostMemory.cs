// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.HLE;

/// <summary>
/// Host virtual memory API forwarding directly to Win32 kernel32.
/// </summary>
public static unsafe class HostMemory
{
    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint MEM_DECOMMIT = 0x4000;
    public const uint MEM_RELEASE = 0x8000;
    public const uint MEM_FREE_STATE = 0x10000;
    public const uint MEM_PRIVATE = 0x20000;

    public const uint PAGE_NOACCESS = 0x01;
    public const uint PAGE_READONLY = 0x02;
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_EXECUTE = 0x10;
    public const uint PAGE_EXECUTE_READ = 0x20;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;

    /// <summary>Win32 MEMORY_BASIC_INFORMATION (64-bit) layout.</summary>
    public struct BasicInfo
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint AllocationProtect;
        public uint Alignment1;
        public ulong RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
        public uint Alignment2;
    }

    public const uint MEM_REPLACE_PLACEHOLDER = 0x00004000;
    public const uint MEM_RESERVE_PLACEHOLDER = 0x00040000;
    public const uint MEM_PRESERVE_PLACEHOLDER = 0x00000002;

    private static readonly bool s_hasVirtualAlloc2 = CheckVirtualAlloc2Supported();

    public static bool IsVirtualAlloc2Supported => s_hasVirtualAlloc2;

    private static bool CheckVirtualAlloc2Supported()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (NativeLibrary.TryLoad("kernelbase.dll", out var handle))
        {
            try
            {
                return NativeLibrary.TryGetExport(handle, "VirtualAlloc2", out _);
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        return false;
    }

    public static void* Alloc2(
        void* address,
        nuint size,
        uint allocationType,
        uint protect,
        void* process = null)
    {
        if (s_hasVirtualAlloc2)
        {
            var targetProcess = process != null ? process : Win32GetCurrentProcess();
            return Win32VirtualAlloc2(targetProcess, address, size, allocationType, protect, null, 0);
        }
        return null;
    }

    public static bool FreeEx(
        void* address,
        nuint size,
        uint freeType,
        void* process = null)
    {
        var targetProcess = process != null ? process : Win32GetCurrentProcess();
        return Win32VirtualFreeEx(targetProcess, address, size, freeType);
    }

    public static void* Alloc(void* address, nuint size, uint allocationType, uint protect) =>
        Win32VirtualAlloc(address, size, allocationType, protect);

    public static bool Free(void* address, nuint size, uint freeType) =>
        Win32VirtualFree(address, size, freeType);

    public static bool Protect(void* address, nuint size, uint newProtect, out uint oldProtect) =>
        Win32VirtualProtect(address, size, newProtect, out oldProtect);

    public static nuint Query(void* address, out BasicInfo info) =>
        Win32VirtualQuery(address, out info, (nuint)sizeof(BasicInfo));

    public static void FlushInstructionCache(void* address, nuint size) =>
        Win32FlushInstructionCache(Win32GetCurrentProcess(), address, size);

    [DllImport("kernelbase.dll", EntryPoint = "VirtualAlloc2", SetLastError = true)]
    private static extern void* Win32VirtualAlloc2(
        void* Process,
        void* BaseAddress,
        nuint Size,
        uint AllocationType,
        uint PageProtection,
        void* ExtendedParameters,
        uint ParameterCount);

    [DllImport("kernel32.dll", EntryPoint = "VirtualFreeEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Win32VirtualFreeEx(
        void* hProcess,
        void* lpAddress,
        nuint dwSize,
        uint dwFreeType);

    [DllImport("kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
    private static extern void* Win32VirtualAlloc(void* lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Win32VirtualFree(void* lpAddress, nuint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", EntryPoint = "VirtualProtect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Win32VirtualProtect(void* lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualQuery")]
    private static extern nuint Win32VirtualQuery(void* lpAddress, out BasicInfo lpBuffer, nuint dwLength);

    [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    private static extern void* Win32GetCurrentProcess();

    [DllImport("kernel32.dll", EntryPoint = "FlushInstructionCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Win32FlushInstructionCache(void* hProcess, void* lpBaseAddress, nuint dwSize);
}
