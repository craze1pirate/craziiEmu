// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using CraziiEmu.Logging;
using System.Collections.Generic;
using System.Text;
using CraziiEmu.Core.Memory;
using CraziiEmu.HLE;

namespace CraziiEmu.Core.HLE;

/// <summary>
/// Routes high-level emulated system calls based on a specified ABI.
/// </summary>
public class SyscallRouter
{
    private readonly VirtualMemoryManager _memory;
    private readonly Dictionary<ulong, Action<CpuContext>> _linuxHandlers;
    private readonly Dictionary<ulong, Action<CpuContext>> _freeBsdHandlers;

    /// <summary>
    /// Gets or sets the active ABI for system call routing.
    /// </summary>
    public SyscallAbi ActiveAbi { get; set; } = SyscallAbi.Linux;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyscallRouter"/> class.
    /// </summary>
    /// <param name="memory">The virtual memory manager used for reading guest memory.</param>
    public SyscallRouter(VirtualMemoryManager memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _linuxHandlers = new Dictionary<ulong, Action<CpuContext>>();
        _freeBsdHandlers = new Dictionary<ulong, Action<CpuContext>>();

        RegisterLinuxHandlers();
        RegisterFreeBsdHandlers();
    }

    private void SetSuccess(CpuContext ctx, ulong result)
    {
        ctx.Rax = result;
        if (ActiveAbi == SyscallAbi.FreeBsd)
        {
            ctx.CarryFlag = false;
        }
    }

    private void SetError(CpuContext ctx, ulong errorCode)
    {
        if (ActiveAbi == SyscallAbi.FreeBsd)
        {
            ctx.Rax = errorCode;
            ctx.CarryFlag = true;
        }
        else
        {
            ctx.Rax = unchecked((ulong)-(long)errorCode);
        }
    }

    private void RegisterLinuxHandlers()
    {
        _linuxHandlers[1] = SysWrite;
        _linuxHandlers[39] = SysGetpid;
        _linuxHandlers[60] = SysExit;
        _linuxHandlers[0x1337] = SysDlsymStub;
        _linuxHandlers[591] = SysDlsymStub; // PS4/PS5 sys_dynlib_dlsym
    }

    private void RegisterFreeBsdHandlers()
    {
        _freeBsdHandlers[4] = SysWrite;
        _freeBsdHandlers[1] = SysExit;
        _freeBsdHandlers[20] = SysGetpid;
        _freeBsdHandlers[0x1337] = SysDlsymStub;
        _freeBsdHandlers[591] = SysDlsymStub; // PS4/PS5 sys_dynlib_dlsym
    }

    /// <summary>
    /// Dispatches a system call based on the current context and active ABI.
    /// </summary>
    /// <param name="ctx">The CPU context.</param>
    public void Dispatch(CpuContext ctx)
    {
        var handlers = ActiveAbi == SyscallAbi.Linux ? _linuxHandlers : _freeBsdHandlers;

        if (handlers.TryGetValue(ctx.Rax, out var handler))
        {
            handler(ctx);
        }
        else
        {
            CraziiEmuLog.For("HLE").Info($"[Syscall] Unhandled syscall RAX={ctx.Rax} (0x{ctx.Rax:X}) RDI={ctx.Rdi} (0x{ctx.Rdi:X}) RSI={ctx.Rsi} (0x{ctx.Rsi:X})\n");
            // Unregistered syscall: gracefully return ENOSYS (78 for FreeBSD, 38 for Linux)
            ulong enosysCode = ActiveAbi == SyscallAbi.FreeBsd ? 78UL : 38UL;
            SetError(ctx, enosysCode);
        }
    }

    private void SysWrite(CpuContext ctx)
    {
        // Rdi = fd, Rsi = buffer, Rdx = count
        ulong fd = ctx.Rdi;
        ulong bufferAddress = ctx.Rsi;
        int count = (int)ctx.Rdx;

        if (fd == 1 || fd == 2) // stdout or stderr
        {
            try
            {
                var span = _memory.GetSpan(bufferAddress, count);
                var text = Encoding.UTF8.GetString(span);
                CraziiEmuLog.For("HLE").Info(text);
                SetSuccess(ctx, (ulong)count); // Success: bytes written
            }
            catch (ArgumentOutOfRangeException)
            {
                SetError(ctx, 14); // EFAULT
            }
        }
        else
        {
            SetError(ctx, 9); // EBADF
        }
    }

