// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner
{
    public static class PthreadTlsTests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("[TEST] Running PthreadTlsTests...");

            TestPthreadKeyCreateAndSetGet();
            TestGuestTcbMemoryDirectRead();
            TestMultipleTlsKeysIsolation();
            TestNullAndKeyDeleteBehavior();
            TestRepeatedSetGetStress();

            Console.WriteLine("[TEST] PthreadTlsTests PASSED successfully.");
        }

        private static void TestPthreadKeyCreateAndSetGet()
        {
            var ctx = CreateMockCpuContext();
            ulong keyPtrAddress = 0x1000;
            ctx[CpuRegister.Rdi] = keyPtrAddress;
            ctx[CpuRegister.Rsi] = 0; // null destructor

            int res = KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            if (res != 0)
            {
                throw new Exception($"PosixPthreadKeyCreate failed with code {res}");
            }

            if (!ctx.TryReadInt32(keyPtrAddress, out int key))
            {
                throw new Exception("Failed to read key from outKeyAddress");
            }

            const ulong testValue = 0xDEADBEEFCAFEBABE;
            ctx[CpuRegister.Rdi] = (ulong)key;
            ctx[CpuRegister.Rsi] = testValue;

            res = KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);
            if (res != 0)
            {
                throw new Exception($"PosixPthreadSetspecific failed with code {res}");
            }

            // Verify via pthread_getspecific
            ctx[CpuRegister.Rdi] = (ulong)key;
            res = KernelPthreadExtendedCompatExports.PosixPthreadGetspecific(ctx);
            if (res != 0)
            {
                throw new Exception($"PosixPthreadGetspecific failed with code {res}");
            }

            ulong gotValue = ctx[CpuRegister.Rax];
            if (gotValue != testValue)
            {
                throw new Exception($"pthread_getspecific mismatch: expected 0x{testValue:X16}, got 0x{gotValue:X16}");
            }

            Console.WriteLine("  [PASS] TestPthreadKeyCreateAndSetGet");
        }

        private static void TestGuestTcbMemoryDirectRead()
        {
            var ctx = CreateMockCpuContext();
            ulong keyPtrAddress = 0x1008;
            ctx[CpuRegister.Rdi] = keyPtrAddress;
            ctx[CpuRegister.Rsi] = 0;

            KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            ctx.TryReadInt32(keyPtrAddress, out int key);

            const ulong testValue = 0x123456789ABCDEF0;
            ctx[CpuRegister.Rdi] = (ulong)key;
            ctx[CpuRegister.Rsi] = testValue;
            KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);

            // Direct guest assembly style memory dereference at FsBase + 0x400 + key * 8
            ulong slotAddress = ctx.FsBase + 0x400 + ((ulong)key * sizeof(ulong));
            if (!ctx.TryReadUInt64(slotAddress, out ulong directTcbValue))
            {
                throw new Exception($"Failed to read guest TCB memory slot at 0x{slotAddress:X16}");
            }

            if (directTcbValue != testValue)
            {
                throw new Exception($"Guest TCB direct memory mismatch: expected 0x{testValue:X16}, got 0x{directTcbValue:X16}");
            }

            Console.WriteLine("  [PASS] TestGuestTcbMemoryDirectRead");
        }

        private static void TestMultipleTlsKeysIsolation()
        {
            var ctx = CreateMockCpuContext();
            ulong keyPtr1 = 0x1010;
            ulong keyPtr2 = 0x1014;

            ctx[CpuRegister.Rdi] = keyPtr1;
            KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            ctx.TryReadInt32(keyPtr1, out int key1);

            ctx[CpuRegister.Rdi] = keyPtr2;
            KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            ctx.TryReadInt32(keyPtr2, out int key2);

            const ulong val1 = 0xAAAA_BBBB_CCCC_DDDD;
            const ulong val2 = 0x1111_2222_3333_4444;

            ctx[CpuRegister.Rdi] = (ulong)key1;
            ctx[CpuRegister.Rsi] = val1;
            KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);

            ctx[CpuRegister.Rdi] = (ulong)key2;
            ctx[CpuRegister.Rsi] = val2;
            KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);

            // Read key1
            ctx[CpuRegister.Rdi] = (ulong)key1;
            KernelPthreadExtendedCompatExports.PosixPthreadGetspecific(ctx);
            if (ctx[CpuRegister.Rax] != val1)
            {
                throw new Exception("Key1 value isolation failed");
            }

            // Read key2
            ctx[CpuRegister.Rdi] = (ulong)key2;
            KernelPthreadExtendedCompatExports.PosixPthreadGetspecific(ctx);
            if (ctx[CpuRegister.Rax] != val2)
            {
                throw new Exception("Key2 value isolation failed");
            }

            Console.WriteLine("  [PASS] TestMultipleTlsKeysIsolation");
        }

        private static void TestNullAndKeyDeleteBehavior()
        {
            var ctx = CreateMockCpuContext();
            ulong keyPtr = 0x1020;
            ctx[CpuRegister.Rdi] = keyPtr;
            KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            ctx.TryReadInt32(keyPtr, out int key);

            ctx[CpuRegister.Rdi] = (ulong)key;
            ctx[CpuRegister.Rsi] = 0x9999;
            KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);

            // Delete key
            ctx[CpuRegister.Rdi] = (ulong)key;
            int delRes = KernelPthreadExtendedCompatExports.PosixPthreadKeyDelete(ctx);
            if (delRes != 0)
            {
                throw new Exception($"PosixPthreadKeyDelete failed with code {delRes}");
            }

            // TCB slot must be zeroed
            ulong slotAddress = ctx.FsBase + 0x400 + ((ulong)key * sizeof(ulong));
            ctx.TryReadUInt64(slotAddress, out ulong slotVal);
            if (slotVal != 0)
            {
                throw new Exception($"Guest TCB memory slot was not cleared after key delete; got 0x{slotVal:X16}");
            }

            Console.WriteLine("  [PASS] TestNullAndKeyDeleteBehavior");
        }

        private static void TestRepeatedSetGetStress()
        {
            var ctx = CreateMockCpuContext();
            ulong keyPtr = 0x1030;
            ctx[CpuRegister.Rdi] = keyPtr;
            KernelPthreadExtendedCompatExports.PosixPthreadKeyCreate(ctx);
            ctx.TryReadInt32(keyPtr, out int key);

            for (ulong iteration = 1; iteration <= 1000; iteration++)
            {
                ulong value = iteration * 0x0102030405060708UL;
                ctx[CpuRegister.Rdi] = (ulong)key;
                ctx[CpuRegister.Rsi] = value;
                KernelPthreadExtendedCompatExports.PosixPthreadSetspecific(ctx);

                ctx[CpuRegister.Rdi] = (ulong)key;
                KernelPthreadExtendedCompatExports.PosixPthreadGetspecific(ctx);
                if (ctx[CpuRegister.Rax] != value)
                {
                    throw new Exception($"Stress test failed on iteration {iteration}");
                }
            }

            Console.WriteLine("  [PASS] TestRepeatedSetGetStress");
        }

        private static CpuContext CreateMockCpuContext()
        {
            var ctx = new CpuContext(new MockMemory(), Generation.Gen5);
            ctx.FsBase = 0x500000; // Allocate mock FsBase TCB address
            return ctx;
        }

        private class MockMemory : ICpuMemory
        {
            private readonly byte[] _ram = new byte[0x1000000];

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
        }
    }
}
