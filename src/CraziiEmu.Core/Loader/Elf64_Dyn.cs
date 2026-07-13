// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.Core.Loader;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Elf64_Dyn
{
    private readonly long _dTag;
    private readonly ulong _dUn;

    public ElfDynamicTag Tag => (ElfDynamicTag)_dTag;
    public ulong Value => _dUn;
    public ulong Pointer => _dUn;
}

public enum ElfDynamicTag : long
{
    Null = 0,
    Needed = 1,
    PltRelSize = 2,
    PltGot = 3,
    Hash = 4,
    StringTable = 5,
    SymbolTable = 6,
    Rela = 7,
    RelaSize = 8,
    RelaEntrySize = 9,
    StringTableSize = 10,
    SymbolTableEntrySize = 11,
    Init = 12,
    Fini = 13,
    SoName = 14,
    RPath = 15,
    Symbolic = 16,
    Rel = 17,
    RelSize = 18,
    RelEntrySize = 19,
    PltRel = 20,
    Debug = 21,
    TextRel = 22,
    JmpRel = 23,
    BindNow = 24,
    InitArray = 25,
    FiniArray = 26,
    InitArraySize = 27,
    FiniArraySize = 28,
    RunPath = 29,
    Flags = 30,
    
    // Some Sony specific tags might exist but these are standard SystemV
}
