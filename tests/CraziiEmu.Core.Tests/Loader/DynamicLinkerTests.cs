// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;
using Xunit;

namespace CraziiEmu.Core.Tests.Loader;

/// <summary>
/// Contains unit tests for the <see cref="DynamicLinker"/> class.
/// </summary>
public class DynamicLinkerTests
{
    private byte[] CreateMockElf(string name, bool hasImport)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ELF Header (64 bytes)
        bw.Write(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' });
        bw.Write((byte)2); // 64-bit
        bw.Write((byte)1); // Little endian
        bw.Write((byte)1); // Version
        bw.Write((byte)0); // OS ABI
        bw.Write((byte)0); // ABI Version
        bw.Write(new byte[7]); // Pad
        
        bw.Write((ushort)2); // Executable
        bw.Write((ushort)0x3E); // AMD64
        bw.Write((uint)1); // Version
        bw.Write((ulong)0x1000); // Entry
        bw.Write((ulong)64); // Program header offset
        bw.Write((ulong)0); // Section header offset
        bw.Write((uint)0); // Flags
        bw.Write((ushort)64); // Header size
        bw.Write((ushort)56); // Program header size
        bw.Write((ushort)2); // Program header count
        bw.Write((ushort)0); // Section header size
        bw.Write((ushort)0); // Section header count
        bw.Write((ushort)0); // Section header string index

        // Program Header 1: LOAD (56 bytes)
        bw.Write((uint)1); // PT_LOAD
        bw.Write((uint)7); // RWX
        bw.Write((ulong)0); // Offset
        bw.Write((ulong)0x1000); // VAddr
        bw.Write((ulong)0x1000); // PAddr
        bw.Write((ulong)0x1000); // FileSize
        bw.Write((ulong)0x1000); // MemSize
        bw.Write((ulong)0x1000); // Align

        // Program Header 2: DYNAMIC (56 bytes)
        bw.Write((uint)2); // PT_DYNAMIC
        bw.Write((uint)6); // RW
        bw.Write((ulong)176); // Offset (after headers)
        bw.Write((ulong)0x10B0); // VAddr
        bw.Write((ulong)0x10B0); // PAddr
        bw.Write((ulong)256); // FileSize
        bw.Write((ulong)256); // MemSize
        bw.Write((ulong)8); // Align

        // Dynamic Section at offset 176 (0xB0)
        if (hasImport)
        {
            bw.Write((long)1); // DT_NEEDED
            bw.Write((ulong)1); // strtab offset 1

            bw.Write((long)5); // DT_STRTAB
            bw.Write((ulong)0x1200);

            bw.Write((long)10); // DT_STRSZ
            bw.Write((ulong)32);

            bw.Write((long)6); // DT_SYMTAB
            bw.Write((ulong)0x1220);

            bw.Write((long)7); // DT_RELA
            bw.Write((ulong)0x1250);

            bw.Write((long)8); // DT_RELASZ
            bw.Write((ulong)24);

            bw.Write((long)9); // DT_RELAENT
            bw.Write((ulong)24);

            bw.Write((long)0); // DT_NULL
            bw.Write((ulong)0);

            while (ms.Position < 512) bw.Write((byte)0);

            // strtab at 0x200 (VAddr 0x1200)
            bw.Write((byte)0); // idx 0
            var libName = System.Text.Encoding.UTF8.GetBytes("libtest.sprx\0");
            bw.Write(libName); // idx 1
            var symName = System.Text.Encoding.UTF8.GetBytes("sceTestFunction\0");
            bw.Write(symName); // idx 14
            
            while (ms.Position < 544) bw.Write((byte)0);

            // symtab at 0x220 (VAddr 0x1220)
            bw.Write(new byte[24]);
            bw.Write((uint)14); // st_name
            bw.Write((byte)0x10); // global
            bw.Write((byte)0);
            bw.Write((ushort)0); // shndx undef
            bw.Write((ulong)0); // value
            bw.Write((ulong)0); // size

            // rela at 0x250 (VAddr 0x1250)
            while (ms.Position < 592) bw.Write((byte)0);
            
            bw.Write((ulong)0x1300); // r_offset (jump slot target)
            bw.Write((ulong)((1UL << 32) | 7)); // r_info (sym 1, type R_X86_64_JUMP_SLOT=7)
            bw.Write((long)0); // r_addend
            
            while (ms.Position < 0x1000) bw.Write((byte)0);
        }
        else
        {
            bw.Write((long)5); // DT_STRTAB
            bw.Write((ulong)0x1200);

            bw.Write((long)10); // DT_STRSZ
            bw.Write((ulong)32);

            bw.Write((long)6); // DT_SYMTAB
            bw.Write((ulong)0x1220);

            bw.Write((long)0); // DT_NULL
            bw.Write((ulong)0);

            while (ms.Position < 512) bw.Write((byte)0);

            // strtab at 0x200
            bw.Write((byte)0);
            bw.Write(System.Text.Encoding.UTF8.GetBytes("sceTestFunction\0"));
            while (ms.Position < 544) bw.Write((byte)0);

            // symtab at 0x220
            bw.Write(new byte[24]); // sym 0
            bw.Write((uint)1); // st_name
            bw.Write((byte)0x10); // global
            bw.Write((byte)0);
            bw.Write((ushort)1); // shndx 1
            bw.Write((ulong)0x1500); // value
            bw.Write((ulong)0x10); // size

            while (ms.Position < 0x1000) bw.Write((byte)0);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Verifies that the dynamic linker resolves a symbol from a dependent loaded library.
    /// </summary>
    [Fact]
    public void DynamicLinker_ResolvesImportedSymbol_FromDependentLibrary()
    {
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        
        byte[] mainElf = CreateMockElf("main", hasImport: true);
        byte[] libElf = CreateMockElf("libtest.sprx", hasImport: false);

        byte[]? Resolver(string name)
        {
            if (name == "libtest.sprx") return libElf;
            return null;
        }

        var linker = new DynamicLinker(vmm, Resolver);

        var mainModule = linker.LoadModule("main", mainElf);

        // target offset = r_offset (0x1300) - minVA (0x1000) = 0x300
        ulong jumpSlotAddress = mainModule.BaseAddress + 0x300;
        
        var span = vmm.GetSpan(jumpSlotAddress, 8);
        ulong resolvedAddress = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span);

        Assert.NotEqual(0UL, resolvedAddress);
        Assert.True(resolvedAddress > mainModule.BaseAddress + 0x1000); 
    }

    /// <summary>
    /// Verifies that the dynamic linker resolves an imported symbol using an HLE stub if registered.
    /// </summary>
    [Fact]
    public void DynamicLinker_ResolvesImportedSymbol_FromHleStub()
    {
        using var vmm = new VirtualMemoryManager(0x100000); // 1MB pool
        
        byte[] mainElf = CreateMockElf("main", hasImport: true);

        byte[]? Resolver(string name) => null;

        var linker = new DynamicLinker(vmm, Resolver);
        
        ulong expectedHleAddress = 0xCAFEBABE;
        linker.RegisterHleStub("libtest.sprx", "sceTestFunction", expectedHleAddress);

        var mainModule = linker.LoadModule("main", mainElf);

        // target offset = r_offset (0x1300) - minVA (0x1000) = 0x300
        ulong jumpSlotAddress = mainModule.BaseAddress + 0x300;
        var span = vmm.GetSpan(jumpSlotAddress, 8);
        ulong resolvedAddress = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span);

        Assert.Equal(expectedHleAddress, resolvedAddress);
    }
}
