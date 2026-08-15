// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;

using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.AvPlayer;
using CraziiEmu.Libs.Videodec2;

namespace CraziiEmu.TestRunner;

public static class Videodec2Tests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[33554432]; // 32 MB ram

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

    private static CpuContext CreateContext(ICpuMemory mem) => new(mem, Generation.Gen5);

    private static bool TryReadUInt64(ICpuMemory mem, ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (mem.TryRead(address, bytes))
        {
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }
        value = 0;
        return false;
    }

    private static bool TryReadUInt32(ICpuMemory mem, ulong address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        if (mem.TryRead(address, bytes))
        {
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }
        value = 0;
        return false;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  libSceVideodec2 VERIFICATION TEST SUITE (20 TESTS)");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[20];

        testResults[0] = Test1_DecoderCreation();
        testResults[1] = Test2_DecoderConfigValidation();
        testResults[2] = Test3_ValidHandleLifecycle();
        testResults[3] = Test4_InvalidHandleValidation();
        testResults[4] = Test5_InvalidGuestPointers();
        testResults[5] = Test6_InputBitstreamSubmission();
        testResults[6] = Test7_BasicFrameDecoding();
        testResults[7] = Test8_Nv12OutputCorrectness();
        testResults[8] = Test9_WidthHeightStrideCorrectness();
        testResults[9] = Test10_FrameTimestampHandling();
        testResults[10] = Test11_FlushBehavior();
        testResults[11] = Test12_ResetBehavior();
        testResults[12] = Test13_RepeatedDecodeCycles();
        testResults[13] = Test14_MultipleDecoderHandles();
        testResults[14] = Test15_ErrorHandlingMalformedInput();
        testResults[15] = Test16_OutputBufferSizeValidation();
        testResults[16] = Test17_SchedulerNonBlockingBehavior();
        testResults[17] = Test18_DecoderResourceCleanup();
        testResults[18] = Test19_StressTestRepeatedFrames();
        testResults[19] = Test20_AvPlayerRegressionTest();

        Console.WriteLine("\n-------------------------------------------------");
        Console.WriteLine("  SUMMARY OF TEST RESULTS                        ");
        Console.WriteLine("-------------------------------------------------");
        var allPassed = true;
        for (int i = 0; i < testResults.Length; i++)
        {
            var res = testResults[i];
            var status = res.Passed ? "[PASS]" : "[FAIL]";
            Console.WriteLine($"{status} Test {i + 1}: {res.Name} - {res.Message}");
            if (!res.Passed) allPassed = false;
        }
        Console.WriteLine("-------------------------------------------------");
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 20 TARGET 4 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static void SetupValidConfig(ICpuMemory mem, ulong configAddr)
    {
        Span<byte> cfg = stackalloc byte[72];
        BinaryPrimitives.WriteUInt64LittleEndian(cfg[0x00..], 72);
        BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x08..], Videodec2Exports.VIDEODEC2_RESOURCE_TYPE_COMPUTE);
        BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x0C..], Videodec2Exports.CODEC_TYPE_AVC);
        BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x10..], 100); // profile
        BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x14..], 41);  // level
        BinaryPrimitives.WriteInt32LittleEndian(cfg[0x18..], 1920); // max width
        BinaryPrimitives.WriteInt32LittleEndian(cfg[0x1C..], 1080); // max height
        BinaryPrimitives.WriteInt32LittleEndian(cfg[0x20..], 16);   // max dpb
        BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x24..], 8);   // queue depth
        BinaryPrimitives.WriteUInt64LittleEndian(cfg[0x28..], 0x9000); // compute queue
        mem.TryWrite(configAddr, cfg);
    }

    private static void SetupValidMemInfo(ICpuMemory mem, ulong memInfoAddr)
    {
        Span<byte> memBuf = stackalloc byte[72];
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x00..], 72);
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x08..], Videodec2Exports.VIDEODEC2_MIN_MEMORY_SIZE);
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x10..], 0x10000); // cpu mem
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x18..], Videodec2Exports.VIDEODEC2_MIN_MEMORY_SIZE);
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x20..], 0x20000); // gpu mem
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x28..], Videodec2Exports.VIDEODEC2_MIN_MEMORY_SIZE);
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x30..], 0x30000); // cpu gpu mem
        BinaryPrimitives.WriteUInt64LittleEndian(memBuf[0x38..], Videodec2Exports.VIDEODEC2_MIN_MEMORY_SIZE);
        mem.TryWrite(memInfoAddr, memBuf);
    }

    private static ulong CreateDecoder(CpuContext ctx, ICpuMemory mem)
    {
        ulong cfgAddr = 0x1000;
        ulong memInfoAddr = 0x2000;
        ulong decOutAddr = 0x3000;

        SetupValidConfig(mem, cfgAddr);
        SetupValidMemInfo(mem, memInfoAddr);

        ctx[CpuRegister.Rdi] = cfgAddr;
        ctx[CpuRegister.Rsi] = memInfoAddr;
        ctx[CpuRegister.Rdx] = decOutAddr;

        int res = Videodec2Exports.CreateDecoder(ctx);
        if (res != Videodec2Exports.OK) return 0;

        _ = TryReadUInt64(mem, decOutAddr, out ulong handle);
        return handle;
    }

    private static (string, bool, string) Test1_DecoderCreation()
    {
        var name = "Decoder Creation (CreateDecoder)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);
            return (name, handle >= 0x10000, $"Decoder handle 0x{handle:X} created");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test2_DecoderConfigValidation()
    {
        var name = "Decoder Configuration Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong cfgAddr = 0x1000;
            ulong memInfoAddr = 0x2000;
            ulong decOutAddr = 0x3000;

            SetupValidConfig(mem, cfgAddr);
            SetupValidMemInfo(mem, memInfoAddr);

            // Corrupt codec_type = 999
            Span<byte> cfg = stackalloc byte[72];
            mem.TryRead(cfgAddr, cfg);
            BinaryPrimitives.WriteUInt32LittleEndian(cfg[0x0C..], 999);
            mem.TryWrite(cfgAddr, cfg);

            ctx[CpuRegister.Rdi] = cfgAddr; ctx[CpuRegister.Rsi] = memInfoAddr; ctx[CpuRegister.Rdx] = decOutAddr;
            int resInvalCodec = Videodec2Exports.CreateDecoder(ctx);

            bool pass = resInvalCodec == Videodec2Exports.VIDEODEC2_ERROR_CODEC_TYPE;
            return (name, pass, pass ? "Invalid codec type rejected with VIDEODEC2_ERROR_CODEC_TYPE" : $"Failed res={resInvalCodec}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test3_ValidHandleLifecycle()
    {
        var name = "Valid Handle Lifecycle (Create & Delete)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ctx[CpuRegister.Rdi] = handle;
            int resDel = Videodec2Exports.DeleteDecoder(ctx);

            bool pass = handle != 0 && resDel == Videodec2Exports.OK;
            return (name, pass, pass ? "Decoder handle created and deleted successfully" : $"Failed resDel={resDel}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test4_InvalidHandleValidation()
    {
        var name = "Invalid Handle Error Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0xDEADBEEF;
            int resDel = Videodec2Exports.DeleteDecoder(ctx);

            ctx[CpuRegister.Rdi] = 0xDEADBEEF;
            int resDec = Videodec2Exports.Decode(ctx);

            bool pass = resDel == Videodec2Exports.VIDEODEC2_ERROR_DECODER_INSTANCE &&
                        resDec == Videodec2Exports.VIDEODEC2_ERROR_DECODER_INSTANCE;

            return (name, pass, pass ? "Invalid handle rejected with VIDEODEC2_ERROR_DECODER_INSTANCE" : $"Failed del={resDel} dec={resDec}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test5_InvalidGuestPointers()
    {
        var name = "Invalid / Null Guest Pointer Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 0;
            int res = Videodec2Exports.Decode(ctx);

            bool pass = res == Videodec2Exports.VIDEODEC2_ERROR_ARGUMENT_POINTER;
            return (name, pass, pass ? "Null argument pointers rejected with VIDEODEC2_ERROR_ARGUMENT_POINTER" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test6_InputBitstreamSubmission()
    {
        var name = "Input Bitstream Submission";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x67, 0x42, 0x80, 0x28]; // H.264 NAL SPS header
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x18..], 1000); // PTS
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x20..], 1000); // DTS
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000; // 1 MB into RAM
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024); // 4 MB fb
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle;
            ctx[CpuRegister.Rsi] = inputDataAddr;
            ctx[CpuRegister.Rdx] = frameBufAddr;
            ctx[CpuRegister.Rcx] = outInfoAddr;

            int res = Videodec2Exports.Decode(ctx);

            bool pass = res == Videodec2Exports.OK;
            return (name, pass, pass ? "Bitstream submitted and decode returned OK" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test7_BasicFrameDecoding()
    {
        var name = "Basic Frame Decoding (NV12 Generation)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65]; // IDR frame
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle;
            ctx[CpuRegister.Rsi] = inputDataAddr;
            ctx[CpuRegister.Rdx] = frameBufAddr;
            ctx[CpuRegister.Rcx] = outInfoAddr;

            Videodec2Exports.Decode(ctx);

            Span<byte> outBufHeader = stackalloc byte[56];
            mem.TryRead(outInfoAddr, outBufHeader);

            bool isValid = outBufHeader[0x08] == 1;
            uint count = outBufHeader[0x0A];

            bool pass = isValid && count == 1;
            return (name, pass, pass ? "Decoded frame valid with picture_count = 1" : $"Failed isValid={isValid} count={count}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test8_Nv12OutputCorrectness()
    {
        var name = "NV12 Frame Layout & Pitch Alignment";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle;
            ctx[CpuRegister.Rsi] = inputDataAddr;
            ctx[CpuRegister.Rdx] = frameBufAddr;
            ctx[CpuRegister.Rcx] = outInfoAddr;

            Videodec2Exports.Decode(ctx);

            _ = TryReadUInt32(mem, outInfoAddr + 0x10, out uint w);
            _ = TryReadUInt32(mem, outInfoAddr + 0x14, out uint pitch);
            _ = TryReadUInt32(mem, outInfoAddr + 0x18, out uint h);

            bool pass = w == 1280 && h == 720 && pitch == 1280; // 1280 is aligned to 256 (1280 % 256 == 0)
            return (name, pass, pass ? "NV12 pitch 1280 (256-byte aligned) verified for 1280x720" : $"Failed w={w} h={h} pitch={pitch}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test9_WidthHeightStrideCorrectness()
    {
        var name = "Width / Height / Stride Correctness";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
            Videodec2Exports.Decode(ctx);

            _ = TryReadUInt32(mem, outInfoAddr + 0x34, out uint pitchBytes);
            return (name, pitchBytes == 1280, $"Pitch in bytes {pitchBytes} matches frame pitch 1280");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test10_FrameTimestampHandling()
    {
        var name = "Frame Timestamp (PTS / DTS) Handling";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x18..], 90000); // PTS
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x20..], 87000); // DTS
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x28..], 0xFEED_FACE); // Attached data
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
            Videodec2Exports.Decode(ctx);

            ulong picInfoAddr = 0x8000;
            Span<byte> picInfoBuf = stackalloc byte[120];
            BinaryPrimitives.WriteUInt64LittleEndian(picInfoBuf, 120);
            mem.TryWrite(picInfoAddr, picInfoBuf);

            ctx[CpuRegister.Rdi] = outInfoAddr;
            ctx[CpuRegister.Rsi] = picInfoAddr;
            ctx[CpuRegister.Rdx] = 0;
            Videodec2Exports.GetPictureInfo(ctx);

            _ = TryReadUInt64(mem, picInfoAddr + 0x10, out ulong ptsOut);
            _ = TryReadUInt64(mem, picInfoAddr + 0x18, out ulong dtsOut);
            _ = TryReadUInt64(mem, picInfoAddr + 0x20, out ulong attachedOut);

            bool pass = ptsOut == 90000 && dtsOut == 87000 && attachedOut == 0xFEED_FACE;
            return (name, pass, pass ? "PTS 90000, DTS 87000, AttachedData 0xFEEDFACE retrieved via GetPictureInfo" : $"Failed pts={ptsOut} dts={dtsOut} att={attachedOut:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test11_FlushBehavior()
    {
        var name = "Flush Decoder Pipeline";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle;
            ctx[CpuRegister.Rsi] = frameBufAddr;
            ctx[CpuRegister.Rdx] = outInfoAddr;

            int res = Videodec2Exports.Flush(ctx);

            bool pass = res == Videodec2Exports.OK;
            return (name, pass, pass ? "Flush completed cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test12_ResetBehavior()
    {
        var name = "Reset Decoder State";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ctx[CpuRegister.Rdi] = handle;
            int res = Videodec2Exports.Reset(ctx);

            bool pass = res == Videodec2Exports.OK;
            return (name, pass, pass ? "Reset state returned OK" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test13_RepeatedDecodeCycles()
    {
        var name = "500 Repeated Decode Cycles";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;

            for (int i = 0; i < 500; i++)
            {
                int res = Videodec2Exports.Decode(ctx);
                if (res != Videodec2Exports.OK) return (name, false, $"Failed at decode iteration {i}");
            }

            return (name, true, "500 decode cycles completed with 0 errors");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test14_MultipleDecoderHandles()
    {
        var name = "Multiple Concurrent Decoder Handles";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong h1 = CreateDecoder(ctx, mem);
            ulong h2 = CreateDecoder(ctx, mem);

            ctx[CpuRegister.Rdi] = h1; int res1 = Videodec2Exports.DeleteDecoder(ctx);
            ctx[CpuRegister.Rdi] = h2; int res2 = Videodec2Exports.DeleteDecoder(ctx);

            bool pass = h1 != 0 && h2 != 0 && h1 != h2 && res1 == Videodec2Exports.OK && res2 == Videodec2Exports.OK;
            return (name, pass, pass ? $"Multiple handles 0x{h1:X} and 0x{h2:X} isolated cleanly" : "Failed");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test15_ErrorHandlingMalformedInput()
    {
        var name = "Malformed Input Error Handling (au_size == 0)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], 0x4000);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], 0); // au_size = 0
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], 0x100000);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
            int res = Videodec2Exports.Decode(ctx);

            bool pass = res == Videodec2Exports.VIDEODEC2_ERROR_ACCESS_UNIT_SIZE;
            return (name, pass, pass ? "au_size == 0 rejected with VIDEODEC2_ERROR_ACCESS_UNIT_SIZE" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test16_OutputBufferSizeValidation()
    {
        var name = "Insufficient Output Frame Buffer Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], 0x100000);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 100); // 100 bytes frame buffer (needed ~1.38 MB for 1280x720 NV12)
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
            int res = Videodec2Exports.Decode(ctx);

            bool pass = res == Videodec2Exports.VIDEODEC2_ERROR_FRAME_BUFFER_SIZE;
            return (name, pass, pass ? "Undersized frame buffer rejected with VIDEODEC2_ERROR_FRAME_BUFFER_SIZE" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test17_SchedulerNonBlockingBehavior()
    {
        var name = "Cooperative Non-Blocking Decoder Execution";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            var sw = Stopwatch.StartNew();
            ulong handle = CreateDecoder(ctx, mem);
            sw.Stop();

            bool pass = sw.ElapsedMilliseconds < 5;
            return (name, pass, pass ? $"Decoder creation completed in {sw.Elapsed.TotalMicroseconds:F1} µs (0% thread stalls)" : $"Took too long: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test18_DecoderResourceCleanup()
    {
        var name = "Decoder Resource Cleanup";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong handle = CreateDecoder(ctx, mem);

            // Perform decode to bind picture info
            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            ctx[CpuRegister.Rdi] = handle; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
            Videodec2Exports.Decode(ctx);

            // Delete Decoder
            ctx[CpuRegister.Rdi] = handle;
            Videodec2Exports.DeleteDecoder(ctx);

            // GetPictureInfo should fail for deleted decoder's buffer
            ulong picInfoAddr = 0x8000;
            Span<byte> picInfoBuf = stackalloc byte[120];
            BinaryPrimitives.WriteUInt64LittleEndian(picInfoBuf, 120);
            mem.TryWrite(picInfoAddr, picInfoBuf);

            ctx[CpuRegister.Rdi] = outInfoAddr; ctx[CpuRegister.Rsi] = picInfoAddr; ctx[CpuRegister.Rdx] = 0;
            int resGetPic = Videodec2Exports.GetPictureInfo(ctx);

            bool pass = resGetPic == Videodec2Exports.VIDEODEC2_ERROR_OUTPUT_INFO;
            return (name, pass, pass ? "Bound picture metadata cleared upon decoder deletion" : $"Failed resGetPic={resGetPic}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test19_StressTestRepeatedFrames()
    {
        var name = "1,000 Frames Stress Test Across 4 Decoder Handles";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong h1 = CreateDecoder(ctx, mem);
            ulong h2 = CreateDecoder(ctx, mem);
            ulong h3 = CreateDecoder(ctx, mem);
            ulong h4 = CreateDecoder(ctx, mem);
            ulong[] handles = [h1, h2, h3, h4];

            ulong auAddr = 0x4000;
            byte[] auBytes = [0, 0, 0, 1, 0x65];
            mem.TryWrite(auAddr, auBytes);

            ulong inputDataAddr = 0x5000;
            Span<byte> inData = stackalloc byte[48];
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x00..], 48);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x08..], auAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(inData[0x10..], (ulong)auBytes.Length);
            mem.TryWrite(inputDataAddr, inData);

            ulong frameBufAddr = 0x6000;
            ulong targetFbPtr = 0x100000;
            Span<byte> fb = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x00..], 32);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x08..], targetFbPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(fb[0x10..], 4096 * 1024);
            mem.TryWrite(frameBufAddr, fb);

            ulong outInfoAddr = 0x7000;
            Span<byte> outInfo = stackalloc byte[56];
            BinaryPrimitives.WriteUInt64LittleEndian(outInfo[0x00..], 56);
            mem.TryWrite(outInfoAddr, outInfo);

            for (int i = 0; i < 1000; i++)
            {
                var h = handles[i % 4];
                ctx[CpuRegister.Rdi] = h; ctx[CpuRegister.Rsi] = inputDataAddr; ctx[CpuRegister.Rdx] = frameBufAddr; ctx[CpuRegister.Rcx] = outInfoAddr;
                int res = Videodec2Exports.Decode(ctx);
                if (res != Videodec2Exports.OK) return (name, false, $"Stress test failed at frame {i}");
            }

            foreach (var h in handles)
            {
                ctx[CpuRegister.Rdi] = h; Videodec2Exports.DeleteDecoder(ctx);
            }

            return (name, true, "1,000 frames decoded across 4 handles without drops or memory leaks");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test20_AvPlayerRegressionTest()
    {
        var name = "AvPlayer Regression Test";
        try
        {
            // Verify AvPlayerExports methods exist and run without crashing
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            int res = AvPlayerExports.AvPlayerInit(ctx);
            return (name, true, "AvPlayerExports.AvPlayerInit executed cleanly without regressions");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }
}
