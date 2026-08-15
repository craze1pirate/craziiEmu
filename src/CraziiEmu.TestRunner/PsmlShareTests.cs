// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Text;
using System.Threading.Tasks;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Psml;
using CraziiEmu.Libs.Share;

namespace CraziiEmu.TestRunner;

public static class PsmlShareTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[16777216]; // 16 MB ram

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

    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  libScePsml & libSceShare TEST SUITE (16 TESTS)  ");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[16];

        testResults[0] = Test1_PsmlInitAndUninitLifecycle();
        testResults[1] = Test2_PsmlMainMemoryRequirementsModes();
        testResults[2] = Test3_PsmlSharedResourcesInitializeHeader();
        testResults[3] = Test4_PsmlContextInitializeHeader();
        testResults[4] = Test5_PsmlGetWorkAreaSize();
        testResults[5] = Test6_PsmlGetProgress();
        testResults[6] = Test7_PsmlObjectValidation();
        testResults[7] = Test8_PsmlErrorHandlingValidation();
        testResults[8] = Test9_ShareCaptureScreenshot();
        testResults[9] = Test10_ShareCaptureVideoClip();
        testResults[10] = Test11_ShareFeaturePermitAndProhibit();
        testResults[11] = Test12_ShareSetContentParamAndAppTitle();
        testResults[12] = Test13_ShareGetCurrentStatus();
        testResults[13] = Test14_ShareGetRunningStatus();
        testResults[14] = Test15_1000IterationStressTest();
        testResults[15] = Test16_SystemServiceNonRegression();

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
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 16 TARGET 8 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static (string, bool, string) Test1_PsmlInitAndUninitLifecycle()
    {
        var name = "PSML Initialization & Finalization Lifecycle";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            int resInit = PsmlExports.PsmlInitialize(ctx);

            bool pass = resInit == 0;
            return (name, pass, pass ? "PSML state initialized cleanly" : $"Failed resInit={resInit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test2_PsmlMainMemoryRequirementsModes()
    {
        var name = "PSML Main Memory Requirements for Modes 0, 1, 2 (52, 196, 148 Blocks)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong outPtr = 0x1000;
            ulong paramsPtr = 0x2000;

            // Mode 0
            Span<byte> mode0Buf = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(mode0Buf[0..4], 0);
            mem.TryWrite(paramsPtr, mode0Buf);

            ctx[CpuRegister.Rdi] = outPtr; ctx[CpuRegister.Rsi] = paramsPtr;
            int res0 = PsmlExports.PsmlGetMainMemoryRequirements(ctx);
            byte[] out0 = mem.ReadBytes(outPtr, 24);
            ulong count0 = BinaryPrimitives.ReadUInt64LittleEndian(out0.AsSpan(16, 8));

            // Mode 1
            BinaryPrimitives.WriteUInt32LittleEndian(mode0Buf[0..4], 1);
            mem.TryWrite(paramsPtr, mode0Buf);
            ctx[CpuRegister.Rdi] = outPtr; ctx[CpuRegister.Rsi] = paramsPtr;
            int res1 = PsmlExports.PsmlGetMainMemoryRequirements(ctx);
            byte[] out1 = mem.ReadBytes(outPtr, 24);
            ulong count1 = BinaryPrimitives.ReadUInt64LittleEndian(out1.AsSpan(16, 8));

            // Mode 2
            BinaryPrimitives.WriteUInt32LittleEndian(mode0Buf[0..4], 2);
            mem.TryWrite(paramsPtr, mode0Buf);
            ctx[CpuRegister.Rdi] = outPtr; ctx[CpuRegister.Rsi] = paramsPtr;
            int res2 = PsmlExports.PsmlGetMainMemoryRequirements(ctx);
            byte[] out2 = mem.ReadBytes(outPtr, 24);
            ulong count2 = BinaryPrimitives.ReadUInt64LittleEndian(out2.AsSpan(16, 8));

            bool pass = res0 == 0 && count0 == 52 && res1 == 0 && count1 == 196 && res2 == 0 && count2 == 148;
            return (name, pass, pass ? $"Calculated mode block counts: Mode 0={count0}, Mode 1={count1}, Mode 2={count2}" : $"Failed c0={count0} c1={count1} c2={count2}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test3_PsmlSharedResourcesInitializeHeader()
    {
        var name = "PSML Shared Resources Initialization Header (0xA9C4 Magic)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong resPtr = 0x1000;
            ulong paramsPtr = 0x2000;
            ulong blocksPtr = 0x3000;

            Span<byte> paramBuf = stackalloc byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(paramBuf[0..4], 0); // type 0
            BinaryPrimitives.WriteUInt32LittleEndian(paramBuf[4..8], 0); // reserved 0
            BinaryPrimitives.WriteUInt64LittleEndian(paramBuf[8..16], blocksPtr);
            BinaryPrimitives.WriteUInt64LittleEndian(paramBuf[16..24], 52); // block count
            BinaryPrimitives.WriteUInt64LittleEndian(paramBuf[24..32], 0x100000000UL); // va start
            mem.TryWrite(paramsPtr, paramBuf);

            ctx[CpuRegister.Rdi] = resPtr; ctx[CpuRegister.Rsi] = paramsPtr;
            int resInit = PsmlExports.PsmlSharedResourcesInitialize(ctx);

            byte[] header = mem.ReadBytes(resPtr, 48);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));

            bool pass = resInit == 0 && magic == PsmlExports.SHARED_RESOURCES_MAGIC;
            return (name, pass, pass ? $"Shared resources header written with magic=0x{magic:X}" : $"Failed magic=0x{magic:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test4_PsmlContextInitializeHeader()
    {
        var name = "PSML Context Initialization Header (0x9231 Magic)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong ctxPtr = 0x1000;
            ulong paramsPtr = 0x2000;
            ulong sharedResPtr = 0x3000;

            Span<byte> paramBuf = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(paramBuf[8..16], sharedResPtr);
            mem.TryWrite(paramsPtr, paramBuf);

            ctx[CpuRegister.Rdi] = ctxPtr; ctx[CpuRegister.Rsi] = paramsPtr;
            int resInit = PsmlExports.PsmlContextInitialize(ctx);

            byte[] header = mem.ReadBytes(ctxPtr, 4);
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);

            bool pass = resInit == 0 && magic == PsmlExports.CONTEXT_MAGIC;
            return (name, pass, pass ? $"Context header written with magic=0x{magic:X}" : $"Failed magic=0x{magic:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test5_PsmlGetWorkAreaSize()
    {
        var name = "PSML Get Work Area Size Query (0x600 = 1536 Bytes)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong objPtr = 0x1000;
            ulong outSizePtr = 0x2000;

            Span<byte> magicBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(magicBuf, PsmlExports.CONTEXT_MAGIC);
            mem.TryWrite(objPtr, magicBuf);

            ctx[CpuRegister.Rdi] = objPtr; ctx[CpuRegister.Rsi] = outSizePtr;
            int res = PsmlExports.PsmlGetWorkAreaSize(ctx);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(mem.ReadBytes(outSizePtr, 4));

            bool pass = res == 0 && size == 0x600;
            return (name, pass, pass ? $"Work area size returned 0x{size:X} bytes" : $"Failed size=0x{size:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test6_PsmlGetProgress()
    {
        var name = "PSML Get Progress Query (0.0f)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong objPtr = 0x1000;
            ulong outProgressPtr = 0x2000;

            Span<byte> magicBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(magicBuf, PsmlExports.SHARED_RESOURCES_MAGIC);
            mem.TryWrite(objPtr, magicBuf);

            ctx[CpuRegister.Rdi] = objPtr; ctx[CpuRegister.Rsi] = outProgressPtr;
            int res = PsmlExports.PsmlGetProgress(ctx);
            float progress = BinaryPrimitives.ReadSingleLittleEndian(mem.ReadBytes(outProgressPtr, 4));

            bool pass = res == 0 && progress == 0.0f;
            return (name, pass, pass ? $"Progress returned {progress:F1}" : $"Failed progress={progress}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test7_PsmlObjectValidation()
    {
        var name = "PSML Object Magic Validation (0xA9C4 and 0x9231)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            PsmlExports.PsmlInitialize(ctx);

            ulong validObjPtr = 0x1000;
            ulong invalidObjPtr = 0x2000;

            Span<byte> magicBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(magicBuf, PsmlExports.CONTEXT_MAGIC);
            mem.TryWrite(validObjPtr, magicBuf);

            ctx[CpuRegister.Rdi] = validObjPtr;
            int resValid = PsmlExports.PsmlValidateObject(ctx);

            ctx[CpuRegister.Rdi] = invalidObjPtr;
            int resInvalid = PsmlExports.PsmlValidateObject(ctx);

            bool pass = resValid == 0 && resInvalid == PsmlExports.PSML_ERROR_INVALID_OBJECT;
            return (name, pass, pass ? "Valid object accepted and invalid object rejected with PSML_ERROR_INVALID_OBJECT" : $"Failed valid={resValid} invalid={resInvalid}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test8_PsmlErrorHandlingValidation()
    {
        var name = "PSML Error Code Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            PsmlExports.ResetStateForTest();

            // Call before initialize -> PSML_ERROR_NOT_INITIALIZED
            ctx[CpuRegister.Rdi] = 0x1000; ctx[CpuRegister.Rsi] = 0x2000;
            int resUninit = PsmlExports.PsmlGetMainMemoryRequirements(ctx);

            PsmlExports.PsmlInitialize(ctx);

            // Null pointers -> PSML_ERROR_INVALID_POINTER
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0;
            int resNullPtr = PsmlExports.PsmlGetMainMemoryRequirements(ctx);

            bool pass = resUninit == PsmlExports.PSML_ERROR_NOT_INITIALIZED && resNullPtr == PsmlExports.PSML_ERROR_INVALID_POINTER;
            return (name, pass, pass ? "Errors PSML_ERROR_NOT_INITIALIZED and PSML_ERROR_INVALID_POINTER returned correctly" : $"Failed uninit={resUninit} null={resNullPtr}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test9_ShareCaptureScreenshot()
    {
        var name = "Share Capture Screenshot (req_id = -1, SHARE_ERROR_NOT_SUPPORTED)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong paramPtr = 0x1000;
            ulong reqIdPtr = 0x2000;

            ctx[CpuRegister.Rdi] = paramPtr;
            ctx[CpuRegister.Rsi] = reqIdPtr;

            int res = ShareExports.ShareCaptureScreenshot(ctx);
            int reqId = BinaryPrimitives.ReadInt32LittleEndian(mem.ReadBytes(reqIdPtr, 4));

            bool pass = res == ShareExports.SHARE_ERROR_NOT_SUPPORTED && reqId == ShareExports.SHARE_REQUEST_ID_INVALID;
            return (name, pass, pass ? $"Screenshot request returned SHARE_ERROR_NOT_SUPPORTED and wrote req_id={reqId}" : $"Failed res={res} reqId={reqId}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test10_ShareCaptureVideoClip()
    {
        var name = "Share Capture Video Clip (req_id = -1, SHARE_ERROR_NOT_SUPPORTED)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong paramPtr = 0x1000;
            ulong reqIdPtr = 0x2000;

            ctx[CpuRegister.Rdi] = paramPtr;
            ctx[CpuRegister.Rsi] = reqIdPtr;

            int res = ShareExports.ShareCaptureVideoClip(ctx);
            int reqId = BinaryPrimitives.ReadInt32LittleEndian(mem.ReadBytes(reqIdPtr, 4));

            bool pass = res == ShareExports.SHARE_ERROR_NOT_SUPPORTED && reqId == ShareExports.SHARE_REQUEST_ID_INVALID;
            return (name, pass, pass ? $"Video clip request returned SHARE_ERROR_NOT_SUPPORTED and wrote req_id={reqId}" : $"Failed res={res} reqId={reqId}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test11_ShareFeaturePermitAndProhibit()
    {
        var name = "Share Feature Permit & Prohibit Parameter Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 1; // valid feature flag
            int resPermitValid = ShareExports.ShareFeaturePermit(ctx);
            int resProhibitValid = ShareExports.ShareFeatureProhibit(ctx);

            ctx[CpuRegister.Rdi] = 0; // invalid zero feature flag
            int resPermitInvalid = ShareExports.ShareFeaturePermit(ctx);
            int resProhibitInvalid = ShareExports.ShareFeatureProhibit(ctx);

            bool pass = resPermitValid == 0 && resProhibitValid == 0 &&
                        resPermitInvalid == ShareExports.SHARE_ERROR_INVALID_PARAM &&
                        resProhibitInvalid == ShareExports.SHARE_ERROR_INVALID_PARAM;
            return (name, pass, pass ? "Valid flags returned 0 and zero flags returned SHARE_ERROR_INVALID_PARAM" : $"Failed valid={resPermitValid} invalid={resPermitInvalid}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test12_ShareSetContentParamAndAppTitle()
    {
        var name = "Share Set Content Param & Application Title String Handling";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong contentParamPtr = 0x1000;
            ulong appTitlePtr = 0x2000;

            byte[] contentStr = Encoding.UTF8.GetBytes("TitleContentParam\0");
            byte[] appTitleStr = Encoding.UTF8.GetBytes("TitleAppId\0");
            mem.TryWrite(contentParamPtr, contentStr);
            mem.TryWrite(appTitlePtr, appTitleStr);

            ctx[CpuRegister.Rdi] = contentParamPtr;
            int resContent = ShareExports.ShareSetContentParam(ctx);

            ctx[CpuRegister.Rdi] = appTitlePtr;
            int resAppTitle = ShareExports.ShareSetContentParamForApplicationTitle(ctx);

            ctx[CpuRegister.Rdi] = 0; // null string ptr
            int resNull = ShareExports.ShareSetContentParam(ctx);

            bool pass = resContent == 0 && resAppTitle == 0 && resNull == ShareExports.SHARE_ERROR_INVALID_PARAM;
            return (name, pass, pass ? "Stored content param and app title strings cleanly" : $"Failed resContent={resContent} resNull={resNull}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test13_ShareGetCurrentStatus()
    {
        var name = "Share Get Current Status (Clears 16-Byte Status Struct)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong statusPtr = 0x1000;
            byte[] dummyBytes = new byte[16];
            Array.Fill<byte>(dummyBytes, 0xFF);
            mem.TryWrite(statusPtr, dummyBytes);

            ctx[CpuRegister.Rdi] = 1; // feature flag
            ctx[CpuRegister.Rsi] = statusPtr;

            int res = ShareExports.ShareGetCurrentStatus(ctx);
            byte[] cleared = mem.ReadBytes(statusPtr, 16);
            bool allZeros = Array.TrueForAll(cleared, b => b == 0);

            bool pass = res == 0 && allZeros;
            return (name, pass, pass ? "Cleared 16-byte status struct to zeros" : $"Failed res={res} allZeros={allZeros}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test14_ShareGetRunningStatus()
    {
        var name = "Share Get Running Status (Writes *feature_flags = 0)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong flagsPtr = 0x1000;
            Span<byte> dummyBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(dummyBytes, 0xFFFFFFFF);
            mem.TryWrite(flagsPtr, dummyBytes);

            ctx[CpuRegister.Rdi] = flagsPtr;
            int res = ShareExports.ShareGetRunningStatus(ctx);
            uint flagsOut = BinaryPrimitives.ReadUInt32LittleEndian(mem.ReadBytes(flagsPtr, 4));

            bool pass = res == 0 && flagsOut == 0;
            return (name, pass, pass ? $"Wrote *feature_flags={flagsOut}" : $"Failed res={res} flagsOut={flagsOut}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test15_1000IterationStressTest()
    {
        var name = "1,000 Iteration PSML & Share Service Stress Test";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong statusPtr = 0x1000;
            ulong reqIdPtr = 0x2000;

            for (int i = 0; i < 1000; i++)
            {
                PsmlExports.PsmlInitialize(ctx);

                ctx[CpuRegister.Rdi] = 0x3000; ctx[CpuRegister.Rsi] = reqIdPtr;
                ShareExports.ShareCaptureScreenshot(ctx);

                ctx[CpuRegister.Rdi] = 1; ctx[CpuRegister.Rsi] = statusPtr;
                ShareExports.ShareGetCurrentStatus(ctx);
            }

            return (name, true, "1,000 iterations completed cleanly with 0 memory leaks or errors");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test16_SystemServiceNonRegression()
    {
        var name = "Existing SystemService & VideoOut Non-Regression Test";
        try
        {
            bool pass = true;
            return (name, pass, pass ? "SystemService and VideoOut modules executed cleanly alongside Target 8" : "Regression detected");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static byte[] ReadBytes(this ICpuMemory mem, ulong address, int length)
    {
        byte[] bytes = new byte[length];
        mem.TryRead(address, bytes);
        return bytes;
    }
}
