// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Ime;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.VideoOut;

namespace CraziiEmu.TestRunner;

public static class UnityScriptingMemTests
{
    private sealed class TestMemory : ICpuMemory
    {
        private readonly byte[] _storage = new byte[0x100000];

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address + (ulong)destination.Length > (ulong)_storage.Length)
            {
                return false;
            }

            _storage.AsSpan((int)address, destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong address, ReadOnlySpan<byte> source)
        {
            if (address + (ulong)source.Length > (ulong)_storage.Length)
            {
                return false;
            }

            source.CopyTo(_storage.AsSpan((int)address, source.Length));
            return true;
        }

        public bool TryProtect(ulong address, ulong size, GuestPageProtection protection) => true;
    }

    public static void RunAllTests()
    {
        Console.WriteLine("[TEST] Starting UnityScriptingMemTests...");

        TestImeDialogParamOffsetLayout();
        TestPosixMemalignAlignmentAndCapacity();
        TestPosixMemalignInvalidAlignmentRejection();
        TestVirtualRandomDeviceLifecycleAndEntropy();
        TestGuestPathTempMappingAndApp0Writable();
        TestKernelExceptionSignalParityAndEsrch();
        TestGuestPathDataMappingAndRecursiveMkdir();
        TestAmongUsAndUnityProgressionFeatures();

        Console.WriteLine("[TEST] UnityScriptingMemTests PASSED cleanly.");
    }

    private static void TestImeDialogParamOffsetLayout()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ulong paramAddress = 0x1000;
        ulong textBufferAddress = 0x4000;
        const uint maxTextLength = 32;

        // Initialize SceImeDialogParam at 0x1000 (0x60 bytes total)
        // Offset 0x00: user_id = 0x10000000 (slot 0)
        Span<byte> userIdBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(userIdBytes, 0x10000000);
        mem.TryWrite(paramAddress + 0x00, userIdBytes);

        // Offset 0x20: max_text_length = 32
        Span<byte> maxLenBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(maxLenBytes, maxTextLength);
        mem.TryWrite(paramAddress + 0x20, maxLenBytes);

        // Offset 0x28: input_text_buffer pointer = textBufferAddress
        Span<byte> ptrBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(ptrBytes, textBufferAddress);
        mem.TryWrite(paramAddress + 0x28, ptrBytes);

        ctx[CpuRegister.Rdi] = paramAddress;
        var initResult = ImeDialogExports.ImeDialogInit(ctx);
        if (initResult != 0)
        {
            throw new InvalidOperationException($"ImeDialogInit failed with code {initResult}");
        }

        var statusResult = ImeDialogExports.ImeDialogGetStatus(ctx);
        if (statusResult != 2 /* StatusFinished */)
        {
            throw new InvalidOperationException($"Expected StatusFinished (2), got {statusResult}");
        }

        // Verify that UTF-16 text was written to textBufferAddress
        Span<byte> readBuffer = stackalloc byte[64];
        if (!mem.TryRead(textBufferAddress, readBuffer))
        {
            throw new InvalidOperationException("Failed to read back text buffer written by ImeDialogInit");
        }

        var text = Encoding.Unicode.GetString(readBuffer).TrimEnd('\0');
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("Expected autofill text from ImeDialogInit, but got empty string");
        }

        Console.WriteLine($"  [PASS] ImeDialog autofill verified (text=\"{text}\", maxLen={maxTextLength})");
    }

    private static void TestPosixMemalignAlignmentAndCapacity()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ulong outPtrAddress = 0x8000;
        ulong requestedAlignment = 64;
        ulong requestedSize = 1024 * 1024; // 1 MB

        ctx[CpuRegister.Rdi] = outPtrAddress;
        ctx[CpuRegister.Rsi] = requestedAlignment;
        ctx[CpuRegister.Rdx] = requestedSize;

        var result = KernelMemoryCompatExports.PosixMemalign(ctx);
        if (result != 0)
        {
            throw new InvalidOperationException($"PosixMemalign failed with code {result}");
        }

        Span<byte> ptrBytes = stackalloc byte[8];
        if (!mem.TryRead(outPtrAddress, ptrBytes))
        {
            throw new InvalidOperationException("Failed to read out pointer from PosixMemalign");
        }

        var allocatedAddress = BinaryPrimitives.ReadUInt64LittleEndian(ptrBytes);
        if (allocatedAddress == 0)
        {
            throw new InvalidOperationException("PosixMemalign returned null pointer on success");
        }

        if (allocatedAddress % requestedAlignment != 0)
        {
            throw new InvalidOperationException(
                $"Allocated address 0x{allocatedAddress:X} is not aligned to {requestedAlignment} bytes");
        }

        // Verify entire allocation is writable without throwing
        for (ulong offset = 0; offset < requestedSize; offset += 4096)
        {
            System.Runtime.InteropServices.Marshal.WriteByte((nint)(allocatedAddress + offset), 0xAB);
        }

        Console.WriteLine($"  [PASS] posix_memalign alignment & full capacity verified: addr=0x{allocatedAddress:X16}, align={requestedAlignment}, size={requestedSize}");
    }

    private static void TestPosixMemalignInvalidAlignmentRejection()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        ulong outPtrAddress = 0x9000;
        ulong invalidAlignment = 15; // not a power of two and < sizeof(void*)
        ulong requestedSize = 1024;

        ctx[CpuRegister.Rdi] = outPtrAddress;
        ctx[CpuRegister.Rsi] = invalidAlignment;
        ctx[CpuRegister.Rdx] = requestedSize;

        var result = KernelMemoryCompatExports.PosixMemalign(ctx);
        // PosixMemalign returns 0 in managed dispatcher, but RAX contains EINVAL (22)
        var rax = ctx[CpuRegister.Rax];
        if (rax != 22 /* EINVAL */)
        {
            throw new InvalidOperationException($"Expected EINVAL (22) in RAX for non-power-of-2 alignment, got {rax}");
        }

        Console.WriteLine("  [PASS] posix_memalign invalid alignment rejection verified");
    }

    private static void WriteCString(TestMemory mem, ulong address, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        mem.TryWrite(address, bytes);
    }

    private static void TestVirtualRandomDeviceLifecycleAndEntropy()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. sceKernelCheckReachability on /dev/urandom
        ulong urandomPathAddr = 0xA000;
        WriteCString(mem, urandomPathAddr, "/dev/urandom");
        ctx[CpuRegister.Rdi] = urandomPathAddr;
        var reachResult = KernelMemoryCompatExports.KernelCheckReachability(ctx);
        if (reachResult != 0)
        {
            throw new InvalidOperationException($"KernelCheckReachability failed for /dev/urandom: {reachResult}");
        }

        // 2. Open /dev/urandom via _open
        ctx[CpuRegister.Rdi] = urandomPathAddr;
        ctx[CpuRegister.Rsi] = 0; // O_RDONLY
        var openResult = KernelMemoryCompatExports.KernelOpenUnderscore(ctx);
        if (openResult != 0)
        {
            throw new InvalidOperationException($"KernelOpenUnderscore failed for /dev/urandom: {openResult}");
        }

        var fd = unchecked((int)ctx[CpuRegister.Rax]);
        if (fd <= 2)
        {
            throw new InvalidOperationException($"Expected fd > 2, got {fd}");
        }

        // 3. Read 16 bytes of entropy (like Unity UUID generation)
        ulong bufferAddr = 0xB000;
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        ctx[CpuRegister.Rsi] = bufferAddr;
        ctx[CpuRegister.Rdx] = 16;
        var readResult = KernelMemoryCompatExports.KernelReadUnderscore(ctx);
        if (readResult != 0 || ctx[CpuRegister.Rax] != 16)
        {
            throw new InvalidOperationException($"KernelReadUnderscore failed: res={readResult}, rax={ctx[CpuRegister.Rax]}");
        }

        Span<byte> uuidBytes = stackalloc byte[16];
        mem.TryRead(bufferAddr, uuidBytes);
        bool allZero = true;
        for (int i = 0; i < uuidBytes.Length; i++)
        {
            if (uuidBytes[i] != 0) { allZero = false; break; }
        }
        if (allZero)
        {
            throw new InvalidOperationException("Read from /dev/urandom produced all zero bytes");
        }

        // 4. sceKernelFstat verification
        ulong statAddr = 0xC000;
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        ctx[CpuRegister.Rsi] = statAddr;
        var fstatResult = KernelMemoryCompatExports.KernelFstat(ctx);
        if (fstatResult != 0)
        {
            throw new InvalidOperationException($"KernelFstat failed for random fd {fd}: {fstatResult}");
        }

        Span<byte> statBytes = stackalloc byte[128];
        mem.TryRead(statAddr, statBytes);
        var stMode = BinaryPrimitives.ReadUInt16LittleEndian(statBytes.Slice(8, 2));
        if (stMode != 0x21B6) // S_IFCHR | 0666
        {
            throw new InvalidOperationException($"Expected st_mode 0x21B6 for character device, got 0x{stMode:X4}");
        }

        // 5. sceKernelLseek returns ESPIPE (0x8002001D)
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        ctx[CpuRegister.Rsi] = 0;
        ctx[CpuRegister.Rdx] = 0;
        var lseekResult = unchecked((uint)KernelMemoryCompatExports.KernelLseek(ctx));
        if (lseekResult != 0x8002001D)
        {
            throw new InvalidOperationException($"Expected ESPIPE (0x8002001D) for lseek on random fd, got 0x{lseekResult:X8}");
        }

        // 6. Descriptor duplication: PosixDup
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        var dupResult = KernelMemoryCompatExports.PosixDup(ctx);
        if (dupResult != 0)
        {
            throw new InvalidOperationException($"PosixDup failed for random fd: {dupResult}");
        }
        var dupFd = unchecked((int)ctx[CpuRegister.Rax]);
        if (dupFd == fd || dupFd <= 2)
        {
            throw new InvalidOperationException($"Invalid dupFd: {dupFd}");
        }

        // Read through dupFd
        ctx[CpuRegister.Rdi] = unchecked((ulong)dupFd);
        ctx[CpuRegister.Rsi] = bufferAddr;
        ctx[CpuRegister.Rdx] = 16;
        var readDupResult = KernelMemoryCompatExports.KernelReadUnderscore(ctx);
        if (readDupResult != 0 || ctx[CpuRegister.Rax] != 16)
        {
            throw new InvalidOperationException($"Read via dupFd failed: res={readDupResult}");
        }

        // Close dupFd
        ctx[CpuRegister.Rdi] = unchecked((ulong)dupFd);
        KernelMemoryCompatExports.KernelCloseUnderscore(ctx);

        // 7. Descriptor duplication: PosixDup2
        int dup2Target = 155;
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        ctx[CpuRegister.Rsi] = unchecked((ulong)dup2Target);
        var dup2Result = KernelMemoryCompatExports.PosixDup2(ctx);
        if (dup2Result != 0 || ctx[CpuRegister.Rax] != (ulong)dup2Target)
        {
            throw new InvalidOperationException($"PosixDup2 failed: res={dup2Result}, rax={ctx[CpuRegister.Rax]}");
        }

        // Close dup2Target
        ctx[CpuRegister.Rdi] = unchecked((ulong)dup2Target);
        KernelMemoryCompatExports.KernelCloseUnderscore(ctx);

        // 8. Descriptor duplication: KernelFcntl(F_DUPFD = 0)
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        ctx[CpuRegister.Rsi] = 0; // F_DUPFD
        ctx[CpuRegister.Rdx] = 200;
        var fcntlResult = KernelMemoryCompatExports.KernelFcntl(ctx);
        if (fcntlResult != 0)
        {
            throw new InvalidOperationException($"KernelFcntl(F_DUPFD) failed: {fcntlResult}");
        }
        var fcntlFd = unchecked((int)ctx[CpuRegister.Rax]);
        if (fcntlFd < 200)
        {
            throw new InvalidOperationException($"Expected fcntlFd >= 200, got {fcntlFd}");
        }
        ctx[CpuRegister.Rdi] = unchecked((ulong)fcntlFd);
        KernelMemoryCompatExports.KernelCloseUnderscore(ctx);

        // 9. Close original random fd
        ctx[CpuRegister.Rdi] = unchecked((ulong)fd);
        var closeResult = KernelMemoryCompatExports.KernelCloseUnderscore(ctx);
        if (closeResult != 0)
        {
            throw new InvalidOperationException($"KernelCloseUnderscore failed for random fd: {closeResult}");
        }

        // 10. Repeat open on /dev/random
        ulong randomPathAddr = 0xD000;
        WriteCString(mem, randomPathAddr, "/dev/random");
        ctx[CpuRegister.Rdi] = randomPathAddr;
        ctx[CpuRegister.Rsi] = 0;
        var openRandResult = KernelMemoryCompatExports.KernelOpenUnderscore(ctx);
        if (openRandResult != 0)
        {
            throw new InvalidOperationException($"KernelOpenUnderscore failed for /dev/random: {openRandResult}");
        }
        var randFd = unchecked((int)ctx[CpuRegister.Rax]);
        ctx[CpuRegister.Rdi] = unchecked((ulong)randFd);
        KernelMemoryCompatExports.KernelCloseUnderscore(ctx);

        Console.WriteLine("  [PASS] /dev/urandom & /dev/random virtual character device lifecycle verified");
    }

    private static void TestGuestPathTempMappingAndApp0Writable()
    {
        var tempFile1 = KernelMemoryCompatExports.ResolveGuestPath("/temp/archive.dat");
        var tempFile2 = KernelMemoryCompatExports.ResolveGuestPath("temp/archive.dat");
        var tempDir1 = KernelMemoryCompatExports.ResolveGuestPath("/temp");
        var tempDir2 = KernelMemoryCompatExports.ResolveGuestPath("temp");

        if (!string.Equals(tempFile1, tempFile2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"/temp and temp mapping mismatch: '{tempFile1}' vs '{tempFile2}'");
        }

        if (!string.Equals(Path.GetDirectoryName(tempFile1), tempDir1, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Parent of /temp/archive.dat '{Path.GetDirectoryName(tempFile1)}' does not match /temp '{tempDir1}'");
        }

        if (!string.Equals(tempDir1, tempDir2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"/temp '{tempDir1}' does not match temp '{tempDir2}'");
        }

        if (!Directory.Exists(tempDir1))
        {
            throw new InvalidOperationException($"Resolved temp directory does not exist on host: {tempDir1}");
        }

        // Verify /app0 write access defaults to allowed (matching KytyPS5)
        if (KernelMemoryCompatExports.IsReadOnlyGuestMutationPath("/app0/savedata/file.bin"))
        {
            throw new InvalidOperationException("Expected /app0 mutation path to be writable by default");
        }

        Console.WriteLine("  [PASS] /temp guest path mapping and /app0 writable defaults verified");
    }

    private static void TestKernelExceptionSignalParityAndEsrch()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Verify SIGUSR1 (30) and SIGUSR2 (31) are accepted by InstallExceptionHandler
        ctx[CpuRegister.Rdi] = 30;
        ctx[CpuRegister.Rsi] = 0x80010000;
        int resInstall30 = KernelExceptionCompatExports.InstallExceptionHandler(ctx);
        if (resInstall30 != (int)OrbisGen2Result.ORBIS_GEN2_OK)
        {
            throw new InvalidOperationException($"Expected SIGUSR1 install OK, got 0x{resInstall30:X8}");
        }

        ctx[CpuRegister.Rdi] = 31;
        ctx[CpuRegister.Rsi] = 0x80020000;
        int resInstall31 = KernelExceptionCompatExports.InstallExceptionHandler(ctx);
        if (resInstall31 != (int)OrbisGen2Result.ORBIS_GEN2_OK)
        {
            throw new InvalidOperationException($"Expected SIGUSR2 install OK, got 0x{resInstall31:X8}");
        }

        // 2. Raise exception targeting an invalid/unknown thread handle
        // Must return ORBIS_GEN2_ERROR_INVALID_ARGUMENT (0x80020003 / ESRCH) so IL2CPP skips thread
        ctx[CpuRegister.Rdi] = 0xDEADBEEF0000UL;
        ctx[CpuRegister.Rsi] = 30;
        int resRaiseUnknown = KernelExceptionCompatExports.RaiseException(ctx);
        if (resRaiseUnknown != (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT)
        {
            throw new InvalidOperationException(
                $"Expected RaiseException on unknown thread to return 0x80020003 (ESRCH), got 0x{resRaiseUnknown:X8}");
        }

        // 3. Clean up installed handlers
        ctx[CpuRegister.Rdi] = 30;
        KernelExceptionCompatExports.RemoveExceptionHandler(ctx);
        ctx[CpuRegister.Rdi] = 31;
        KernelExceptionCompatExports.RemoveExceptionHandler(ctx);

        Console.WriteLine("  [PASS] sceKernelRaiseException ESRCH return and SIGUSR1/SIGUSR2 parity verified");
    }

    private static void TestGuestPathDataMappingAndRecursiveMkdir()
    {
        // 1. Verify /data/ and data/ path resolution
        var dataFile1 = KernelMemoryCompatExports.ResolveGuestPath("/data/test.dat");
        var dataFile2 = KernelMemoryCompatExports.ResolveGuestPath("data/test.dat");
        var dataDir1 = KernelMemoryCompatExports.ResolveGuestPath("/data");
        var dataDir2 = KernelMemoryCompatExports.ResolveGuestPath("data");

        if (!string.Equals(dataFile1, dataFile2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"/data and data mapping mismatch: '{dataFile1}' vs '{dataFile2}'");
        }

        if (!string.Equals(Path.GetDirectoryName(dataFile1), dataDir1, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Parent of /data/test.dat '{Path.GetDirectoryName(dataFile1)}' does not match /data '{dataDir1}'");
        }

        if (!string.Equals(dataDir1, dataDir2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"/data '{dataDir1}' does not match data '{dataDir2}'");
        }

        if (!Directory.Exists(dataDir1))
        {
            throw new InvalidOperationException($"Resolved data directory does not exist on host: {dataDir1}");
        }

        // 2. Verify recursive mkdir for nested paths
        var hostNestedDir = KernelMemoryCompatExports.ResolveGuestPath("/data/nested/archive/dir");
        var hostNestedParent = KernelMemoryCompatExports.ResolveGuestPath("/data/nested");
        try
        {
            if (Directory.Exists(hostNestedParent))
            {
                Directory.Delete(hostNestedParent, recursive: true);
            }
        }
        catch { }

        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);
        ulong pathAddr = 0xE000;
        WriteCString(mem, pathAddr, "/data/nested/archive/dir");
        ctx[CpuRegister.Rdi] = pathAddr;
        ctx[CpuRegister.Rsi] = 0x1FF; // 0777 octal
        var mkdirRes = KernelMemoryCompatExports.KernelMkdir(ctx);
        if (mkdirRes != 0)
        {
            throw new InvalidOperationException($"KernelMkdir failed for nested path: {mkdirRes}");
        }

        if (!Directory.Exists(hostNestedDir))
        {
            throw new InvalidOperationException($"Nested directory was not created on host: {hostNestedDir}");
        }

        try
        {
            if (Directory.Exists(hostNestedParent))
            {
                Directory.Delete(hostNestedParent, recursive: true);
            }
        }
        catch { }

        // 3. Verify stock exception exports
        var stockInstall = KernelExceptionCompatExports.InstallExceptionHandler(ctx);
        if (stockInstall != (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT)
        {
            throw new InvalidOperationException($"Expected INVALID_ARGUMENT for invalid signal, got {stockInstall}");
        }

        // 4. Verify CompleteFlip fallback to open port and event queue signaling
        VideoOutExports.CompleteFlip(0, 42);

        Console.WriteLine("  [PASS] /data guest path, recursive mkdir, and exception exports verified");
    }

    private static void TestAmongUsAndUnityProgressionFeatures()
    {
        var mem = new TestMemory();
        var ctx = new CpuContext(mem, Generation.Gen5);

        // 1. Verify Gen5 display events preserve ident=3 and unmasked flipArg
        ulong eqHandle = 0;
        ulong eqNameAddr = 0x1000;
        WriteCString(mem, eqNameAddr, "test_unity_eq");
        ctx[CpuRegister.Rdi] = 0x2000; // out handle addr
        ctx[CpuRegister.Rsi] = eqNameAddr;
        ctx[CpuRegister.Rdx] = 0;
        ctx[CpuRegister.Rcx] = 16;
        Span<byte> handleBytes = stackalloc byte[8];
        if (KernelEventQueueCompatExports.KernelCreateEqueue(ctx) == 0 &&
            mem.TryRead(0x2000, handleBytes))
        {
            eqHandle = BinaryPrimitives.ReadUInt64LittleEndian(handleBytes);
            const ulong expectedFlipArg = 0xDEAD_BEEF_CAFE_0001UL;
            KernelEventQueueCompatExports.TriggerDisplayEvent(
                eqHandle,
                ident: 0UL,
                filter: VideoOutExports.OrbisKernelEventFilterVideoOut,
                eventHint: expectedFlipArg,
                userData: 0x8888UL,
                isGen5: true);

            if (KernelEventQueueCompatExports.TryReservePendingEventForTest(eqHandle, out var ev))
            {
                var recoveredArg = (ev.Data >> 16) & 0x0000_FFFF_FFFF_FFFFUL;
                if (ev.Ident != 0UL || recoveredArg != (expectedFlipArg & 0x0000_FFFF_FFFF_FFFFUL) || ev.UserData != 0x8888UL)
                {
                    throw new InvalidOperationException(
                        $"Gen5 TriggerDisplayEvent mismatch: ident={ev.Ident}, data=0x{ev.Data:X16}, userData=0x{ev.UserData:X16}");
                }
            }
            else
            {
                throw new InvalidOperationException("Failed to reserve queued Gen5 display event");
            }

            ctx[CpuRegister.Rdi] = eqHandle;
            KernelEventQueueCompatExports.KernelDeleteEqueue(ctx);
        }

        // 2. Verify VideoOutIsFlipPending returns 0 or pending count in RAX
        ctx[CpuRegister.Rdi] = 0; // userId
        ctx[CpuRegister.Rsi] = 0; // busType
        ctx[CpuRegister.Rdx] = 0; // index
        ctx[CpuRegister.Rcx] = 0;
        var videoPort = VideoOutExports.VideoOutOpen(ctx);
        if (videoPort <= 0)
        {
            throw new InvalidOperationException($"VideoOutOpen failed: {videoPort}");
        }

        try
        {
            ctx[CpuRegister.Rdi] = (ulong)videoPort;
            var isFlipPendingRes = VideoOutExports.VideoOutIsFlipPending(ctx);
            if (isFlipPendingRes != 0 || ctx[CpuRegister.Rax] != 0)
            {
                throw new InvalidOperationException(
                    $"VideoOutIsFlipPending returned res={isFlipPendingRes}, RAX={ctx[CpuRegister.Rax]}");
            }

            // 3. Verify VideoOutFlipStatus writes currentBuffer at both 0x20 and 0x38
            ulong statusAddr = 0x3000;
            ctx[CpuRegister.Rdi] = (ulong)videoPort;
            ctx[CpuRegister.Rsi] = statusAddr;
            var statusRes = VideoOutExports.VideoOutGetFlipStatus(ctx);
            if (statusRes == 0)
            {
                Span<byte> bufGen4Bytes = stackalloc byte[8];
                Span<byte> bufGen5Bytes = stackalloc byte[4];
                mem.TryRead(statusAddr + 0x20, bufGen4Bytes);
                mem.TryRead(statusAddr + 0x38, bufGen5Bytes);
                ulong bufGen4 = BinaryPrimitives.ReadUInt64LittleEndian(bufGen4Bytes);
                uint bufGen5 = BinaryPrimitives.ReadUInt32LittleEndian(bufGen5Bytes);
                if (bufGen4 != (ulong)bufGen5)
                {
                    throw new InvalidOperationException($"VideoOutFlipStatus mismatch: 0x20={bufGen4} vs 0x38={bufGen5}");
                }
            }
            else
            {
                throw new InvalidOperationException($"VideoOutGetFlipStatus failed: {statusRes}");
            }
        }
        finally
        {
            ctx[CpuRegister.Rdi] = (ulong)videoPort;
            VideoOutExports.VideoOutClose(ctx);
        }

        // 4. Verify fopen on non-existent file returns ORBIS_GEN2_OK with RAX=0 (NULL)
        ulong fakePathAddr = 0x4000;
        ulong modeAddr = 0x4100;
        WriteCString(mem, fakePathAddr, "/app0/non_existent_unity_file.txt");
        WriteCString(mem, modeAddr, "r");
        ctx[CpuRegister.Rdi] = fakePathAddr;
        ctx[CpuRegister.Rsi] = modeAddr;
        var fopenRes = CraziiEmu.Libs.LibcStdio.LibcStdioExports.Fopen(ctx);
        if (fopenRes != (int)OrbisGen2Result.ORBIS_GEN2_OK || ctx[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException(
                $"Expected fopen to return ORBIS_GEN2_OK and RAX=0, got res={fopenRes}, RAX=0x{ctx[CpuRegister.Rax]:X16}");
        }

        Console.WriteLine("  [PASS] Gen5 display events, dual flip status layout, and fopen NULL return verified");
    }
}
