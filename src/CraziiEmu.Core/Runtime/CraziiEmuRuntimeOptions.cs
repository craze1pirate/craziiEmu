// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Runtime;

using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Cpu.Debugging;

public readonly struct CraziiEmuRuntimeOptions
{
    public CpuExecutionEngine CpuEngine { get; init; }

    public bool StrictDynlibResolution { get; init; }

    public int ImportTraceLimit { get; init; }

    /// <summary>
    /// An optional debugger to attach to guest execution. Flows through to
    /// <see cref="CpuExecutionOptions.DebugHook"/>. Null (the default) runs with
    /// no debugger attached.
    /// </summary>
    public ICpuDebugHook? DebugHook { get; init; }
}
