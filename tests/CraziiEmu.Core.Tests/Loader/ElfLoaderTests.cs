// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using CraziiEmu.Core.Loader;
using Xunit;

namespace CraziiEmu.Core.Tests.Loader;

/// <summary>
/// Contains unit tests for the <see cref="ElfLoader"/> class, ensuring correct parsing of 64-bit ELF images.
/// </summary>
public class ElfLoaderTests
{
    /// <summary>
    /// Verifies that loading a valid, plain static ELF extracts the correct header values and segment properties.
    /// </summary>
    [Fact]
    public void Load_WithValidHelloStaticElf_ExtractsCorrectHeaderAndSegments()
    {
        // Arrange
        var filePath = Path.Combine(AppContext.BaseDirectory, "test_hello_static.elf");
        var data = File.ReadAllBytes(filePath);

        // Act
        var elfImage = ElfLoader.Load(data);

        // Assert
        Assert.NotNull(elfImage);
        Assert.Equal(0x401790UL, elfImage.Header.EntryPoint);
        Assert.Equal(10, elfImage.ProgramHeaders.Count);
        Assert.Equal(64UL, elfImage.Header.ProgramHeaderOffset);

        var firstLoad = elfImage.ProgramHeaders[0];
        Assert.Equal(ProgramHeaderType.Load, firstLoad.HeaderType);
        Assert.Equal(0x400000UL, firstLoad.VirtualAddress);
        Assert.Equal(0x4f8UL, firstLoad.FileSize);
        Assert.Equal(ProgramHeaderFlags.Read, firstLoad.Flags);
        Assert.Equal(0x1000UL, firstLoad.Alignment);

        var secondLoad = elfImage.ProgramHeaders[1];
        Assert.Equal(ProgramHeaderType.Load, secondLoad.HeaderType);
        Assert.Equal(0x401000UL, secondLoad.VirtualAddress);
        Assert.Equal(0x7d8adUL, secondLoad.FileSize);
        Assert.Equal(ProgramHeaderFlags.Read | ProgramHeaderFlags.Execute, secondLoad.Flags);
        Assert.Equal(0x1000UL, secondLoad.Alignment);
    }

    /// <summary>
    /// Verifies that loading an ELF with an invalid magic signature throws an <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void Load_WithInvalidMagic_ThrowsInvalidDataException()
    {
        // Arrange
        var data = new byte[64];

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => ElfLoader.Load(data));
    }
}
