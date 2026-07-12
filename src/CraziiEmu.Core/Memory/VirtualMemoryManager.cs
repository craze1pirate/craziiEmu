// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace CraziiEmu.Core.Memory;

/// <summary>
/// A cross-platform unified virtual memory manager.
/// Reserves a large contiguous address space and commits physical pages on demand via OS page fault hooks.
/// On Windows, page faults are handled by a native x86-64 VEH stub that calls VirtualAlloc directly,
/// avoiding managed-to-unmanaged transition issues in .NET 10+.
/// </summary>
public sealed unsafe class VirtualMemoryManager : IDisposable
{
    private const ulong PageSize = 4096;

    // Windows memory allocation constants
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    /// <summary>
    /// Protection flag for executable, readable, and writable memory.
    /// Used when allocating the native VEH stub that must contain executable machine code.
    /// </summary>
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    // Linux mmap/mprotect constants
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int PROT_NONE = 0;
    private const int MAP_PRIVATE = 0x02;
    private const int MAP_ANONYMOUS = 0x20;
    private const int MAP_NORESERVE = 0x4000;

    /// <summary>Size of the native VEH stub in bytes (106 bytes of x86-64 machine code).</summary>
    private const int NativeVehStubSize = 0x6A;

    /// <summary>Byte offset within the native VEH stub where the pool base address is patched (8-byte immediate).</summary>
    private const int VehStubPoolBaseOffset = 0x17;

    /// <summary>Byte offset within the native VEH stub where the pool end address is patched (8-byte immediate).</summary>
    private const int VehStubPoolEndOffset = 0x26;

    /// <summary>Byte offset within the native VEH stub where the VirtualAlloc function pointer is patched (8-byte immediate).</summary>
    private const int VehStubVirtualAllocOffset = 0x50;

    private readonly ulong _poolSize;
    private readonly ulong _poolBase;
    private readonly ulong _gpuBaseOffset;

    private ulong _cpuOffset;
    private ulong _gpuOffset;

    // Page table for translating 115TB guest virtual addresses down to CPU-pool physical offsets
    private readonly ConcurrentDictionary<ulong, ulong> _highMemoryMap = new();

    /// <summary>Handle returned by AddVectoredExceptionHandler, used for cleanup.</summary>
    private readonly nint _vehHandle;

    /// <summary>Pointer to the native x86-64 VEH stub allocated with PAGE_EXECUTE_READWRITE (Windows only).</summary>
    private readonly nint _nativeVehStub;

