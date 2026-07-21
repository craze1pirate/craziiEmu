// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CraziiEmu.Core.Loader;

/// <summary>
/// Provides functionality for parsing plain, unencrypted 64-bit ELF files per the System V ABI spec.
/// </summary>
public static class ElfLoader
{
    private static readonly int ElfHeaderSize = Unsafe.SizeOf<ElfHeader>();
    private static readonly int ProgramHeaderSize = Unsafe.SizeOf<ProgramHeader>();

    /// <summary>
    /// Parses an ELF image from the provided byte span.
    /// </summary>
    /// <param name="data">The raw bytes of the ELF file.</param>
    /// <returns>An <see cref="ElfImage"/> representing the parsed headers.</returns>
    /// <exception cref="InvalidDataException">Thrown if the ELF data is invalid or unsupported.</exception>
    public static ElfImage Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < ElfHeaderSize)
        {
            throw new InvalidDataException("Data is too small to contain a valid ELF header.");
        }

        var header = MemoryMarshal.Read<ElfHeader>(data);

        if (!header.HasElfMagic)
        {
            throw new InvalidDataException("Invalid ELF magic number.");
        }

        if (!header.Is64Bit)
        {
            throw new InvalidDataException("Only 64-bit ELF files are supported.");
        }

        if (!header.IsLittleEndian)
        {
            throw new InvalidDataException("Only little-endian ELF files are supported.");
        }

        if (header.ProgramHeaderEntrySize != ProgramHeaderSize && header.ProgramHeaderCount > 0)
        {
            throw new InvalidDataException($"Unexpected program header entry size: {header.ProgramHeaderEntrySize}. Expected: {ProgramHeaderSize}.");
        }

        var expectedProgramHeadersEnd = header.ProgramHeaderOffset + ((ulong)header.ProgramHeaderCount * header.ProgramHeaderEntrySize);
        if ((ulong)data.Length < expectedProgramHeadersEnd)
        {
            throw new InvalidDataException("Data is too small to contain all program headers.");
        }

        var programHeaders = new ProgramHeader[header.ProgramHeaderCount];
        var programHeaderSpan = data.Slice((int)header.ProgramHeaderOffset, header.ProgramHeaderCount * ProgramHeaderSize);
        
        var parsedHeaders = MemoryMarshal.Cast<byte, ProgramHeader>(programHeaderSpan);
        for (var i = 0; i < header.ProgramHeaderCount; i++)
        {
            programHeaders[i] = parsedHeaders[i];
        }
        var neededLibraries = new List<string>();
        var relocations = new List<Elf64_Rela>();
        var symbols = new List<Elf64_Sym>();
        var stringTable = new Dictionary<uint, string>();

        foreach (var ph in programHeaders)
        {
            if (ph.HeaderType == ProgramHeaderType.Dynamic)
            {
                ParseDynamicSegment(data, ph, programHeaders, neededLibraries, relocations, symbols, stringTable);
                break;
            }
        }

        return new ElfImage(header, programHeaders, neededLibraries, relocations, symbols, stringTable);
    }

    private static void ParseDynamicSegment(
        ReadOnlySpan<byte> data, 
        ProgramHeader dynamicHeader, 
        ProgramHeader[] programHeaders,
        List<string> neededLibraries,
        List<Elf64_Rela> relocations,
        List<Elf64_Sym> symbols,
        Dictionary<uint, string> stringTable)
    {
        var dynSpan = data.Slice((int)dynamicHeader.Offset, (int)dynamicHeader.FileSize);
        var dynEntries = MemoryMarshal.Cast<byte, Elf64_Dyn>(dynSpan);
        
        ulong strTabVA = 0, symTabVA = 0, relaVA = 0;
        ulong strTabSize = 0, relaSize = 0, relaEnt = 0;
        
        var neededStringIndices = new List<uint>();

        foreach (var dyn in dynEntries)
        {
            switch (dyn.Tag)
            {
                case ElfDynamicTag.Null:
                    goto DoneTags;
                case ElfDynamicTag.Needed:
                    neededStringIndices.Add((uint)dyn.Value);
                    break;
                case ElfDynamicTag.StringTable:
                    strTabVA = dyn.Pointer;
                    break;
                case ElfDynamicTag.StringTableSize:
                    strTabSize = dyn.Value;
                    break;
                case ElfDynamicTag.SymbolTable:
                    symTabVA = dyn.Pointer;
                    break;
                case ElfDynamicTag.Rela:
                    relaVA = dyn.Pointer;
                    break;
                case ElfDynamicTag.RelaSize:
                    relaSize = dyn.Value;
                    break;
                case ElfDynamicTag.RelaEntrySize:
                    relaEnt = dyn.Value;
                    break;
            }
        }
    DoneTags:

        if (strTabVA != 0 && strTabSize != 0)
        {
            var strTabOffset = VirtualAddressToFileOffset(strTabVA, programHeaders);
            var strTabSpan = data.Slice((int)strTabOffset, (int)strTabSize);
            
            uint currentOffset = 0;
            while (currentOffset < strTabSpan.Length)
            {
                var slice = strTabSpan.Slice((int)currentOffset);
                var nullIdx = slice.IndexOf((byte)0);
                if (nullIdx == -1) break;
                
                var str = Encoding.UTF8.GetString(slice.Slice(0, nullIdx));
                stringTable[currentOffset] = str;
                currentOffset += (uint)(nullIdx + 1);
            }
            
            foreach (var idx in neededStringIndices)
            {
                if (stringTable.TryGetValue(idx, out var name))
                {
                    neededLibraries.Add(name);
                }
            }
        }
        
        if (symTabVA != 0)
        {
            ulong endVA = ulong.MaxValue;
            if (strTabVA > symTabVA) endVA = Math.Min(endVA, strTabVA);
            if (relaVA > symTabVA) endVA = Math.Min(endVA, relaVA);
            if (endVA == ulong.MaxValue)
            {
                foreach (var ph in programHeaders)
                {
                    if (ph.HeaderType == ProgramHeaderType.Load && symTabVA >= ph.VirtualAddress && symTabVA < ph.VirtualAddress + ph.MemorySize)
                    {
                        endVA = ph.VirtualAddress + ph.MemorySize;
                        break;
                    }
                }
            }
            
            if (endVA > symTabVA)
            {
                var symOffset = VirtualAddressToFileOffset(symTabVA, programHeaders);
                var symSpan = data.Slice((int)symOffset, (int)(endVA - symTabVA));
                var symEntries = MemoryMarshal.Cast<byte, Elf64_Sym>(symSpan);
                foreach (var sym in symEntries)
                {
                    symbols.Add(sym);
                }
            }
        }

        if (relaVA != 0 && relaSize != 0 && relaEnt != 0)
        {
            var relaOffset = VirtualAddressToFileOffset(relaVA, programHeaders);
            var relaSpan = data.Slice((int)relaOffset, (int)relaSize);
            var relaEntries = MemoryMarshal.Cast<byte, Elf64_Rela>(relaSpan);
            foreach (var rela in relaEntries)
            {
                relocations.Add(rela);
            }
        }
    }

    private static ulong VirtualAddressToFileOffset(ulong virtualAddress, ProgramHeader[] programHeaders)
    {
        foreach (var ph in programHeaders)
        {
            if (ph.HeaderType == ProgramHeaderType.Load || ph.HeaderType == ProgramHeaderType.Dynamic)
            {
                if (virtualAddress >= ph.VirtualAddress && virtualAddress < ph.VirtualAddress + ph.MemorySize)
                {
                    return ph.Offset + (virtualAddress - ph.VirtualAddress);
                }
            }
        }
        throw new InvalidDataException($"Virtual address 0x{virtualAddress:X} not found in any segment.");
    }
}
