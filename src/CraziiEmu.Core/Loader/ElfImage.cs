// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Generic;

namespace CraziiEmu.Core.Loader;

/// <summary>
/// Represents a parsed generic Executable and Linkable Format (ELF) image.
/// </summary>
public sealed class ElfImage
{
    /// <summary>
    /// Gets the parsed ELF header.
    /// </summary>
    public ElfHeader Header { get; }

    /// <summary>
    /// Gets the collection of parsed program headers.
    /// </summary>
    public IReadOnlyList<ProgramHeader> ProgramHeaders { get; }

    /// <summary>
    /// Gets the list of dependent library names (from DT_NEEDED).
    /// </summary>
    public IReadOnlyList<string> NeededLibraries { get; }

    /// <summary>
    /// Gets the parsed relocation entries (from DT_RELA).
    /// </summary>
    public IReadOnlyList<Elf64_Rela> Relocations { get; }

    /// <summary>
    /// Gets the parsed symbol table entries (from DT_SYMTAB).
    /// </summary>
    public IReadOnlyList<Elf64_Sym> Symbols { get; }

    /// <summary>
    /// Gets the string table mapped by byte offset (from DT_STRTAB).
    /// </summary>
    public IReadOnlyDictionary<uint, string> StringTable { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElfImage"/> class.
    /// </summary>
    /// <param name="header">The parsed ELF header.</param>
    /// <param name="programHeaders">The parsed program headers.</param>
    /// <param name="neededLibraries">The list of dependent libraries.</param>
    /// <param name="relocations">The list of relocations.</param>
    /// <param name="symbols">The list of symbols.</param>
    /// <param name="stringTable">The string table.</param>
    public ElfImage(
        ElfHeader header, 
        IReadOnlyList<ProgramHeader> programHeaders,
        IReadOnlyList<string>? neededLibraries = null,
        IReadOnlyList<Elf64_Rela>? relocations = null,
        IReadOnlyList<Elf64_Sym>? symbols = null,
        IReadOnlyDictionary<uint, string>? stringTable = null)
    {
        Header = header;
        ProgramHeaders = programHeaders;
        NeededLibraries = neededLibraries ?? System.Array.Empty<string>();
        Relocations = relocations ?? System.Array.Empty<Elf64_Rela>();
        Symbols = symbols ?? System.Array.Empty<Elf64_Sym>();
        StringTable = stringTable ?? new Dictionary<uint, string>();
    }
}
