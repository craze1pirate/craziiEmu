// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;

namespace CraziiEmu.Core.Cpu.Native;

public interface INativeCpuBackend
{
    string BackendName { get; }

    string? LastError { get; }

    bool TryExecute(
        CpuContext context,
        ulong entryPoint,
        Generation generation,
        IReadOnlyDictionary<ulong, string> importStubs,
        IReadOnlyDictionary<string, ulong> runtimeSymbols,
        CpuExecutionOptions executionOptions,
        out OrbisGen2Result result);
}
