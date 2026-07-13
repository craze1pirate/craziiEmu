// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public readonly struct CpuExecutionOptions
{
    public bool EnableDisasmDiagnostics { get; init; }
    
    public CpuExecutionEngine CpuEngine { get; init; }

    public bool StrictDynlibResolution { get; init; }

    public int ImportTraceLimit { get; init; }
}
