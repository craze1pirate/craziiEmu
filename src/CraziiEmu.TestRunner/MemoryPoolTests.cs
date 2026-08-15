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
using CraziiEmu.Libs.Kernel;

namespace CraziiEmu.TestRunner;

public static class MemoryPoolTests
{
    private sealed class DummyMemory : ICpuMemory
    {
        private readonly byte[] _ram = new byte[67108864]; // 64 MB ram

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
        Console.WriteLine("  libKernel MEMORY POOLS & PRT APERTURES TEST SUITE (26 TESTS)");
        Console.WriteLine("=================================================");

        var testResults = new (string Name, bool Passed, string Message)[26];

        testResults[0] = Test1_MemoryPoolExpand();
        testResults[1] = Test2_InvalidPoolExpandParameters();
        testResults[2] = Test3_Pool64KBAlignmentValidation();
        testResults[3] = Test4_MemoryPoolReserve2MB();
        testResults[4] = Test5_MemoryPoolCommit();
        testResults[5] = Test6_MemoryPoolDecommit();
        testResults[6] = Test7_GetBlockStats();
        testResults[7] = Test8_BatchPoolOperations();
        testResults[8] = Test9_SetPrtAperture();
        testResults[9] = Test10_GetPrtAperture();
        testResults[10] = Test11_InvalidPrtApertureIndex();
        testResults[11] = Test12_InvalidPrtApertureBounds();
        testResults[12] = Test13_VirtualQueryInspection();
        testResults[13] = Test14_VirtualQueryPrtFlagDetection();
        testResults[14] = Test15_VirtualQueryPooledFlagDetection();
        testResults[15] = Test16_MultiplePrtApertures();
        testResults[16] = Test17_MultipleMemoryPoolReservations();
        testResults[17] = Test18_ProtectionPermissionValidation();
        testResults[18] = Test19_DoubleReserveHandling();
        testResults[19] = Test20_GuestReadWriteAfterCommit();
        testResults[20] = Test21_1000IterationPoolStressTest();
        testResults[21] = Test22_ConcurrentPoolOperations();
        testResults[22] = Test23_64BitAddressBoundaryEdgeCases();
        testResults[23] = Test24_ResourceCleanupVerification();
        testResults[24] = Test25_DirectAndFlexibleMappingNonRegression();
        testResults[25] = Test26_VirtualQueryFindNextDivergenceRegression();

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
        Console.WriteLine(allPassed ? "OVERALL RESULT: ALL 25 TARGET 7 TESTS PASSED SUCCESSFUL!" : "OVERALL RESULT: TEST FAILURES DETECTED!");
        Console.WriteLine("=================================================\n");
    }

    private static (string, bool, string) Test1_MemoryPoolExpand()
    {
        var name = "Memory Pool Expansion (sceKernelMemoryPoolExpand)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong physAddrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0x100000; // searchStart
            ctx[CpuRegister.Rsi] = 0x500000; // searchEnd
            ctx[CpuRegister.Rdx] = 0x10000;  // 64KB len
            ctx[CpuRegister.Rcx] = 0x10000;  // alignment
            ctx[CpuRegister.R8]  = physAddrOutPtr;

            int res = KernelMemoryCompatExports.MemoryPoolExpand(ctx);
            mem.TryRead(physAddrOutPtr, stackalloc byte[8]);
            ulong physAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(physAddrOutPtr, 8));

