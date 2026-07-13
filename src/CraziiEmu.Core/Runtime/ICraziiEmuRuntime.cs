// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core.Loader;
using CraziiEmu.HLE;

namespace CraziiEmu.Core.Runtime;

public interface ICraziiEmuRuntime : IDisposable
{
    string? LastExecutionDiagnostics { get; }

    string? LastExecutionTrace { get; }

    string? LastSessionSummary { get; }

    string? LastBasicBlockTrace { get; }

    string? LastMilestoneLog { get; }

    SelfImage LoadImage(string ebootPath);

    OrbisGen2Result Run(string ebootPath);

    OrbisGen2Result DispatchHleCall(string nid, CpuContext context);
}
