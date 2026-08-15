// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.IO;
using System.Text;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.SaveData;

namespace CraziiEmu.TestRunner;

public static class SaveDataMountTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[65536];

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address + (ulong)destination.Length > (ulong)_ram.Length) return false;
            _ram.AsSpan((int)address, destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
        {
            if (address + (ulong)source.Length > (ulong)_ram.Length) return false;
            source.CopyTo(_ram.AsSpan((int)address, source.Length));
            return true;
        }

        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => true;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting SaveDataMountTests...");

        TestMountMissingDirectoryAutoCreate();
        TestInvalidParametersRejection();

        Console.WriteLine("[TEST] SaveDataMountTests PASSED cleanly.");
    }

    private static void TestMountMissingDirectoryAutoCreate()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // Prepare mount struct at 0x1000
        ulong mountAddr = 0x1000;
        ulong resultAddr = 0x2000;
        ulong dirNameAddr = 0x3000;

        string dirName = "TestSaveSlot1";
        byte[] dirBytes = Encoding.ASCII.GetBytes(dirName + "\0");
        mem.TryWrite(dirNameAddr, dirBytes);

        // Write Mount struct: userId (int32 at 0x00), dirNameAddr (uint64 at 0x08), mountMode (uint32 at 0x20)
        Span<byte> mountStruct = stackalloc byte[0x30];
        mountStruct.Clear();
        BitConverter.GetBytes(0x10000000).CopyTo(mountStruct[0x00..]);
        BitConverter.GetBytes(dirNameAddr).CopyTo(mountStruct[0x08..]);
        BitConverter.GetBytes(0u).CopyTo(mountStruct[0x20..]); // mountMode = 0 (read-only)

        mem.TryWrite(mountAddr, mountStruct);

        ctx[CpuRegister.Rdi] = mountAddr;
        ctx[CpuRegister.Rsi] = resultAddr;

        int res = SaveDataExports.SaveDataMount3(ctx);
        if (res != 0)
        {
            throw new InvalidOperationException($"SaveDataMount3 failed for missing directory: {res}");
        }

        Console.WriteLine("  [PASS] Missing first-boot save directory auto-created and mounted cleanly");

        // Unmount
        ctx[CpuRegister.Rdi] = resultAddr; // mountPoint "/savedata0" written to result
        res = SaveDataExports.SaveDataUmount2(ctx);
        if (res != 0)
        {
            throw new InvalidOperationException($"SaveDataUmount2 failed: {res}");
        }

        Console.WriteLine("  [PASS] SaveDataUmount2 executed cleanly");
    }

    private static void TestInvalidParametersRejection()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ctx[CpuRegister.Rdi] = 0;
        ctx[CpuRegister.Rsi] = 0;

        int res = SaveDataExports.SaveDataMount3(ctx);
        if (res == 0)
        {
            throw new InvalidOperationException("Expected failure for null parameters");
        }

        Console.WriteLine("  [PASS] Invalid null mount parameters rejected cleanly");
    }
}
