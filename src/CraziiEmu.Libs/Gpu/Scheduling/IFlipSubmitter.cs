// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Libs.Gpu.Scheduling;

public interface IFlipSubmitter
{
    const int FlipQueueFull = unchecked((int)0x80290012);

    int SubmitFlipFromGpu(RecordingBuffer buffer, int handle, int index, int flipMode, long flipArg, out ulong requestId);

    void WaitForSubmitSlot();

    void CompleteFlip(ulong requestId);
}