    private void SysExit(CpuContext ctx)
    {
        // Rdi = error_code (or sometimes a leftover pointer if the payload called syscall(1) without an exit code)
        string leftoverString = "";
        if (ctx.Rdi > 0x10000000)
        {
            try
            {
                var span = _memory.GetSpan(ctx.Rdi, 256);
                int len = span.IndexOf((byte)0);
                if (len > 0) leftoverString = " | Leftover string: '" + Encoding.UTF8.GetString(span.Slice(0, len)).Replace("\n", "\\n") + "'";
            }
            catch { }
        }
        
        CraziiEmuLog.For("HLE").Info($"[Syscall] sys_exit called with code {ctx.Rdi}{leftoverString}\n");
        ctx.IsTerminated = true;
        ctx.ExitCode = (int)ctx.Rdi;
    }

    private readonly Dictionary<string, ulong> _dlsymStubs = new Dictionary<string, ulong>();

    public void RegisterDlsymStub(string symbolName, ulong address)
    {
        _dlsymStubs[symbolName] = address;
    }

    private void SysGetpid(CpuContext ctx)
    {
        SetSuccess(ctx, 42); // Dummy PID
    }

    private void SysDlsymStub(CpuContext ctx)
    {
        ulong symbolAddr = ctx.Rsi;
        ulong outAddr = ctx.Rdx;

        string symbolName = "unknown";
        try
        {
            var span = _memory.GetSpan(symbolAddr, 256);
            int len = span.IndexOf((byte)0);
            if (len >= 0) symbolName = Encoding.UTF8.GetString(span.Slice(0, len));
        }
        catch { }

        CraziiEmuLog.For("HLE").Info($"[HLE] dlsym called for '{symbolName}'\n");

        if (!_dlsymStubs.TryGetValue(symbolName, out ulong stubAddr))
        {
            stubAddr = _memory.AllocateCpu(16);
            var stubSpan = _memory.GetSpan(stubAddr, 16);
            
            if (symbolName == "sceKernelDlsym")
            {
                // Returns a sys_dynlib_dlsym wrapper! (syscall 591)
                stubSpan[0] = 0xB8; stubSpan[1] = 0x4F; stubSpan[2] = 0x02; stubSpan[3] = 0x00; stubSpan[4] = 0x00; // mov eax, 591
                stubSpan[5] = 0x0F; stubSpan[6] = 0x05; // syscall
                stubSpan[7] = 0xC3; // ret
            }
            else if (symbolName.Contains("LoadModule") || symbolName.Contains("Close") || symbolName.Contains("Destroy"))
            {
                stubSpan[0] = 0x31; stubSpan[1] = 0xC0; // xor eax, eax
                stubSpan[2] = 0xC3; // ret
                for (int i = 3; i < 10; i++) stubSpan[i] = 0x90;
                stubSpan[10] = 0x0F; stubSpan[11] = 0x05; // syscall
                stubSpan[12] = 0xC3; // ret
            }
            else
            {
                stubSpan[0] = 0xB8; stubSpan[1] = 0x01; stubSpan[2] = 0x00; stubSpan[3] = 0x00; stubSpan[4] = 0x00; // mov eax, 1
                stubSpan[5] = 0xC3; // ret
                for (int i = 6; i < 10; i++) stubSpan[i] = 0x90;
                stubSpan[10] = 0x0F; stubSpan[11] = 0x05; // syscall
                stubSpan[12] = 0xC3; // ret
            }
            
            _dlsymStubs[symbolName] = stubAddr;
        }

        try
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(_memory.GetSpan(outAddr, 8), stubAddr);
        }
        catch (Exception ex)
        {
            CraziiEmuLog.For("HLE").Info($"[Error] SysDlsymStub failed to write stubAddr 0x{stubAddr:X} to outAddr 0x{outAddr:X}: {ex.Message}\n");
        }

        SetSuccess(ctx, 0); // Return 0 (success)
    }
}

