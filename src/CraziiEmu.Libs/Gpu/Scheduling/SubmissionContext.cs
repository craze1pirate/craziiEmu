// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Libs.Gpu.Scheduling;

// The guest queue whose work the current recording buffer carries.
public sealed class SubmissionContext
{
    public string QueueName { get; set; } = "host.default";

    public ulong SubmissionId { get; set; }
}
