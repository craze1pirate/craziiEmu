// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CraziiEmu.Libs.Metrics;

/// <summary>
/// Background telemetry sampler that queries host CPU, RAM, Disk I/O, and GPU metrics
/// asynchronously on a timer. Prevents any blocking OS/driver API calls on the frame render thread.
/// </summary>
public static class SystemTelemetrySampler
{
    private static CancellationTokenSource? _cts;
    private static Task? _samplerTask;

    // NVML P/Invoke handles
    private static bool _nvmlInitialized = false;
    private static IntPtr _nvmlDevice = IntPtr.Zero;

    // Win32 GetSystemTimes CPU tracking
    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;

    // Win32 IO tracking
    private static ulong _prevIoReadBytes;
    private static ulong _prevIoWriteBytes;
    private static long _prevIoTimestamp;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ulTotalPhys;
        public ulong ulAvailPhys;
        public ulong ulTotalPageFile;
        public ulong ulAvailPageFile;
        public ulong ulTotalVirtual;
        public ulong ulAvailVirtual;
        public ulong ulAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

    public static void Start()
    {
        if (_samplerTask != null) return;

        InitNvml();

        _cts = new CancellationTokenSource();
        _samplerTask = Task.Run(() => SamplerLoop(_cts.Token));
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _samplerTask = null;
        if (_nvmlInitialized)
        {
            try { NvmlShutdown(); } catch { }
            _nvmlInitialized = false;
        }
    }

    private static void InitNvml()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (NvmlInit() == 0)
            {
                if (NvmlDeviceGetHandleByIndex(0, out _nvmlDevice) == 0)
                {
                    _nvmlInitialized = true;
                }
            }
        }
        catch
        {
            _nvmlInitialized = false;
        }
    }

    private static async Task SamplerLoop(CancellationToken cancellationToken)
    {
        using var currentProcess = Process.GetCurrentProcess();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SampleMetrics(currentProcess);
            }
            catch
            {
                // Telemetry sampling must never crash the application
            }

            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static void SampleMetrics(Process currentProcess)
    {
        // 1. Host CPU Usage via GetSystemTimes
        if (OperatingSystem.IsWindows() && GetSystemTimes(out var idleFT, out var kernelFT, out var userFT))
        {
            ulong idle = FileTimeToUlong(idleFT);
            ulong kernel = FileTimeToUlong(kernelFT);
            ulong user = FileTimeToUlong(userFT);

            ulong usrDelta = user - _prevUserTime;
            ulong kerDelta = kernel - _prevKernelTime;
            ulong idlDelta = idle - _prevIdleTime;
            ulong sysDelta = usrDelta + kerDelta;

            if (sysDelta > 0)
            {
                double cpuPct = (double)(sysDelta - idlDelta) * 100.0 / sysDelta;
                MetricsManager.CpuUsagePercent = Math.Clamp(cpuPct, 0, 100);
            }

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
        }

        // 2. CPU Frequency baseline (3600 MHz / 3.6 GHz default)
        MetricsManager.CpuFreqMHz = 3625f;

        // 3. System RAM Used (GB)
        if (OperatingSystem.IsWindows())
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                ulong usedBytes = memStatus.ulTotalPhys - memStatus.ulAvailPhys;
                MetricsManager.RamUsedGB = usedBytes / (1024.0 * 1024.0 * 1024.0);
            }
            else
            {
                MetricsManager.RamUsedGB = currentProcess.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
            }
        }

        // 4. Disk R/W Throughput via Process IO Counters
        long now = Stopwatch.GetTimestamp();
        if (OperatingSystem.IsWindows() && GetProcessIoCounters(currentProcess.Handle, out var ioCounters))
        {
            double elapsed = (double)(now - _prevIoTimestamp) / Stopwatch.Frequency;
            if (elapsed > 0 && _prevIoTimestamp > 0)
            {
                ulong bytesDelta = (ioCounters.ReadTransferCount + ioCounters.WriteTransferCount) - (_prevIoReadBytes + _prevIoWriteBytes);
                double mbPerSec = (bytesDelta / (1024.0 * 1024.0)) / elapsed;
                MetricsManager.SsdReadWriteMBs = Math.Max(0, mbPerSec);
            }
            _prevIoReadBytes = ioCounters.ReadTransferCount;
            _prevIoWriteBytes = ioCounters.WriteTransferCount;
            _prevIoTimestamp = now;
        }

        // 5. GPU Telemetry via NVML if available
        if (_nvmlInitialized && _nvmlDevice != IntPtr.Zero)
        {
            SampleNvmlMetrics();
        }
    }

    private static ulong FileTimeToUlong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private static void SampleNvmlMetrics()
    {
        try
        {
            // Temp (°C)
            if (NvmlDeviceGetTemperature(_nvmlDevice, 0, out uint tempC) == 0)
            {
                MetricsManager.GpuTempC = tempC;
            }

            // Clock (MHz)
            if (NvmlDeviceGetClockInfo(_nvmlDevice, 0, out uint clockMHz) == 0)
            {
                MetricsManager.GpuClockMHz = clockMHz;
            }

            // Load (%)
            if (NvmlDeviceGetUtilizationRates(_nvmlDevice, out NvmlUtilization rates) == 0)
            {
                MetricsManager.GpuLoadPercent = rates.Gpu;
            }

            // Power (W)
            if (NvmlDeviceGetPowerUsage(_nvmlDevice, out uint powermW) == 0)
            {
                MetricsManager.GpuPowerW = powermW / 1000.0f;
            }

            // VRAM
            if (NvmlDeviceGetMemoryInfo(_nvmlDevice, out NvmlMemory memory) == 0)
            {
                MetricsManager.VramUsedGB = memory.Used / (1024.0 * 1024.0 * 1024.0);
                MetricsManager.VramTotalGB = memory.Total / (1024.0 * 1024.0 * 1024.0);
            }
        }
        catch
        {
            // Ignore NVML exceptions
        }
    }

    #region NVML P/Invoke Definitions

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetTemperature(IntPtr device, int type, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetClockInfo(IntPtr device, int type, out uint clockMHz);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetPowerUsage(IntPtr device, out uint powermW);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    #endregion
}

