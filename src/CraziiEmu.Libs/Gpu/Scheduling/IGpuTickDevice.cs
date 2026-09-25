// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Libs.Gpu.Scheduling;

// Provide the scheduler with one timeline semaphore, one command pool, and one queue.
public interface IGpuTickDevice : IDisposable
{
    object QueueGate { get; }

    ulong TimelineHandle { get; }

    ulong ReadTimeline();

    bool TryWaitTimeline(ulong tick, out string failure);

    nint[] AllocateBuffers(int count);

    void BeginBuffer(nint buffer);

    void EndBuffer(nint buffer);

    bool TrySubmit(nint buffer, SubmitBundle bundle, out string failure);
}
