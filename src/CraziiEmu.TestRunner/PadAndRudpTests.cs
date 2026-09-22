// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Buffers.Binary;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.Generated;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Network;
using CraziiEmu.Libs.Pad;

namespace CraziiEmu.TestRunner;

public static class PadAndRudpTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[131072];

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
        Console.WriteLine("[TEST] Starting PadAndRudpTests...");

        TestNidResolution();
        TestPadDeviceClassParseData();
        TestPadCompanionStubs();
        TestRudpExports();

        Console.WriteLine("[TEST] PadAndRudpTests PASSED cleanly.");
    }

    private static void TestNidResolution()
    {
        var stubs = SysAbiExportRegistry.CreateExports(Generation.Gen4 | Generation.Gen5);
        var expectedNids = new (string Nid, string Name)[]
        {
            ("IHPqcbc0zCA", "scePadDeviceClassParseData"),
            ("r44mAxdSG+U", "scePadSetAngularVelocityDeadbandState"),
            ("rIZnR6eSpvk", "scePadResetOrientation"),
            ("Yq0zOH7YNOM", "scePadSetVibrationTriggerEffectWeakWhileEmbeddedMicInUse"),
            ("n3kSX62fgNo", "scePadUnknownN3kSX62fgNo"),
            ("amuBfI-AQc4", "sceRudpInit"),
            ("6PBNpsgyaxw", "sceRudpEnableInternalIOThread"),
            ("SUEVes8gvmw", "sceRudpSetEventHandler"),
        };

        foreach (var (nid, expectedName) in expectedNids)
        {
            bool found = false;
            foreach (var stub in stubs)
            {
                if (stub.Nid == nid)
                {
                    if (stub.Name != expectedName)
                    {
                        throw new InvalidOperationException($"Expected NID {nid} to be {expectedName}, got {stub.Name}");
                    }
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                throw new InvalidOperationException($"NID {nid} ({expectedName}) was not found in SysAbiExportRegistry!");
            }
        }
    }

    private static void TestPadDeviceClassParseData()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Invalid handle
        ctx[CpuRegister.Rdi] = 99; // invalid handle
        ctx[CpuRegister.Rsi] = 0x1000;
        ctx[CpuRegister.Rdx] = 0x2000;
        PadExports.PadDeviceClassParseData(ctx);
        var ret = unchecked((int)ctx[CpuRegister.Rax]);
        if (ret != unchecked((int)0x80920003))
        {
            throw new InvalidOperationException($"Expected invalid handle error 0x80920003, got 0x{ret:X8}");
        }

        // 2. Null pointers
        ctx[CpuRegister.Rdi] = 1;
        ctx[CpuRegister.Rsi] = 0;
        ctx[CpuRegister.Rdx] = 0x2000;
        PadExports.PadDeviceClassParseData(ctx);
        ret = unchecked((int)ctx[CpuRegister.Rax]);
        if (ret != unchecked((int)0x80920001))
        {
            throw new InvalidOperationException($"Expected invalid arg error 0x80920001, got 0x{ret:X8}");
        }

        ctx[CpuRegister.Rsi] = 0x1000;
        ctx[CpuRegister.Rdx] = 0;
        PadExports.PadDeviceClassParseData(ctx);
        ret = unchecked((int)ctx[CpuRegister.Rax]);
        if (ret != unchecked((int)0x80920001))
        {
            throw new InvalidOperationException($"Expected invalid arg error 0x80920001, got 0x{ret:X8}");
        }

        // 3. Standard connected pad
        Span<byte> padData = stackalloc byte[120];
        padData.Clear();
        padData[0x4C] = 1; // connected
        padData[0x6B] = 0; // uniqueDataLen = 0
        mem.TryWrite(0x1000, padData);

        ctx[CpuRegister.Rdi] = 1;
        ctx[CpuRegister.Rsi] = 0x1000;
        ctx[CpuRegister.Rdx] = 0x2000;
        PadExports.PadDeviceClassParseData(ctx);
        ret = unchecked((int)ctx[CpuRegister.Rax]);
        if (ret != 0)
        {
            throw new InvalidOperationException($"Expected return 0, got {ret}");
        }

        Span<byte> classData = stackalloc byte[24];
        mem.TryRead(0x2000, classData);

        int deviceClass = BinaryPrimitives.ReadInt32LittleEndian(classData[0x00..]);
        if (deviceClass != 0)
        {
            throw new InvalidOperationException($"Expected deviceClass == 0, got {deviceClass}");
        }
        if (classData[0x04] != 1)
        {
            throw new InvalidOperationException($"Expected data_valid == 1, got {classData[0x04]}");
        }

        // 4. Pad with unique data payload
        padData.Clear();
        padData[0x4C] = 1;
        padData[0x6B] = 4; // unique data length
        padData[0x6C] = 0xDE;
        padData[0x6D] = 0xAD;
        padData[0x6E] = 0xBE;
        padData[0x6F] = 0xEF;
        mem.TryWrite(0x1000, padData);

        PadExports.PadDeviceClassParseData(ctx);
        ret = unchecked((int)ctx[CpuRegister.Rax]);
        if (ret != 0)
        {
            throw new InvalidOperationException($"Expected return 0, got {ret}");
        }

        mem.TryRead(0x2000, classData);
        deviceClass = BinaryPrimitives.ReadInt32LittleEndian(classData[0x00..]);
        if (deviceClass != -1)
        {
            throw new InvalidOperationException($"Expected deviceClass == -1 with unique data, got {deviceClass}");
        }
        if (classData[0x04] != 1)
        {
            throw new InvalidOperationException($"Expected data_valid == 1, got {classData[0x04]}");
        }
        if (classData[0x08] != 4)
        {
            throw new InvalidOperationException($"Expected data_len == 4, got {classData[0x08]}");
        }
        if (classData[0x0C] != 0xDE || classData[0x0D] != 0xAD || classData[0x0E] != 0xBE || classData[0x0F] != 0xEF)
        {
            throw new InvalidOperationException("Unique data bytes were not copied accurately to classData");
        }
    }

    private static void TestPadCompanionStubs()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ctx[CpuRegister.Rdi] = 1;
        PadExports.PadSetAngularVelocityDeadbandState(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("PadSetAngularVelocityDeadbandState failed");

        ctx[CpuRegister.Rdi] = 1;
        PadExports.PadResetOrientation(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("PadResetOrientation failed");

        ctx[CpuRegister.Rdi] = 1;
        PadExports.PadSetVibrationTriggerEffectWeakWhileEmbeddedMicInUse(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("PadSetVibrationTriggerEffectWeakWhileEmbeddedMicInUse failed");

        // PadUnknownN3kSX62fgNo clears buffer
        Span<byte> testBuffer = stackalloc byte[16];
        testBuffer.Fill(0x55);
        mem.TryWrite(0x13000, testBuffer);

        ctx[CpuRegister.Rdi] = 0x13000;
        ctx[CpuRegister.Rcx] = 16;
        PadExports.PadUnknownN3kSX62fgNo(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("PadUnknownN3kSX62fgNo failed");

        mem.TryRead(0x13000, testBuffer);
        foreach (var b in testBuffer)
        {
            if (b != 0) throw new InvalidOperationException("PadUnknownN3kSX62fgNo failed to zero buffer");
        }
    }

    private static void TestRudpExports()
    {
        var mem = new DummyMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ctx[CpuRegister.Rdi] = 0x1000;
        ctx[CpuRegister.Rsi] = 52428800; // 50MB
        RudpExports.RudpInit(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("RudpInit failed");

        ctx[CpuRegister.Rdi] = 0x10000;
        ctx[CpuRegister.Rsi] = 100;
        RudpExports.RudpEnableInternalIOThread(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("RudpEnableInternalIOThread failed");

        ctx[CpuRegister.Rdi] = 0x80001000;
        ctx[CpuRegister.Rsi] = 0x80002000;
        RudpExports.RudpSetEventHandler(ctx);
        if (ctx[CpuRegister.Rax] != 0) throw new InvalidOperationException("RudpSetEventHandler failed");
    }
}
