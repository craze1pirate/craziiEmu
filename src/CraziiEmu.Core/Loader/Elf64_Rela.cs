// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Runtime.InteropServices;

namespace CraziiEmu.Core.Loader;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Elf64_Rela
{
    private readonly ulong _rOffset;
    private readonly ulong _rInfo;
    private readonly long _rAddend;

    public ulong Offset => _rOffset;
    
    public uint SymbolIndex => (uint)(_rInfo >> 32);
    
    public uint RelocationType => (uint)(_rInfo & 0xFFFFFFFF);
    
    public long Addend => _rAddend;
}