    // Linux sigaction handler fields
    private readonly SigactionDelegate? _sigactionDelegate;
    private readonly GCHandle _sigactionDelegateHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualMemoryManager"/> class.
    /// </summary>
    /// <param name="poolSize">The total unified pool size to reserve in bytes.</param>
    public VirtualMemoryManager(ulong poolSize)
    {
        _poolSize = (poolSize + PageSize - 1) & ~(PageSize - 1);
        _gpuBaseOffset = _poolSize / 2;
        _gpuOffset = _gpuBaseOffset;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _poolBase = (ulong)VirtualAlloc(IntPtr.Zero, (nuint)_poolSize, MEM_RESERVE, PAGE_READWRITE);
            if (_poolBase == 0) throw new OutOfMemoryException("Failed to reserve virtual memory pool on Windows.");

            _nativeVehStub = CreateNativeVehStub(_poolBase, _poolBase + _poolSize);
            _vehHandle = AddVectoredExceptionHandler(1, _nativeVehStub);
        }
        else
        {
            _poolBase = (ulong)mmap(IntPtr.Zero, (nuint)_poolSize, PROT_NONE, MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE, -1, IntPtr.Zero);
            if (_poolBase == ulong.MaxValue || _poolBase == 0) throw new OutOfMemoryException("Failed to reserve virtual memory pool on Linux.");

            _sigactionDelegate = SigsegvHandler;
            _sigactionDelegateHandle = GCHandle.Alloc(_sigactionDelegate);
            InstallSigaction();
        }
    }

    /// <summary>
    /// Allocates a block of memory from the CPU-visible region of the pool.
    /// </summary>
    public ulong AllocateCpu(ulong size)
    {
        size = (size + PageSize - 1) & ~(PageSize - 1);
        ulong offset = Interlocked.Add(ref _cpuOffset, size) - size;
        if (offset + size > _gpuBaseOffset) throw new OutOfMemoryException("Out of CPU-visible memory.");
        return offset;
    }

    /// <summary>
    /// Allocates a block of memory from the GPU-visible region of the pool.
    /// </summary>
    public ulong AllocateGpu(ulong size)
    {
        size = (size + PageSize - 1) & ~(PageSize - 1);
        ulong offset = Interlocked.Add(ref _gpuOffset, size) - size;
        if (offset + size > _poolSize) throw new OutOfMemoryException("Out of GPU-visible memory.");
        return offset;
    }

    /// <summary>
    /// Maps a high guest virtual address (e.g. 0x700000000000) into a dynamically allocated safe region within the 32GB CPU pool.
    /// </summary>
    public void MapHighAddress(ulong highAddress, ulong size)
    {
        ulong safeOffset = AllocateCpu(size);
        _highMemoryMap[highAddress] = safeOffset;
        Console.WriteLine($"[VMM] Mapped High Address 0x{highAddress:X16} -> 0x{safeOffset:X16} (size: 0x{size:X})");
    }

    /// <summary>
    /// Safely gets a span of memory from the unified pool given an address.
    /// </summary>
    public Span<byte> GetSpan(ulong virtualAddress, int length)
    {
        ulong translatedAddress = virtualAddress;

        // If the address exceeds our 64GB boundary, query our explicit TLB page table mapping
        if (virtualAddress >= _poolSize)
        {
            // Mask the address down to its page boundary for the dictionary lookup
            ulong pageAlignedAddress = virtualAddress & ~(PageSize - 1);
            ulong pageOffset = virtualAddress - pageAlignedAddress;
            
            // For now, look up the exact base address (assuming MapHighAddress mapped this exact segment base).
            // A production page table would walk pages, but this suffices for contiguous module segment mapping.
            
            // To handle segment accesses dynamically, we'll try to find the nearest base in the map
            // For simplicity and speed in this emulator, we assume the SelfLoader accesses exactly the mapped base or within it.
            // Let's iterate if not found exactly (or we can just store the base).
            
            // To ensure compatibility without a complex page-walker, let's just resolve the closest base.
            // Since this is just for import stubs, the address accessed is usually exactly the mapped base + offset.
            ulong foundBase = 0;
            ulong mappedOffset = 0;
            bool found = false;
            
            foreach (var kvp in _highMemoryMap)
            {
                if (virtualAddress >= kvp.Key && virtualAddress < kvp.Key + 0x1000000) // Assumed 16MB max stub segment size
                {
                    foundBase = kvp.Key;
                    mappedOffset = kvp.Value;
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                throw new AccessViolationException($"High memory address 0x{virtualAddress:X16} was accessed without being explicitly mapped in the VMM Page Table.");
            }
            
            translatedAddress = mappedOffset + (virtualAddress - foundBase);
        }
        
        ulong hostPointer = _poolBase + translatedAddress;
        if (hostPointer < _poolBase || hostPointer + (uint)length > _poolBase + _poolSize)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualAddress), $"Address 0x{hostPointer:X16} (Length: {length}) is outside the bounds of the memory pool [0x{_poolBase:X16} - 0x{_poolBase + _poolSize:X16}].");
        }
        return new Span<byte>((void*)hostPointer, length);
    }

    /// <summary>
    /// Creates a native x86-64 VEH handler stub that commits pages on access violation
    /// without transitioning to managed code. This avoids the .NET 10+ restriction that
    /// prevents managed delegates from being called in VEH contexts.
    /// </summary>
    /// <param name="poolBase">The base address of the reserved memory pool.</param>
    /// <param name="poolEnd">The end address (exclusive) of the reserved memory pool.</param>
    /// <returns>A pointer to the allocated executable stub, suitable for AddVectoredExceptionHandler.</returns>
    private static nint CreateNativeVehStub(ulong poolBase, ulong poolEnd)
    {
        // Resolve VirtualAlloc function pointer from kernel32.dll at runtime.
        var kernel32 = NativeLibrary.Load("kernel32.dll");
        var virtualAllocPtr = NativeLibrary.GetExport(kernel32, "VirtualAlloc");

        // Allocate executable memory for the stub.
        var stub = (byte*)VirtualAlloc(
            IntPtr.Zero,
            (nuint)NativeVehStubSize,
            MEM_RESERVE | MEM_COMMIT,
            PAGE_EXECUTE_READWRITE);

        if (stub == null)
        {
            throw new OutOfMemoryException("Failed to allocate executable memory for native VEH stub.");
        }

        // The stub implements the following logic in pure x86-64 machine code:
        //
        //   int VehHandler(EXCEPTION_POINTERS* info) {
        //       EXCEPTION_RECORD* rec = info->ExceptionRecord;          // [rcx+0x00]
        //       if (rec->ExceptionCode != 0xC0000005) return 0;         // not access violation
        //       ulong fault = rec->ExceptionInformation[1];             // [rec+0x28]
        //       if (fault < poolBase || fault >= poolEnd) return 0;      // not our pool
        //       ulong page = fault & ~0xFFF;                            // align to page
        //       VirtualAlloc(page, 4096, MEM_COMMIT, PAGE_READWRITE);
        //       return -1;  // EXCEPTION_CONTINUE_EXECUTION
        //   }
        //
        // Byte layout (106 bytes total):
        //   0x00: prologue (save rbx, rsi, allocate shadow space)
        //   0x06: load ExceptionRecord, check code
        //   0x11: load fault address, range-check against pool
        //   0x33: page-align and call VirtualAlloc
        //   0x5A: return -1 (handled)
        //   0x61: return 0 (not handled)
        //   0x63: epilogue (restore stack, return)
        byte[] template =
        [
            // --- prologue ---
            0x53,                                               // 0x00: push rbx
            0x56,                                               // 0x01: push rsi
            0x48, 0x83, 0xEC, 0x28,                             // 0x02: sub rsp, 0x28

            // --- load ExceptionRecord and check code ---
            0x48, 0x8B, 0x01,                                   // 0x06: mov rax, [rcx]          ; rax = ExceptionRecord
            0x81, 0x38, 0x05, 0x00, 0x00, 0xC0,                 // 0x09: cmp dword [rax], 0xC0000005
            0x75, 0x50,                                         // 0x0F: jne not_handled (0x61)

            // --- load fault address ---
            0x48, 0x8B, 0x58, 0x28,                             // 0x11: mov rbx, [rax+0x28]     ; fault address

            // --- check fault >= poolBase ---
            0x48, 0xBE, 0, 0, 0, 0, 0, 0, 0, 0,                // 0x15: mov rsi, <poolBase>      ; patched at 0x17
            0x48, 0x3B, 0xDE,                                   // 0x1F: cmp rbx, rsi
            0x72, 0x3D,                                         // 0x22: jb not_handled (0x61)

            // --- check fault < poolEnd ---
            0x48, 0xBE, 0, 0, 0, 0, 0, 0, 0, 0,                // 0x24: mov rsi, <poolEnd>       ; patched at 0x26
            0x48, 0x3B, 0xDE,                                   // 0x2E: cmp rbx, rsi
            0x73, 0x2E,                                         // 0x31: jae not_handled (0x61)

            // --- page-align fault address ---
            0x48, 0x81, 0xE3, 0x00, 0xF0, 0xFF, 0xFF,           // 0x33: and rbx, ~0xFFF

            // --- call VirtualAlloc(page, 4096, MEM_COMMIT, PAGE_READWRITE) ---
            0x48, 0x89, 0xD9,                                   // 0x3A: mov rcx, rbx
            0xBA, 0x00, 0x10, 0x00, 0x00,                       // 0x3D: mov edx, 0x1000
            0x41, 0xB8, 0x00, 0x10, 0x00, 0x00,                 // 0x42: mov r8d, 0x1000         ; MEM_COMMIT
            0x41, 0xB9, 0x04, 0x00, 0x00, 0x00,                 // 0x48: mov r9d, 0x04           ; PAGE_READWRITE
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,                // 0x4E: mov rax, <VirtualAlloc>  ; patched at 0x50
            0xFF, 0xD0,                                         // 0x58: call rax

            // --- return EXCEPTION_CONTINUE_EXECUTION ---
            0xB8, 0xFF, 0xFF, 0xFF, 0xFF,                       // 0x5A: mov eax, -1
            0xEB, 0x02,                                         // 0x5F: jmp epilogue (0x63)

            // --- not_handled: return EXCEPTION_CONTINUE_SEARCH ---
            0x31, 0xC0,                                         // 0x61: xor eax, eax

            // --- epilogue ---
            0x48, 0x83, 0xC4, 0x28,                             // 0x63: add rsp, 0x28
            0x5E,                                               // 0x67: pop rsi
            0x5B,                                               // 0x68: pop rbx
            0xC3,                                               // 0x69: ret
        ];

        // Copy template into executable memory.
        fixed (byte* src = template)
        {
            Buffer.MemoryCopy(src, stub, NativeVehStubSize, template.Length);
        }

        // Patch the three 64-bit immediates.
        *(ulong*)(stub + VehStubPoolBaseOffset) = poolBase;
        *(ulong*)(stub + VehStubPoolEndOffset) = poolEnd;
        *(ulong*)(stub + VehStubVirtualAllocOffset) = (ulong)virtualAllocPtr;

        return (nint)stub;
    }

    /// <summary>
    /// Linux SIGSEGV handler that commits pages within the pool via mprotect.
    /// </summary>
    private void SigsegvHandler(int sig, IntPtr infoPtr, IntPtr ucontext)
    {
        var info = (siginfo_t*)infoPtr;
        ulong faultAddress = (ulong)info->si_addr;
        if (faultAddress >= _poolBase && faultAddress < _poolBase + _poolSize)
        {
            ulong pageAddress = faultAddress & ~(PageSize - 1);
            mprotect((IntPtr)pageAddress, (nuint)PageSize, PROT_READ | PROT_WRITE);
        }
    }

    /// <summary>
    /// Installs the SIGSEGV signal handler on Linux.
    /// </summary>
    private void InstallSigaction()
    {
        var sa = new sigaction_t();
        sa.sa_sigaction = Marshal.GetFunctionPointerForDelegate(_sigactionDelegate!);
        sa.sa_flags = 0x0004; // SA_SIGINFO
        sigaction(11, ref sa, IntPtr.Zero); // 11 is SIGSEGV
    }

    /// <summary>
    /// Disposes the virtual memory manager and releases the reserved pool.
    /// </summary>
    public void Dispose()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (_vehHandle != IntPtr.Zero) RemoveVectoredExceptionHandler(_vehHandle);
            if (_nativeVehStub != 0) VirtualFree((IntPtr)_nativeVehStub, 0, MEM_RELEASE);
            if (_poolBase != 0) VirtualFree((IntPtr)_poolBase, 0, MEM_RELEASE);
        }
        else
        {
            if (_poolBase != 0) munmap((IntPtr)_poolBase, (nuint)_poolSize);
            if (_sigactionDelegateHandle.IsAllocated) _sigactionDelegateHandle.Free();
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);
    [DllImport("kernel32.dll")] private static extern bool VirtualFree(IntPtr lpAddress, nuint dwSize, uint dwFreeType);
    [DllImport("kernel32.dll")] private static extern IntPtr AddVectoredExceptionHandler(uint First, IntPtr Handler);
    [DllImport("kernel32.dll")] private static extern uint RemoveVectoredExceptionHandler(IntPtr Handle);

    [DllImport("libc")] private static extern IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, IntPtr offset);
    [DllImport("libc")] private static extern int munmap(IntPtr addr, nuint length);
    [DllImport("libc")] private static extern int mprotect(IntPtr addr, nuint len, int prot);
    [DllImport("libc")] private static extern int sigaction(int signum, ref sigaction_t act, IntPtr oldact);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SigactionDelegate(int sig, IntPtr info, IntPtr ucontext);

    [StructLayout(LayoutKind.Sequential)]
    private struct sigaction_t {
        public IntPtr sa_sigaction;
        public ulong sa_mask;
        public int sa_flags;
        public IntPtr sa_restorer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct siginfo_t {
        public int si_signo;
        public int si_errno;
        public int si_code;
        public IntPtr si_addr;
        public fixed byte _pad[112];
    }
}
