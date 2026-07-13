// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.Core.Loader;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Elf64_Sym
{
    private readonly uint _stName;
    private readonly byte _stInfo;
    private readonly byte _stOther;
    private readonly ushort _stShndx;
    private readonly ulong _stValue;
    private readonly ulong _stSize;

    public uint NameIndex => _stName;
    
    public byte Info => _stInfo;
    
    public byte Other => _stOther;
    
    public ushort SectionIndex => _stShndx;
    
    public ulong Value => _stValue;
    
    public ulong Size => _stSize;
}
