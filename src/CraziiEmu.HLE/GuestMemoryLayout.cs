// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Globalization;
using System.Runtime.InteropServices;

namespace CraziiEmu.HLE;

public static class GuestMemoryLayout
{
    private static readonly ulong _directBytes = ResolveDirectBytes();
    public static ulong DirectBytes => _directBytes;
    public const ulong FlexibleBytes = 448UL * 1024 * 1024;
    public static ulong FlexibleOffset => _directBytes;
    public static ulong BackingBytes => _directBytes + FlexibleBytes;
    public const ulong GuestPage = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static ulong ResolveDirectBytes()
    {
        var env = Environment.GetEnvironmentVariable("CRAZIIEMU_DIRECT_MEMORY_MB");
        if (!string.IsNullOrEmpty(env) && ulong.TryParse(env, NumberStyles.None, CultureInfo.InvariantCulture, out var mb) && mb >= 1024)
        {
            return mb * 1024UL * 1024UL;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var stat = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref stat))
                {
                    var avail = stat.ullAvailPageFile;
                    if (avail >= (20UL * 1024 * 1024 * 1024))
                    {
                        return 16384UL * 1024 * 1024; // 16 GB
                    }
                    if (avail >= (14UL * 1024 * 1024 * 1024))
                    {
                        return 12288UL * 1024 * 1024; // 12 GB
                    }
                    if (avail >= (10UL * 1024 * 1024 * 1024))
                    {
                        return 8192UL * 1024 * 1024;  // 8 GB
                    }
                    if (avail >= (6UL * 1024 * 1024 * 1024))
                    {
                        return 4096UL * 1024 * 1024;  // 4 GB
                    }
                }
            }
        }
        catch
        {
        }

        return 8192UL * 1024 * 1024;
    }
}