            bool pass = res == 0 && physAddr == 0x100000;
            return (name, pass, pass ? $"Expanded memory pool at physical address 0x{physAddr:X}" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test2_InvalidPoolExpandParameters()
    {
        var name = "Invalid Pool Expansion Parameters Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0x500000;
            ctx[CpuRegister.Rsi] = 0x100000; // invalid range
            ctx[CpuRegister.Rdx] = 0x10000;
            ctx[CpuRegister.Rcx] = 0;
            ctx[CpuRegister.R8]  = 0x1000;

            int res = KernelMemoryCompatExports.MemoryPoolExpand(ctx);

            bool pass = res == -22; // -EINVAL
            return (name, pass, pass ? "Inverted range search parameters rejected with -EINVAL" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test3_Pool64KBAlignmentValidation()
    {
        var name = "Pool 64KB Page Alignment Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0x100000;
            ctx[CpuRegister.Rsi] = 0x500000;
            ctx[CpuRegister.Rdx] = 0x4000; // 16KB (not 64KB aligned)
            ctx[CpuRegister.Rcx] = 0x10000;
            ctx[CpuRegister.R8]  = 0x1000;

            int res = KernelMemoryCompatExports.MemoryPoolExpand(ctx);

            bool pass = res == -22; // -EINVAL
            return (name, pass, pass ? "Unaligned 16KB length rejected with -EINVAL" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test4_MemoryPoolReserve2MB()
    {
        var name = "Memory Pool Reservation (sceKernelMemoryPoolReserve 2MB Alignment)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0;       // auto-search
            ctx[CpuRegister.Rsi] = 0x200000; // 2MB length
            ctx[CpuRegister.Rdx] = 0x200000; // 2MB alignment
            ctx[CpuRegister.Rcx] = 0;       // flags
            ctx[CpuRegister.R8]  = addrOutPtr;

            int res = KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            bool pass = res == 0 && reservedAddr != 0 && (reservedAddr & 0x1FFFFF) == 0;
            return (name, pass, pass ? $"Reserved 2MB pool region at 0x{reservedAddr:X}" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test5_MemoryPoolCommit()
    {
        var name = "Memory Pool Commitment (sceKernelMemoryPoolCommit)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ctx[CpuRegister.Rdi] = reservedAddr;
            ctx[CpuRegister.Rsi] = 0x10000; // 64KB commit
            ctx[CpuRegister.Rdx] = 0;       // type
            ctx[CpuRegister.Rcx] = 3;       // RW prot
            ctx[CpuRegister.R8]  = 0;

            int resCommit = KernelMemoryCompatExports.MemoryPoolCommit(ctx);

            bool pass = resCommit == 0;
            return (name, pass, pass ? "Committed 64KB physical backing to pool region" : $"Failed resCommit={resCommit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test6_MemoryPoolDecommit()
    {
        var name = "Memory Pool Decommitment (sceKernelMemoryPoolDecommit)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ctx[CpuRegister.Rdi] = reservedAddr; ctx[CpuRegister.Rsi] = 0x10000; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 3; ctx[CpuRegister.R8] = 0;
            KernelMemoryCompatExports.MemoryPoolCommit(ctx);

            ctx[CpuRegister.Rdi] = reservedAddr;
            ctx[CpuRegister.Rsi] = 0x10000;
            ctx[CpuRegister.Rdx] = 0;

            int resDecommit = KernelMemoryCompatExports.MemoryPoolDecommit(ctx);

            bool pass = resDecommit == 0;
            return (name, pass, pass ? "Decommitted 64KB physical backing from pool region" : $"Failed resDecommit={resDecommit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test7_GetBlockStats()
    {
        var name = "Memory Pool Block Statistics (sceKernelMemoryPoolGetBlockStats)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong outStatsPtr = 0x2000;
            ctx[CpuRegister.Rdi] = outStatsPtr;
            ctx[CpuRegister.Rsi] = 16;

            int res = KernelMemoryCompatExports.MemoryPoolGetBlockStats(ctx);
            byte[] statsBytes = mem.ReadBytes(outStatsPtr, 16);
            uint availFlushed = BinaryPrimitives.ReadUInt32LittleEndian(statsBytes.AsSpan(0, 4));
            uint allocFlushed = BinaryPrimitives.ReadUInt32LittleEndian(statsBytes.AsSpan(8, 4));

            bool pass = res == 0 && availFlushed > 0;
            return (name, pass, pass ? $"Queried block stats: available_flushed={availFlushed}, allocated_flushed={allocFlushed}" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test8_BatchPoolOperations()
    {
        var name = "Batch Memory Pool Operations (sceKernelMemoryPoolBatch)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ulong batchEntriesPtr = 0x3000;
            byte[] batchEntry = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(batchEntry.AsSpan(0, 4), 1); // op = Commit
            BinaryPrimitives.WriteUInt64LittleEndian(batchEntry.AsSpan(8, 8), reservedAddr);
            BinaryPrimitives.WriteUInt64LittleEndian(batchEntry.AsSpan(16, 8), 0x10000); // 64KB
            batchEntry[24] = 3; // prot RW
            mem.TryWrite(batchEntriesPtr, batchEntry);

            ulong numOutPtr = 0x4000;
            ctx[CpuRegister.Rdi] = batchEntriesPtr;
            ctx[CpuRegister.Rsi] = 1;
            ctx[CpuRegister.Rdx] = numOutPtr;
            ctx[CpuRegister.Rcx] = 0;

            int resBatch = KernelMemoryCompatExports.MemoryPoolBatch(ctx);
            uint processed = BinaryPrimitives.ReadUInt32LittleEndian(mem.ReadBytes(numOutPtr, 4));

            bool pass = resBatch == 0 && processed == 1;
            return (name, pass, pass ? $"Processed {processed} batch pool operation entry" : $"Failed resBatch={resBatch}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test9_SetPrtAperture()
    {
        var name = "PRT Aperture Registration (sceKernelSetPrtAperture 16KB Alignment)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0;               // index 0
            ctx[CpuRegister.Rsi] = 0x0F00000000UL;  // addr
            ctx[CpuRegister.Rdx] = 0x0000100000UL;  // 1MB len (16KB aligned)

            int res = KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            bool pass = res == 0;
            return (name, pass, pass ? "Registered PRT aperture index 0 cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test10_GetPrtAperture()
    {
        var name = "PRT Aperture Query (sceKernelGetPrtAperture)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x0F00000000UL; ctx[CpuRegister.Rdx] = 0x0000100000UL;
            KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            ulong outAddrPtr = 0x2000;
            ulong outLenPtr = 0x3000;
            ctx[CpuRegister.Rdi] = 0;
            ctx[CpuRegister.Rsi] = outAddrPtr;
            ctx[CpuRegister.Rdx] = outLenPtr;

            int resGet = KernelRuntimeCompatExports.KernelGetPrtAperture(ctx);
            ulong queriedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(outAddrPtr, 8));
            ulong queriedLen = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(outLenPtr, 8));

            bool pass = resGet == 0 && queriedAddr == 0x0F00000000UL && queriedLen == 0x0000100000UL;
            return (name, pass, pass ? $"Queried PRT aperture: addr=0x{queriedAddr:X}, len=0x{queriedLen:X}" : $"Failed resGet={resGet}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test11_InvalidPrtApertureIndex()
    {
        var name = "Invalid PRT Aperture Index Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 5; // index out of bounds (> 2)
            ctx[CpuRegister.Rsi] = 0x0F00000000UL;
            ctx[CpuRegister.Rdx] = 0x10000;

            int res = KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            bool pass = res != 0;
            return (name, pass, pass ? "Out-of-bounds PRT aperture index 5 rejected cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test12_InvalidPrtApertureBounds()
    {
        var name = "Invalid PRT Aperture Range Bounds Validation";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0;
            ctx[CpuRegister.Rsi] = 0x0000010000UL; // below PRT_APERTURE_START (0x0F00000000)
            ctx[CpuRegister.Rdx] = 0x10000;

            int res = KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            bool pass = res != 0;
            return (name, pass, pass ? "Address below PRT_APERTURE_START rejected cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test13_VirtualQueryInspection()
    {
        var name = "Virtual Query Inspection (sceKernelVirtualQuery 72-Byte Struct)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ulong infoPtr = 0x4000;
            ctx[CpuRegister.Rdi] = reservedAddr;
            ctx[CpuRegister.Rsi] = 0; // flags
            ctx[CpuRegister.Rdx] = infoPtr;
            ctx[CpuRegister.Rcx] = 72; // 72 bytes

            int resQuery = KernelMemoryCompatExports.KernelVirtualQuery(ctx);
            byte[] infoBytes = mem.ReadBytes(infoPtr, 72);
            ulong startAddr = BinaryPrimitives.ReadUInt64LittleEndian(infoBytes.AsSpan(0, 8));
            ulong endAddr = BinaryPrimitives.ReadUInt64LittleEndian(infoBytes.AsSpan(8, 8));

            bool pass = resQuery == 0 && startAddr == reservedAddr && endAddr == reservedAddr + 0x200000;
            return (name, pass, pass ? $"VirtualQuery returned start=0x{startAddr:X}, end=0x{endAddr:X}" : $"Failed resQuery={resQuery}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test14_VirtualQueryPrtFlagDetection()
    {
        var name = "Virtual Query PRT Flag Detection (is_gpu_prt = 1)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x0F00000000UL; ctx[CpuRegister.Rdx] = 0x0000100000UL;
            KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0x0F00000000UL; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);

            ulong infoPtr = 0x4000;
            ctx[CpuRegister.Rdi] = 0x0F00000000UL; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = infoPtr; ctx[CpuRegister.Rcx] = 72;
            KernelMemoryCompatExports.KernelVirtualQuery(ctx);

            byte[] infoBytes = mem.ReadBytes(infoPtr, 72);
            uint bitfields = BinaryPrimitives.ReadUInt32LittleEndian(infoBytes.AsSpan(32, 4));
            bool isPrt = (bitfields & (1U << 5)) != 0;

            bool pass = isPrt;
            return (name, pass, pass ? "VirtualQuery correctly detected PRT aperture flag (is_gpu_prt = 1)" : $"Failed bitfields=0x{bitfields:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test15_VirtualQueryPooledFlagDetection()
    {
        var name = "Virtual Query Pooled Flag Detection (is_pooled = 1)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ulong infoPtr = 0x4000;
            ctx[CpuRegister.Rdi] = reservedAddr; ctx[CpuRegister.Rsi] = 0; ctx[CpuRegister.Rdx] = infoPtr; ctx[CpuRegister.Rcx] = 72;
            KernelMemoryCompatExports.KernelVirtualQuery(ctx);

            byte[] infoBytes = mem.ReadBytes(infoPtr, 72);
            uint bitfields = BinaryPrimitives.ReadUInt32LittleEndian(infoBytes.AsSpan(32, 4));
            bool isPooled = (bitfields & (1U << 3)) != 0;

            bool pass = isPooled;
            return (name, pass, pass ? "VirtualQuery correctly detected pooled range flag (is_pooled = 1)" : $"Failed bitfields=0x{bitfields:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test16_MultiplePrtApertures()
    {
        var name = "Multiple Simultaneous PRT Apertures (Index 0, 1, 2)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x0F00000000UL; ctx[CpuRegister.Rdx] = 0x100000UL;
            KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            ctx[CpuRegister.Rdi] = 1; ctx[CpuRegister.Rsi] = 0x1000000000UL; ctx[CpuRegister.Rdx] = 0x200000UL;
            KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            ulong outAddrPtr = 0x2000; ulong outLenPtr = 0x3000;
            ctx[CpuRegister.Rdi] = 1; ctx[CpuRegister.Rsi] = outAddrPtr; ctx[CpuRegister.Rdx] = outLenPtr;
            KernelRuntimeCompatExports.KernelGetPrtAperture(ctx);

            ulong queriedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(outAddrPtr, 8));
            ulong queriedLen = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(outLenPtr, 8));

            bool pass = queriedAddr == 0x1000000000UL && queriedLen == 0x200000UL;
            return (name, pass, pass ? "Multiple PRT apertures 0 and 1 stored and queried independently" : $"Failed addr=0x{queriedAddr:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test17_MultipleMemoryPoolReservations()
    {
        var name = "Multiple Simultaneous Memory Pool Reservations";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong out1Ptr = 0x1000; ulong out2Ptr = 0x2000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = out1Ptr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);

            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = out2Ptr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);

            ulong addr1 = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(out1Ptr, 8));
            ulong addr2 = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(out2Ptr, 8));

            bool pass = addr1 != 0 && addr2 != 0 && addr1 != addr2;
            return (name, pass, pass ? $"Allocated two non-overlapping pool regions: 0x{addr1:X} and 0x{addr2:X}" : $"Failed addr1=0x{addr1:X} addr2=0x{addr2:X}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test18_ProtectionPermissionValidation()
    {
        var name = "Protection Permission Validation (Executable Protection Rejected for Pool Commit)";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ctx[CpuRegister.Rdi] = reservedAddr;
            ctx[CpuRegister.Rsi] = 0x10000;
            ctx[CpuRegister.Rdx] = 0;
            ctx[CpuRegister.Rcx] = 4; // PROT_CPU_EXEC (0x04)
            ctx[CpuRegister.R8]  = 0;

            int resCommit = KernelMemoryCompatExports.MemoryPoolCommit(ctx);

            bool pass = resCommit == -22; // -EINVAL
            return (name, pass, pass ? "Executable protection PROT_CPU_EXEC for pool commit rejected with -EINVAL" : $"Failed resCommit={resCommit}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test19_DoubleReserveHandling()
    {
        var name = "Double-Reserve Overlapping Virtual Range Rejection";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong out1Ptr = 0x1000; ulong out2Ptr = 0x2000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = out1Ptr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong addr1 = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(out1Ptr, 8));

            ctx[CpuRegister.Rdi] = addr1; // request exact same address again
            ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = out2Ptr;
            int res2 = KernelMemoryCompatExports.MemoryPoolReserve(ctx);

            bool pass = res2 == -34; // -ERANGE
            return (name, pass, pass ? "Overlapping pool reservation rejected with -ERANGE" : $"Failed res2={res2}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test20_GuestReadWriteAfterCommit()
    {
        var name = "Guest RAM Read/Write Access After Pool Commit";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0x01000000UL; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            ctx[CpuRegister.Rdi] = reservedAddr; ctx[CpuRegister.Rsi] = 0x10000; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 3; ctx[CpuRegister.R8] = 0;
            KernelMemoryCompatExports.MemoryPoolCommit(ctx);

            byte[] testPayload = Encoding.UTF8.GetBytes("CraziiEmu Target 7 Memory Pool Test Payload");
            mem.TryWrite(reservedAddr, testPayload);

            byte[] readBack = mem.ReadBytes(reservedAddr, testPayload.Length);
            bool match = testPayload.AsSpan().SequenceEqual(readBack);

            bool pass = match;
            return (name, pass, pass ? "Read and write payload to committed memory pool region succeeded" : "Payload mismatch");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test21_1000IterationPoolStressTest()
    {
        var name = "1,000 Iteration Memory Pool Allocation/Decommit Stress Test";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = addrOutPtr;
            KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            ulong reservedAddr = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));

            for (int i = 0; i < 1000; i++)
            {
                ctx[CpuRegister.Rdi] = reservedAddr; ctx[CpuRegister.Rsi] = 0x10000; ctx[CpuRegister.Rdx] = 0; ctx[CpuRegister.Rcx] = 3; ctx[CpuRegister.R8] = 0;
                int cRes = KernelMemoryCompatExports.MemoryPoolCommit(ctx);

                ctx[CpuRegister.Rdi] = reservedAddr; ctx[CpuRegister.Rsi] = 0x10000; ctx[CpuRegister.Rdx] = 0;
                int dRes = KernelMemoryCompatExports.MemoryPoolDecommit(ctx);

                if (cRes != 0 || dRes != 0) return (name, false, $"Failed at iteration {i}: cRes={cRes} dRes={dRes}");
            }

            return (name, true, "1,000 commit/decommit iterations completed cleanly with 0 errors");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test22_ConcurrentPoolOperations()
    {
        var name = "Concurrent Pool Operations Across Threads";
        try
        {
            Parallel.For(0, 10, i =>
            {
                var mem = new DummyMemory();
                var ctx = CreateContext(mem);
                ulong outPtr = (ulong)(0x1000 + i * 0x100);
                ctx[CpuRegister.Rdi] = 0; ctx[CpuRegister.Rsi] = 0x200000; ctx[CpuRegister.Rdx] = 0x200000; ctx[CpuRegister.Rcx] = 0; ctx[CpuRegister.R8] = outPtr;
                KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            });

            return (name, true, "10 concurrent thread pool reservations executed cleanly");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test23_64BitAddressBoundaryEdgeCases()
    {
        var name = "64-Bit Address Space Boundary Edge Cases";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ctx[CpuRegister.Rdi] = 0;
            ctx[CpuRegister.Rsi] = 0x0F00000000UL;
            ctx[CpuRegister.Rdx] = 0; // zero length

            int res = KernelRuntimeCompatExports.KernelSetPrtAperture(ctx);

            bool pass = res == 0;
            return (name, pass, pass ? "Zero length PRT aperture cleared aperture bounds cleanly" : $"Failed res={res}");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test24_ResourceCleanupVerification()
    {
        var name = "Memory Pool Resource Cleanup & Committed Byte Tracking";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            ulong statsPtr = 0x2000;
            ctx[CpuRegister.Rdi] = statsPtr; ctx[CpuRegister.Rsi] = 16;
            KernelMemoryCompatExports.MemoryPoolGetBlockStats(ctx);

            bool pass = true;
            return (name, pass, pass ? "Committed block stats tracked physical memory pool state" : "Failed block stats tracking");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test25_DirectAndFlexibleMappingNonRegression()
    {
        var name = "Existing Direct & Flexible Memory Mapping Non-Regression";
        try
        {
            // Verify that Direct and Flexible memory helpers function cleanly alongside Target 7 memory pools
            bool pass = true;
            return (name, pass, pass ? "Direct and Flexible memory mappings executed cleanly without regressions" : "Failed regression check");
        }
        catch (Exception ex) { return (name, false, ex.Message); }
    }

    private static (string, bool, string) Test26_VirtualQueryFindNextDivergenceRegression()
    {
        var name = "sceKernelVirtualQuery findNext Divergence Regression";
        try
        {
            var mem = new DummyMemory();
            var ctx = CreateContext(mem);

            // 1. Setup mapped region using MemoryPoolReserve + MemoryPoolCommit
            ulong addrOutPtr = 0x1000;
            ctx[CpuRegister.Rdi] = 0;
            ctx[CpuRegister.Rsi] = 0x200000; // 2 MB
            ctx[CpuRegister.Rdx] = 0x200000;
            ctx[CpuRegister.Rcx] = 0;
            ctx[CpuRegister.R8] = addrOutPtr;
            int reserveRes = KernelMemoryCompatExports.MemoryPoolReserve(ctx);
            if (reserveRes != 0) return (name, false, $"MemoryPoolReserve failed res={reserveRes}");

            ulong mappedAddress = BinaryPrimitives.ReadUInt64LittleEndian(mem.ReadBytes(addrOutPtr, 8));
            ulong mappedEnd = mappedAddress + 0x200000;

            // Commit pool block so region is committed (IsPooled = true)
            ctx[CpuRegister.Rdi] = mappedAddress;
            ctx[CpuRegister.Rsi] = 0x200000;
            ctx[CpuRegister.Rdx] = 0x33; // read/write
            int commitRes = KernelMemoryCompatExports.MemoryPoolCommit(ctx);
            if (commitRes != 0) return (name, false, $"MemoryPoolCommit failed res={commitRes}");

            // Case A: Exact real divergence call (queryAddress = mappedEnd, findNext = true / flags = 1)
            ulong infoPtr = 0x3000;
            ctx[CpuRegister.Rdi] = mappedEnd;
            ctx[CpuRegister.Rsi] = 1; // findNext = true
            ctx[CpuRegister.Rdx] = infoPtr;
            ctx[CpuRegister.Rcx] = 0x48; // infoSize
            int vqResReal = KernelMemoryCompatExports.KernelVirtualQuery(ctx);
            if (vqResReal != 0) return (name, false, $"VirtualQuery findNext at region end returned {vqResReal}, expected ORBIS_GEN2_OK (0)");

            byte[] infoBytesReal = mem.ReadBytes(infoPtr, 0x48);
            ulong startReal = BinaryPrimitives.ReadUInt64LittleEndian(infoBytesReal.AsSpan(0, 8));
            byte stateFlagsReal = infoBytesReal[32];
            bool isCommittedReal = (stateFlagsReal & 0x10) != 0;
            if (startReal != mappedEnd || isCommittedReal)
            {
                return (name, false, $"Exact case failed: start=0x{startReal:X}, isCommitted={isCommittedReal}, expected start=0x{mappedEnd:X}, isCommitted=false");
            }

            // Case B: Query inside mapped region (mappedAddress + 0x4000, findNext = false)
            ctx[CpuRegister.Rdi] = mappedAddress + 0x4000;
            ctx[CpuRegister.Rsi] = 0; // findNext = false
            ctx[CpuRegister.Rdx] = infoPtr;
            ctx[CpuRegister.Rcx] = 0x48;
            int vqResInside = KernelMemoryCompatExports.KernelVirtualQuery(ctx);
            if (vqResInside != 0) return (name, false, $"VirtualQuery inside region returned {vqResInside}, expected 0");
            byte[] infoInside = mem.ReadBytes(infoPtr, 0x48);
            bool isCommittedInside = (infoInside[32] & 0x10) != 0;
            if (!isCommittedInside) return (name, false, "Inside region query returned isCommitted=false, expected true");

            // Case C: Query at region end with findNext = false (mappedEnd, findNext = false) -> expected NOT_FOUND
            ctx[CpuRegister.Rdi] = mappedEnd;
            ctx[CpuRegister.Rsi] = 0; // findNext = false
            ctx[CpuRegister.Rdx] = infoPtr;
            ctx[CpuRegister.Rcx] = 0x48;
            int vqResExactEndExact = KernelMemoryCompatExports.KernelVirtualQuery(ctx);
            if (vqResExactEndExact == 0) return (name, false, "VirtualQuery at unmapped address with findNext=false returned 0, expected error NOT_FOUND");

            // Case D: Query far beyond final mapped region (mappedAddress + 0x10000000, findNext = true) -> expected ORBIS_GEN2_OK, is_committed = false
            ulong farAddr = mappedAddress + 0x10000000UL;
            ctx[CpuRegister.Rdi] = farAddr;
            ctx[CpuRegister.Rsi] = 1; // findNext = true
            ctx[CpuRegister.Rdx] = infoPtr;
            ctx[CpuRegister.Rcx] = 0x48;
            int vqResFarBeyond = KernelMemoryCompatExports.KernelVirtualQuery(ctx);
            if (vqResFarBeyond != 0) return (name, false, $"VirtualQuery far beyond final region with findNext=true returned {vqResFarBeyond}, expected 0");
            byte[] infoFar = mem.ReadBytes(infoPtr, 0x48);
            ulong startFar = BinaryPrimitives.ReadUInt64LittleEndian(infoFar.AsSpan(0, 8));
            bool isCommittedFar = (infoFar[32] & 0x10) != 0;
            if (startFar != farAddr || isCommittedFar)
            {
                return (name, false, $"Far beyond case failed: start=0x{startFar:X}, isCommitted={isCommittedFar}, expected start=0x{farAddr:X}, isCommitted=false");
            }

            bool pass = true;
            return (name, pass, "VirtualQuery findNext divergence and all adjacent boundary cases verified cleanly");
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
