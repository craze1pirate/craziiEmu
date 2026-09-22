// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Threading;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace CraziiEmu.Libs.VideoOut;

/// <summary>
/// Host-GPU timeline synchronization manager, ported from KytyPS5's MasterSemaphore.
/// Wraps a native Vulkan 1.2 timeline semaphore (or provides a thread-safe software
/// fallback when native timeline semaphores are unavailable).
/// </summary>
internal sealed unsafe class VulkanMasterSemaphore : IDisposable
{
    private readonly Vk? _vk;
    private readonly Device _device;
    private Semaphore _semaphore;
    private readonly bool _hasNativeTimeline;
    private readonly ManualResetEventSlim _softwareWaitEvent = new(false);

    private long _currentTick = 1;
    private long _gpuTick = 0;
    private bool _disposed;

    /// <summary>
    /// Gets the current host allocation tick (the next tick will be this value).
    /// </summary>
    public ulong CurrentTick => (ulong)Interlocked.Read(ref _currentTick);

    /// <summary>
    /// Gets the highest GPU tick known to have completed on the device.
    /// </summary>
    public ulong KnownGpuTick => (ulong)Interlocked.Read(ref _gpuTick);

    /// <summary>
    /// Gets the native Vulkan semaphore handle.
    /// </summary>
    public Semaphore Handle => _semaphore;

    /// <summary>
    /// True if native Vulkan timeline semaphore is active; false if running in software emulation mode.
    /// </summary>
    public bool HasNativeTimeline => _hasNativeTimeline;

    public VulkanMasterSemaphore(Vk? vk = null, Device device = default, bool enableNativeTimeline = true)
    {
        _vk = vk;
        _device = device;

        if (enableNativeTimeline && _vk is not null && _device.Handle != 0)
        {
            var typeInfo = new SemaphoreTypeCreateInfo
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0,
            };

            var createInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &typeInfo,
            };

            var result = _vk.CreateSemaphore(_device, &createInfo, null, out _semaphore);
            if (result == Result.Success && _semaphore.Handle != 0)
            {
                _hasNativeTimeline = true;
            }
            else
            {
                _semaphore = default;
                _hasNativeTimeline = false;
            }
        }
        else
        {
            _hasNativeTimeline = false;
        }
    }

    /// <summary>
    /// Returns true if the specified tick has already completed on the GPU.
    /// Fast path: lock-free atomic read with zero Vulkan/kernel syscall overhead.
    /// </summary>
    public bool IsFree(ulong tick) => KnownGpuTick >= tick;

    /// <summary>
    /// Allocates and returns the next submission tick.
    /// </summary>
    public ulong NextTick()
    {
        return (ulong)Interlocked.Increment(ref _currentTick) - 1;
    }

    /// <summary>
    /// Non-blocking query of the native GPU timeline semaphore counter.
    /// Monotonically advances <see cref="KnownGpuTick"/>.
    /// </summary>
    public void Refresh()
    {
        if (_hasNativeTimeline && _vk is not null && _device.Handle != 0)
        {
            ulong counter = 0;
            var result = _vk.GetSemaphoreCounterValue(_device, _semaphore, &counter);
            if (result == Result.Success)
            {
                AdvanceGpuTickDirect(counter);
            }
        }
    }

    /// <summary>
    /// Atomically updates the known GPU tick to at least the given value and wakes any waiters.
    /// </summary>
    public void AdvanceGpuTickDirect(ulong tick)
    {
        var current = Interlocked.Read(ref _gpuTick);
        while ((ulong)current < tick)
        {
            var prev = Interlocked.CompareExchange(ref _gpuTick, (long)tick, current);
            if (prev == current)
            {
                _softwareWaitEvent.Set();
                break;
            }

            current = prev;
        }
    }

    /// <summary>
    /// Waits until the GPU reaches the specified tick or the timeout elapses.
    /// </summary>
    /// <param name="tick">The target timeline tick to wait for.</param>
    /// <param name="timeoutNs">Timeout in nanoseconds (default: infinite).</param>
    /// <returns>True if the tick completed; false on timeout or failure.</returns>
    public bool Wait(ulong tick, ulong timeoutNs = ulong.MaxValue)
    {
        if (IsFree(tick))
        {
            return true;
        }

        Refresh();
        if (IsFree(tick))
        {
            return true;
        }

        if (_hasNativeTimeline && _vk is not null && _device.Handle != 0)
        {
            var sem = _semaphore;
            var val = tick;
            var waitInfo = new SemaphoreWaitInfo
            {
                SType = StructureType.SemaphoreWaitInfo,
                SemaphoreCount = 1,
                PSemaphores = &sem,
                PValues = &val,
            };

            var result = _vk.WaitSemaphores(_device, &waitInfo, timeoutNs);
            Refresh();
            return result == Result.Success && IsFree(tick);
        }

        // Software fallback wait
        if (timeoutNs == 0)
        {
            return IsFree(tick);
        }

        var timeoutMs = timeoutNs >= (ulong.MaxValue / 1_000_000UL)
            ? Timeout.Infinite
            : (int)Math.Min((long)(timeoutNs / 1_000_000UL), int.MaxValue);

        while (!IsFree(tick))
        {
            _softwareWaitEvent.Reset();
            if (IsFree(tick))
            {
                return true;
            }

            if (!_softwareWaitEvent.Wait(timeoutMs))
            {
                return IsFree(tick);
            }
        }

        return true;
    }

    /// <summary>
    /// Signals the timeline semaphore directly from the host CPU.
    /// </summary>
    public void SignalHost(ulong tick)
    {
        AdvanceGpuTickDirect(tick);

        if (_hasNativeTimeline && _vk is not null && _device.Handle != 0)
        {
            var sem = _semaphore;
            var signalInfo = new SemaphoreSignalInfo
            {
                SType = StructureType.SemaphoreSignalInfo,
                Semaphore = sem,
                Value = tick,
            };
            _vk.SignalSemaphore(_device, &signalInfo);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hasNativeTimeline && _vk is not null && _device.Handle != 0 && _semaphore.Handle != 0)
        {
            _vk.DestroySemaphore(_device, _semaphore, null);
            _semaphore = default;
        }

        _softwareWaitEvent.Dispose();
    }
}
