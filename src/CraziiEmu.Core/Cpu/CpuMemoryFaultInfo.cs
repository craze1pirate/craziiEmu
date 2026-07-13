// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public readonly struct CpuMemoryFaultInfo
{
    public CpuMemoryFaultInfo(ulong instructionPointer, byte? opcode, CpuMemoryAccessFailure access)
    {
        InstructionPointer = instructionPointer;
        Opcode = opcode;
        Access = access;
    }

    public ulong InstructionPointer { get; }

    public byte? Opcode { get; }

    public CpuMemoryAccessFailure Access { get; }
}
