// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using CraziiEmu.HLE;
using CraziiEmu.Libs.Kernel;
using CraziiEmu.Libs.Network;
using CraziiEmu.Libs.VideoOut;
using Silk.NET.Vulkan;

namespace CraziiEmu.TestRunner;

public static class GameCompatibilityTests
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
        Console.WriteLine("[TEST] Running GameCompatibilityTests...");
        TestSonicSuperstarsR32SintStorageViewFormat();
        TestSocketTcpNoDelayOption();
        TestSocketExtendedOptions();
        Console.WriteLine("[TEST] GameCompatibilityTests PASSED successfully.");
    }

    private static void TestSonicSuperstarsR32SintStorageViewFormat()
    {
        // PPSA06888 (Sonic Superstars) raw R32 SINT storage textures require a bit-compatible
        // unsigned view (vk::Format::eR32Uint) for compute passes (KytyPS5 ShaderRecompilerComputeTests line 10287-10299).
        var storageFormat = VulkanVideoPresenter.GetStorageImageFormat(Format.R32Sint);
        if (storageFormat != Format.R32Uint)
        {
            throw new InvalidOperationException(
                $"Expected Format.R32Sint to map to Format.R32Uint for storage images, but got {storageFormat}.");
        }

        if (!VulkanVideoPresenter.IsCompatibleViewFormat(Format.R32Sint, Format.R32Uint))
        {
            throw new InvalidOperationException("Expected Format.R32Sint and Format.R32Uint to be compatible view formats.");
        }

        Console.WriteLine("  [PASS] Sonic Superstars PPSA06888 R32 SINT storage view reinterpretation verified");
    }

    private static void TestSocketTcpNoDelayOption()
    {
        var mem = new DummyMemory();
        var cpu = new CpuContext(mem, Generation.Gen5);

        // 1. Initialize Net
        NetExports.NetInit(cpu);

        // 2. Create Socket
        mem.TryWrite(0x1000, "game_compat_test\0"u8);
        cpu[CpuRegister.Rdi] = 0x1000;
        cpu[CpuRegister.Rsi] = 2; // AF_INET
        cpu[CpuRegister.Rdx] = 1; // SOCK_STREAM
        cpu[CpuRegister.Rcx] = 6; // IPPROTO_TCP
        var res = NetExports.NetSocket(cpu);
        if (res != 0) throw new InvalidOperationException($"NetSocket failed: {res}");
        int socketFd = (int)cpu[CpuRegister.Rax];

        // 3. Set TCP_NODELAY via libSceNet: sceNetSetsockopt(socketFd, IPPROTO_TCP=6, TCP_NODELAY=1, &val=1, 4)
        const ulong optvalAddr = 0x2000;
        const ulong optlenAddr = 0x2010;

        Span<byte> valOne = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valOne, 1);
        mem.TryWrite(optvalAddr, valOne);

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 6; // IPPROTO_TCP
        cpu[CpuRegister.Rdx] = 1; // TCP_NODELAY
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(cpu);
        if (res != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"sceNetSetsockopt TCP_NODELAY failed with {cpu[CpuRegister.Rax]}");
        }

        // 4. Query TCP_NODELAY via libSceNet: sceNetGetsockopt
        Span<byte> optlenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(optlenBuf, 4);
        mem.TryWrite(optlenAddr, optlenBuf);
        mem.TryWrite(optvalAddr, stackalloc byte[4]); // zero out value

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 6; // IPPROTO_TCP
        cpu[CpuRegister.Rdx] = 1; // TCP_NODELAY
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(cpu);
        if (res != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"sceNetGetsockopt TCP_NODELAY failed with {cpu[CpuRegister.Rax]}");
        }

        Span<byte> readVal = stackalloc byte[4];
        mem.TryRead(optvalAddr, readVal);
        var readValInt = BinaryPrimitives.ReadInt32LittleEndian(readVal);
        if (readValInt != 1)
        {
            throw new InvalidOperationException($"Expected TCP_NODELAY value 1, got {readValInt}");
        }

        // 5. Cross-API test through libkernel: setsockopt / getsockopt
        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 6; // IPPROTO_TCP
        cpu[CpuRegister.Rdx] = 1; // TCP_NODELAY
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = 4;
        var ksetRes = KernelSocketCompatExports.Setsockopt(cpu);
        if (ksetRes != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"libkernel setsockopt TCP_NODELAY failed with {cpu[CpuRegister.Rax]}");
        }

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 6; // IPPROTO_TCP
        cpu[CpuRegister.Rdx] = 1; // TCP_NODELAY
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = optlenAddr;
        var kgetRes = KernelSocketCompatExports.Getsockopt(cpu);
        if (kgetRes != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"libkernel getsockopt TCP_NODELAY failed with {cpu[CpuRegister.Rax]}");
        }

        mem.TryRead(optvalAddr, readVal);
        var kreadValInt = BinaryPrimitives.ReadInt32LittleEndian(readVal);
        if (kreadValInt != 1)
        {
            throw new InvalidOperationException($"Expected libkernel TCP_NODELAY value 1, got {kreadValInt}");
        }

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        NetExports.NetSocketClose(cpu);
        Console.WriteLine("  [PASS] TCP_NODELAY cross-API socket configuration verified cleanly");
    }

    private static void TestSocketExtendedOptions()
    {
        var mem = new DummyMemory();
        var cpu = new CpuContext(mem, Generation.Gen5);

        NetExports.NetInit(cpu);

        mem.TryWrite(0x1000, "game_compat_ext\0"u8);
        cpu[CpuRegister.Rdi] = 0x1000;
        cpu[CpuRegister.Rsi] = 2; // AF_INET
        cpu[CpuRegister.Rdx] = 1; // SOCK_STREAM
        cpu[CpuRegister.Rcx] = 6; // IPPROTO_TCP
        var res = NetExports.NetSocket(cpu);
        if (res != 0) throw new InvalidOperationException($"NetSocket failed: {res}");
        int socketFd = (int)cpu[CpuRegister.Rax];

        const ulong optvalAddr = 0x3000;
        const ulong optlenAddr = 0x3010;

        // Test SO_NOSIGPIPE (0x0800) - must succeed with 0
        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        cpu[CpuRegister.Rdx] = 0x0800; // SO_NOSIGPIPE
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = 4;
        res = NetExports.NetSetsockopt(cpu);
        if (res != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"SO_NOSIGPIPE failed with {cpu[CpuRegister.Rax]}");
        }

        // Test SO_TYPE (0x1008) via getsockopt -> expected 1 (SOCK_STREAM)
        Span<byte> optlenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(optlenBuf, 4);
        mem.TryWrite(optlenAddr, optlenBuf);

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        cpu[CpuRegister.Rsi] = 0xFFFF; // SOL_SOCKET
        cpu[CpuRegister.Rdx] = 0x1008; // SO_TYPE
        cpu[CpuRegister.Rcx] = optvalAddr;
        cpu[CpuRegister.R8] = optlenAddr;
        res = NetExports.NetGetsockopt(cpu);
        if (res != 0 || cpu[CpuRegister.Rax] != 0)
        {
            throw new InvalidOperationException($"SO_TYPE failed with {cpu[CpuRegister.Rax]}");
        }

        Span<byte> readVal = stackalloc byte[4];
        mem.TryRead(optvalAddr, readVal);
        var sockType = BinaryPrimitives.ReadInt32LittleEndian(readVal);
        if (sockType != 1)
        {
            throw new InvalidOperationException($"Expected SO_TYPE = 1 (SOCK_STREAM), got {sockType}");
        }

        cpu[CpuRegister.Rdi] = (ulong)socketFd;
        NetExports.NetSocketClose(cpu);
        Console.WriteLine("  [PASS] SO_NOSIGPIPE and SO_TYPE options verified cleanly");
    }
}
