// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public readonly struct CpuNotImplementedInfo
{
    public CpuNotImplementedInfo(
        CpuNotImplementedSource source,
        ulong instructionPointer,
        string? nid,
        string? exportName,
        string? libraryName,
        string? detail)
    {
        Source = source;
        InstructionPointer = instructionPointer;
        Nid = nid;
        ExportName = exportName;
        LibraryName = libraryName;
        Detail = detail;
    }

    public CpuNotImplementedSource Source { get; }

    public ulong InstructionPointer { get; }

    public string? Nid { get; }

    public string? ExportName { get; }

    public string? LibraryName { get; }

    public string? Detail { get; }
}
