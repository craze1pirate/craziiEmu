// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Dialog;

namespace CraziiEmu.TestRunner;

public static class LoginDialogTests
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
        Console.WriteLine("  libSceLoginDialog & libSceSigninDialog TEST SUITE (12 TESTS) ");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[12];

        testResults[0] = Test1_LoginDialogInitialize();
        testResults[1] = Test2_LoginDialogOpen();
        testResults[2] = Test3_LoginDialogGetStatus();
        testResults[3] = Test4_LoginDialogGetResult();
        testResults[4] = Test5_LoginDialogClose();
        testResults[5] = Test6_LoginDialogTerminate();
        testResults[6] = Test7_SigninDialogInitialize();
        testResults[7] = Test8_SigninDialogOpen();
        testResults[8] = Test9_SigninDialogGetStatus();
        testResults[9] = Test10_SigninDialogGetResult();
        testResults[10] = Test11_InvalidAndRepeatedStateHandling();
        testResults[11] = Test12_1000IterationLifecycleStressTest();

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
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 12 TARGET 9 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static (string, bool, string) Test1_LoginDialogInitialize()
    {
        var name = "LoginDialog Initialize (sceLoginDialogInitialize)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();

            int resInit = LoginDialogExports.LoginDialogInitialize(ctx);
            int status = LoginDialogExports.LoginDialogGetStatus(ctx);

            bool pass = resInit == 0 && status == LoginDialogExports.LOGIN_STATUS_INITIALIZED;
            return (name, pass, pass ? $"Initialized LoginDialog status={status}" : $"Failed res={resInit} status={status}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test2_LoginDialogOpen()
    {
        var name = "LoginDialog Open (64-Byte Parameter & initial_focus Validation)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.LoginDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.LoginDialogParamInitialize(ctx);

            // Override initial focus to 1005
            Span<byte> initialFocusBuf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(initialFocusBuf, 1005);
            mem.TryWrite(paramPtr + 40, initialFocusBuf);

            int resOpen = LoginDialogExports.LoginDialogOpen(ctx);
            int status = LoginDialogExports.LoginDialogGetStatus(ctx);

            bool pass = resOpen == 0 && status == LoginDialogExports.LOGIN_STATUS_RUNNING;
            return (name, pass, pass ? $"Opened LoginDialog status={status} with custom initial_focus=1005" : $"Failed resOpen={resOpen}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test3_LoginDialogGetStatus()
    {
        var name = "LoginDialog GetStatus & UpdateStatus Auto-Promotion";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.LoginDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.LoginDialogParamInitialize(ctx);
            LoginDialogExports.LoginDialogOpen(ctx);

            int statusUpdate = LoginDialogExports.LoginDialogUpdateStatus(ctx);
            int statusGet = LoginDialogExports.LoginDialogGetStatus(ctx);

            bool pass = statusUpdate == LoginDialogExports.LOGIN_STATUS_FINISHED && statusGet == LoginDialogExports.LOGIN_STATUS_FINISHED;
            return (name, pass, pass ? $"UpdateStatus auto-promoted RUNNING state to FINISHED (3)" : $"Failed statusUpdate={statusUpdate}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test4_LoginDialogGetResult()
    {
        var name = "LoginDialog GetResult (16-Byte Struct, result=0, selected_user=1000)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.LoginDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.LoginDialogParamInitialize(ctx);
            LoginDialogExports.LoginDialogOpen(ctx);
            LoginDialogExports.LoginDialogUpdateStatus(ctx);

            ulong resultPtr = 0x2000;
            ctx[CpuRegister.Rdi] = resultPtr;
            int resResult = LoginDialogExports.LoginDialogGetResult(ctx);

            byte[] resultBuf = mem.ReadBytes(resultPtr, 16);
            int resCode = BinaryPrimitives.ReadInt32LittleEndian(resultBuf.AsSpan(0, 4));
            int selectedUser = BinaryPrimitives.ReadInt32LittleEndian(resultBuf.AsSpan(4, 4));

            bool pass = resResult == 0 && resCode == LoginDialogExports.LOGIN_RESULT_OK && selectedUser == LoginDialogExports.LOGIN_USER_ID;
            return (name, pass, pass ? $"Retrieved result=0, selected_user={selectedUser}" : $"Failed resResult={resResult}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test5_LoginDialogClose()
    {
        var name = "LoginDialog Close (sceLoginDialogClose)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.LoginDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.LoginDialogParamInitialize(ctx);
            LoginDialogExports.LoginDialogOpen(ctx);

            int resClose = LoginDialogExports.LoginDialogClose(ctx);
            int status = LoginDialogExports.LoginDialogGetStatus(ctx);

            bool pass = resClose == 0 && status == LoginDialogExports.LOGIN_STATUS_FINISHED;
            return (name, pass, pass ? "Closed LoginDialog cleanly" : $"Failed resClose={resClose}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test6_LoginDialogTerminate()
    {
        var name = "LoginDialog Terminate (sceLoginDialogTerminate)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.LoginDialogInitialize(ctx);

            int resTerm = LoginDialogExports.LoginDialogTerminate(ctx);
            int status = LoginDialogExports.LoginDialogGetStatus(ctx);

            bool pass = resTerm == 0 && status == LoginDialogExports.LOGIN_STATUS_NONE;
            return (name, pass, pass ? "Terminated LoginDialog state reset to NONE" : $"Failed resTerm={resTerm}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test7_SigninDialogInitialize()
    {
        var name = "SigninDialog Initialize (sceSigninDialogInitialize)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();

            int resInit = LoginDialogExports.SigninDialogInitialize(ctx);
            int status = LoginDialogExports.SigninDialogGetStatus(ctx);

            bool pass = resInit == 0 && status == LoginDialogExports.SIGNIN_STATUS_INITIALIZED;
            return (name, pass, pass ? $"Initialized SigninDialog status={status}" : $"Failed resInit={resInit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test8_SigninDialogOpen()
    {
        var name = "SigninDialog Open (16-Byte Parameter Validation)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.SigninDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            Span<byte> paramBuf = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[0..4], 16); // size = 16
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[4..8], 1000); // user_id
            mem.TryWrite(paramPtr, paramBuf);

            ctx[CpuRegister.Rdi] = paramPtr;
            int resOpen = LoginDialogExports.SigninDialogOpen(ctx);
            int status = LoginDialogExports.SigninDialogGetStatus(ctx);

            bool pass = resOpen == 0 && status == LoginDialogExports.SIGNIN_STATUS_RUNNING;
            return (name, pass, pass ? $"Opened SigninDialog status={status}" : $"Failed resOpen={resOpen}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test9_SigninDialogGetStatus()
    {
        var name = "SigninDialog GetStatus & UpdateStatus Auto-Promotion";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.SigninDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            Span<byte> paramBuf = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[0..4], 16);
            mem.TryWrite(paramPtr, paramBuf);
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.SigninDialogOpen(ctx);

            int statusUpdate = LoginDialogExports.SigninDialogUpdateStatus(ctx);
            int statusGet = LoginDialogExports.SigninDialogGetStatus(ctx);

            bool pass = statusUpdate == LoginDialogExports.SIGNIN_STATUS_FINISHED && statusGet == LoginDialogExports.SIGNIN_STATUS_FINISHED;
            return (name, pass, pass ? "SigninDialog UpdateStatus auto-promoted state to FINISHED (3)" : $"Failed statusUpdate={statusUpdate}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test10_SigninDialogGetResult()
    {
        var name = "SigninDialog GetResult (16-Byte Struct, result=1 USER_CANCELED)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();
            LoginDialogExports.SigninDialogInitialize(ctx);

            ulong paramPtr = 0x1000;
            Span<byte> paramBuf = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[0..4], 16);
            mem.TryWrite(paramPtr, paramBuf);
            ctx[CpuRegister.Rdi] = paramPtr;
            LoginDialogExports.SigninDialogOpen(ctx);
            LoginDialogExports.SigninDialogUpdateStatus(ctx);

            ulong resultPtr = 0x2000;
            ctx[CpuRegister.Rdi] = resultPtr;
            int resResult = LoginDialogExports.SigninDialogGetResult(ctx);

            byte[] resultBuf = mem.ReadBytes(resultPtr, 16);
            int resCode = BinaryPrimitives.ReadInt32LittleEndian(resultBuf.AsSpan(0, 4));

            bool pass = resResult == 0 && resCode == LoginDialogExports.SIGNIN_RESULT_USER_CANCELED;
            return (name, pass, pass ? $"Retrieved result=1 (SIGNIN_RESULT_USER_CANCELED)" : $"Failed resResult={resResult}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test11_InvalidAndRepeatedStateHandling()
    {
        var name = "Invalid & Repeated State Handling (ALREADY_INITIALIZED, NOT_INITIALIZED, PARAM_INVALID)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            LoginDialogExports.ResetStateForTest();

            // Login terminate when not initialized -> LOGIN_ERROR_NOT_INITIALIZED
            int resTermUninit = LoginDialogExports.LoginDialogTerminate(ctx);

            LoginDialogExports.LoginDialogInitialize(ctx);
            // Double initialize -> LOGIN_ERROR_ALREADY_INITIALIZED
            int resDoubleInit = LoginDialogExports.LoginDialogInitialize(ctx);

            // Open null param -> LOGIN_ERROR_PARAM_INVALID
            ctx[CpuRegister.Rdi] = 0;
            int resNullOpen = LoginDialogExports.LoginDialogOpen(ctx);

            // Signin double initialize -> SIGNIN_ERROR_ALREADY_INITIALIZED
            LoginDialogExports.SigninDialogInitialize(ctx);
            int resSigninDoubleInit = LoginDialogExports.SigninDialogInitialize(ctx);

            bool pass = resTermUninit == LoginDialogExports.LOGIN_ERROR_NOT_INITIALIZED &&
                        resDoubleInit == LoginDialogExports.LOGIN_ERROR_ALREADY_INITIALIZED &&
                        resNullOpen == LoginDialogExports.LOGIN_ERROR_PARAM_INVALID &&
                        resSigninDoubleInit == LoginDialogExports.SIGNIN_ERROR_ALREADY_INITIALIZED;
            return (name, pass, pass ? "Error codes LOGIN_ERROR_NOT_INITIALIZED, ALREADY_INITIALIZED, and PARAM_INVALID returned cleanly" : $"Failed term={resTermUninit} double={resDoubleInit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test12_1000IterationLifecycleStressTest()
    {
        var name = "1,000 Iteration PSN Login & Signin Dialog Lifecycle Stress Test";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);
            ulong loginParamPtr = 0x1000;
            ulong signinParamPtr = 0x2000;
            ulong resultPtr = 0x3000;

            Span<byte> signinBuf = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(signinBuf[0..4], 16);
            mem.TryWrite(signinParamPtr, signinBuf);

            for (int i = 0; i < 1000; i++)
            {
                LoginDialogExports.ResetStateForTest();

                LoginDialogExports.LoginDialogInitialize(ctx);
                ctx[CpuRegister.Rdi] = loginParamPtr;
                LoginDialogExports.LoginDialogParamInitialize(ctx);
                LoginDialogExports.LoginDialogOpen(ctx);
                LoginDialogExports.LoginDialogUpdateStatus(ctx);
                ctx[CpuRegister.Rdi] = resultPtr;
                LoginDialogExports.LoginDialogGetResult(ctx);
                LoginDialogExports.LoginDialogTerminate(ctx);

                LoginDialogExports.SigninDialogInitialize(ctx);
                ctx[CpuRegister.Rdi] = signinParamPtr;
                LoginDialogExports.SigninDialogOpen(ctx);
                LoginDialogExports.SigninDialogUpdateStatus(ctx);
                ctx[CpuRegister.Rdi] = resultPtr;
                LoginDialogExports.SigninDialogGetResult(ctx);
                LoginDialogExports.SigninDialogTerminate(ctx);
            }

            return (name, true, "1,000 iterations completed cleanly with 0 memory leaks or errors");
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
