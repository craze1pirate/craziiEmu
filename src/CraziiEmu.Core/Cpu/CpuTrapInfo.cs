// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Cpu;

public readonly struct CpuTrapInfo
{
    public CpuTrapInfo(ulong instructionPointer, byte opcode)
    {
        InstructionPointer = instructionPointer;
        Opcode = opcode;
    }

    public ulong InstructionPointer { get; }

    public byte Opcode { get; }
}
