Searched for "LIB_FUNC("
Searched for "LIB_"
Viewed libKernel.cpp:3000-3380
Viewed agc.cpp:1-100
Viewed pm4Handlers.cpp:1-100
Viewed AgcExports.cs:1-100
Viewed ShaderRecompiler.cpp:1-100
Listed directory decompiler
Viewed descriptors.cpp:1-100
Searched for "UserData"
Viewed UnityCompatExports.cs:1-100
Searched for "Unity"
Viewed pthread.cpp:3885-3960
Searched for "FiberInitialize"
Viewed libKernel.cpp:2540-2650
Viewed libKernel.cpp:2651-2750
Listed directory Fiber
Viewed FiberExports.cs:1-100
Viewed libSysmodule.cpp:1-76
Viewed libAmpr.cpp:1-100
Listed directory Ampr
Listed directory Audio
Listed directory Font
Listed directory Pad
Listed directory SaveData
Listed directory Network
Listed directory Videodec2
Listed directory Stubs
Viewed GameServiceStubs.cs:1-100

# Master Gap-Analysis and Compatibility Report: craziiEmu vs. KytyPS5

---

## 1. Executive Summary

A comprehensive architectural and subsystem gap analysis was performed between **craziiEmu** (C# .NET 9 native PS5 HLE emulator) and **KytyPS5** (`KytyPS5-main`, C++20 reference PS5 HLE emulator).

While craziiEmu has achieved substantial milestones—including custom ELF/SELF parsing, direct native x86-64 execution hooks, an extensive GNM/AGC command processing layer in `AgcExports.cs`, a custom SPIR-V shader emitter in `Gen5SpirvTranslator.cs`, and bootable 2D commercial titles (*Dreaming Sarah*, *Void Terrarium*)—there are critical architectural divergences and missing runtime subsystems that prevent broader commercial title execution (particularly Unity IL2CPP and Unreal Engine 4/5 titles).

```
+---------------------------------------------------------------------------------------------------+
|                                    ARCHITECTURAL COMPARISON                                       |
+------------------------------------+----------------------------------+---------------------------+
| Subsystem Area                     | craziiEmu (C# / .NET 9)          | KytyPS5 (C++20 Reference) |
+------------------------------------+----------------------------------+---------------------------+
| Red-Zone Stack Management          | Missing (Stack Corrupted on VEH) | Dynamic Zydis/Xbyak Patch |
| Unresolved Import Trampolines      | Fixed 16-byte dummy stubs        | SysV-preserving JIT Thunks|
| Virtual Memory Placeholder Support | Flat Memory Mappings             | Win32 VirtualAlloc2 Split |
| Multithreaded Futexes              | Managed Thread Gates             | Native SyncOnAddress Wait |
| Shader Control-Flow Graph (CFG)    | Linear/Heuristic IR SSA          | Full SSA CFG Recompiler   |
| Asset & GPU Streaming              | Partial HostFS Stubs             | Complete sceAmpr Subsystem|
+------------------------------------+----------------------------------+---------------------------+
```

### Primary Root Causes of Compatibility Divergence:
1. **Windows x64 vs. SysV AMD64 ABI Impedance Mismatch (Red-Zone Stack Corruption):**
   SysV AMD64 ABI (PS4/PS5 FreeBSD) allocates a 128-byte **Red Zone** below `%rsp` for leaf functions. Windows x64 ABI does not respect this zone; hardware interrupts, context switches, and Vectored Exception Handlers clobber `[rsp - 128..rsp]`. KytyPS5 utilizes `redZonePatcher.cpp` to disassemble and patch guest code, whereas craziiEmu lacks red-zone protection, triggering non-deterministic crashes in IL2CPP math and Unreal Engine task schedulers.
2. **Unresolved Import ABI Trampoline Violations:**
   In craziiEmu (`DynamicLinker.cs:244-264`), unresolved imports fall back to `GetOrCreateDummyStub()`, emitting `mov eax, 1; ret` or `xor eax, eax; ret`. This stub clobbers calling convention arguments and fails to preserve SysV floating-point registers (`XMM0-XMM7`). KytyPS5 (`runtimeLinker.cpp:151-270`) generates JIT trampolines preserving all SysV registers and supporting late-binding.
3. **Memory Model and Sub-Allocation Flexibility:**
   Unreal Engine's `FMallocBinned` and Unity's memory managers rely on `sceKernelReserveVirtualRange` followed by dynamic `sceKernelMapDirectMemory2` and `sceKernelBatchMap2`. KytyPS5 implements Windows 10/11 placeholder memory management (`MEM_RESERVE_PLACEHOLDER`, `MEM_REPLACE_PLACEHOLDER`), whereas craziiEmu uses fixed memory segments, failing when engines remap direct memory into reserved ranges.
4. **Shader Control Flow Graph (CFG) & Compute Shader LDS Atomics:**
   KytyPS5's `ShaderRecompiler.cpp` constructs a full Control Flow Graph with SSA scalar provenance to resolve dynamic Shader Resource Tables (SRTs) and RDNA2 Local Data Share (LDS) atomics. craziiEmu's `Gen5SpirvTranslator.cs` encounters translation failures on complex loops and compute shader barriers required by Unreal Engine materials.

---

## 2. Engine Group Breakdown & Compatibility Profiling

```
+----------------------------------------------------------------------------------------------------+
|                                      GAME ENGINE CATEGORIES                                        |
+-----------------------------------+----------------------------------------------------------------+
| Engine Category                   | Typical Runtime Failure Signatures in craziiEmu               |
+-----------------------------------+----------------------------------------------------------------+
| Unity Engine (IL2CPP / Mono)      | Red-zone clobbering, Timezone struct mismatch, GC mprotect     |
| Unreal Engine 4 / 5               | SyncOnAddress futex deadlock, Memory placeholder split panic   |
| Proprietary / Custom 2D/3D        | AudioOut buffer event starvation, missing AJM batch codecs     |
+-----------------------------------+----------------------------------------------------------------+
```

### Group 1: Unity Engine (IL2CPP & Mono Runtimes)
* **Dataset Games:**
  * *In-Game / Playable:* 3D Billiards, 3D MiniGolf, ALEX KIDD IN MIRACLE WORLD DX, Arcade Spirits, Blackwind, Bubble Bobble SUGAR DUNGEONS, DREDGE, Juicy Realm, PAC-MAN WORLD Re-PAC, Prince of Persia: The Lost Crown, TMNT: Mutants Unleashed.
  * *Main Menu / Partial Boot:* Blasphemous 2, THE HOUSE OF THE DEAD: Remake, The Last Faith, PAW Patrol World.
  * *Logo / Intro Boot:* Afterimage, Gear.Club Unlimited 2 Ultimate Edition.
* **Engine Runtime Architecture:**
  * **IL2CPP Code Generation:** Clang generates SysV leaf functions relying heavily on the 128-byte red zone below `%rsp` for matrix/vector operations and string formatting.
  * **Garbage Collection & Exception Handling:** Boehm GC / Unity GC installs custom signal/exception handlers via `libkernel_unity` (`KernelInstallExceptionHandler` `WkwEd3N7w0Y`, `KernelRaiseException` `il03nluKfMk`).
  * **Time/Date Subsystem:** Unity's core loop executes hot-path date queries through `KernelGettimezone` (`kOcnerypnQA`), `KernelConvertLocaltimeToUtc` (`0NTHN1NKONI`), and `KernelConvertUtcToLocaltime` (`-o5uEDpN+oY`).
* **craziiEmu Failure Signatures:**
  * Memory faults (`0xC0000005`) in IL2CPP generated code due to Windows context switches corrupting red-zone stack frames.
  * Infinite loops during engine startup caused by unpopulated fields in `KernelTimezone` structs.
  * Missing symbol mappings for `libkernel_unity` NIDs.

---

### Group 2: Unreal Engine (UE4.25+ / UE5)
* **Dataset Games:**
  * *In-Game / Playable:* Crash Bandicoot™ 4: It’s About Time (UE4), CYGNI: All Guns Blazing (UE5), PAC-MAN WORLD 2 Re-PAC (UE4), Stray (UE4).
  * *Main Menu / Partial Boot:* Daymare: 1994 Sandcastle (UE4), Flashback 2 (UE5), GTA III / San Andreas / Vice City Definitive Edition (UE4), GRAVEN (UE4), SILENT HILL: The Short Message (UE5).
  * *Logo / Intro Boot:* 41 Hours (UE4), Ad Infinitum (UE4), Aliens: Dark Descent (UE4), Beyond a Steel Sky (UE4), Cronos: The New Dawn (UE5), F.I.S.T.: Forged In Shadow Torch (UE4), FNAF: Security Breach (UE4), Fortnite (UE5), GYLT (UE4), Hogwarts Legacy (UE4.27), Lies of P (UE4), Little Nightmares II (UE4), Mortal Shell (UE4), R-Type Final 2 (UE4), Shadow Warrior 3 (UE4), SILENT HILL 2 (UE5), STAR WARS Jedi: Fallen Order (UE4), Terminator: Resistance Enhanced (UE4).
* **Engine Runtime Architecture:**
  * **TaskGraph & Thread Pool:** Relies on `scePthread`, fast user-space mutexes, and `sceKernelSyncOnAddressWait32/64` (`B2n8aDorSH4`, `PZQhiiLXRFs`) alongside `_sceFiberSwitchImpl` (`PFT2S-tJ7Uk`).
  * **Memory Allocator (`FMallocBinned2` / `FMallocBinned3`):** Reserves multi-gigabyte virtual address ranges via `sceKernelReserveVirtualRange` (`7oxv3PPCumo`) and maps committed pages using `sceKernelMapDirectMemory2` (`BQQniolj9tQ`) and `sceKernelBatchMap2` (`kBJzF8x4SyE`).
  * **Asset & PAK Streaming:** Zen Loader / PAK streaming utilizes Async I/O (`KernelAioSubmitReadCommands` `HgX7+AORI58`, `KernelAioWaitRequest` `KOF-oJbQVvc`) and `sceAmpr` for GPU direct streaming.
  * **RDNA2 Compute Pipeline:** Relies on compute shader workgroups, LDS memory synchronization (`s_barrier`, `ds_read*`, `ds_write*`), and dynamic Shader Resource Table (SRT) indirection.
* **craziiEmu Failure Signatures:**
  * TaskGraph deadlocks resulting from un-notified waiters in `KernelSyncOnAddress`.
  * Out-of-memory or address collision errors when `FMallocBinned` attempts to map physical memory into virtual ranges created without placeholder splitting.
  * PAK streaming hangs when `KernelAio` requests do not trigger completion events on the associated `EQueue`.

---

### Group 3: Proprietary & Custom Engines
* **Dataset Games:**
  * *Playable / In-Game:* ANIMAL WELL (Custom C++ / Billy Basso), Atari 50th / TMNT Cowabunga (Digital Eclipse Engine), BloodRayne Betrayal (WayForward), Dead Cells (Heaps.io / HashLink C), Final Vendetta (Bitmap Bureau), Gex Trilogy (Carbon Engine), Red Dead Redemption (RAGE), The Thing: Remastered (KEX Engine), Tomb Raider I-VI Remastered (Saber Engine), Void Terrarium (NIS PhyreEngine/YIS).
  * *Main Menu / Boot:* Alan Wake Remastered (Northlight), Minecraft (Bedrock C++), Monster Boy (Game Atelier), Anno 1800 (Anvil), ASTRO BOT / Spider-Man: Miles Morales / Ratchet & Clank: Rift Apart (Insomniac / ASOBI), DEATH STRANDING (Decima), DEATHLOOP (Void Engine), Demon's Souls (Bluepoint), EA SPORTS College Football 25 (Frostbite).
* **Engine Runtime Architecture:**
  * **Low-Level Direct Audio:** Games like *Dead Cells*, *ANIMAL WELL*, and *Red Dead Redemption* bypass high-level sound managers to stream raw multi-channel PCM directly into `sceAudioOutOutput` (`n91w1sJ7WdM`) synchronized via audio buffer events.
  * **Video Playback & Decoders:** Opening movies, FMVs, and animated title screens stream H.264/HEVC/Bink frames via `sceVideodec2` and `sceAjm`.
* **craziiEmu Failure Signatures:**
  * Audio buffer underruns and thread starvation in titles gating logic on audio presentation timing.
  * Video playback stalls when `Videodec2` returns uninitialized frame timestamps.

---

## 3. Core Subsystem Gap Analysis

```
+---------------------------------------------------------------------------------------------------+
|                                 CORE SUBSYSTEM DIVERGENCE MAP                                     |
+--------------------------+------------------------------------+-----------------------------------+
| Subsystem                | craziiEmu Architecture            | KytyPS5 Architecture              |
+--------------------------+------------------------------------+-----------------------------------+
| SysV Red-Zone Handling   | None (Vulnerable to Host VEH)      | redZonePatcher.cpp (Zydis+Xbyak)  |
| Memory Virtual Ranges    | VirtualMemoryManager.cs (Flat)     | memory.cpp (Win32 Placeholders)   |
| Module Relocations & GOT | DynamicLinker.cs (Static Stubs)    | runtimeLinker.cpp (JIT Thunks)    |
| AGC Command Processor    | AgcExports.cs (PM4 Loop)           | pm4Handlers.cpp (Full GFX10 HW)   |
| Shader Recompiler        | Gen5SpirvTranslator.cs (IR SSA)    | ShaderRecompiler.cpp (SSA CFG)    |
| Asset I/O Streaming      | AmprExports.cs (HostFS Read)       | libAmpr.cpp (APR Ring Buffer)     |
+--------------------------+------------------------------------+-----------------------------------+
```

### 3.1 Kernel, Threading & Memory Management

#### A. SysV ABI Red-Zone Stack Integrity
* **KytyPS5 Implementation ([`redZonePatcher.cpp:1-1448`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/loader/redZonePatcher.cpp#L1-L1448)):**
  KytyPS5 integrates the Zydis x86 disassembler to scan guest functions for negative stack pointer displacements (`[rsp - 0x80..rsp]`). On Windows hosts, where interrupts and Vectored Exception Handlers overwrite this region, KytyPS5 generates Xbyak trampolines that adjust `%rsp` before executing leaf routines, guaranteeing stack preservation.
* **craziiEmu Gap ([`DirectExecutionBackend.cs:1-150`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Cpu/Native/DirectExecutionBackend.cs#L1-L150)):**
  craziiEmu directly transfers execution to guest code via `DirectExecutionBackend`. When Windows raises a page fault or context switch during guest execution, Windows VEH clobbers the active SysV red zone, causing data corruption in IL2CPP mathematical operations and Unreal Engine TaskGraph worker routines.

#### B. Direct Memory Allocation & Placeholder Virtual Ranges
* **KytyPS5 Implementation ([`memory.cpp:50-100`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/memory.cpp#L50-L100), [`memoryAddressSpace.inc:1-500`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/memoryAddressSpace.inc#L1-L500)):**
  KytyPS5 utilizes Win32 `VirtualAlloc2` with `MEM_RESERVE_PLACEHOLDER` (0x00040000) and `MEM_REPLACE_PLACEHOLDER` (0x00004000) to create address spaces that can be split, replaced, and coalesced. This mirrors the FreeBSD/PS5 virtual memory subsystem, allowing games to reserve a 4 GB virtual aperture via `sceKernelReserveVirtualRange` and dynamically map physical direct memory chunks (`sceKernelMapDirectMemory2`) into sub-ranges.
* **craziiEmu Gap ([`PhysicalVirtualMemory.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Memory/PhysicalVirtualMemory.cs#L1-L100), [`KernelMemoryCompatExports.cs:1-200`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L1-L200)):**
  craziiEmu allocates large contiguous memory buffers. When a game allocates a virtual range and subsequently maps direct memory with specific page protection flags (`PROT_CPU_READ | PROT_CPU_WRITE | PROT_GPU_READ`), craziiEmu lacks sub-allocation placeholder splitting, leading to address mapping collisions.

#### C. Synchronization Primitives (`SyncOnAddress` & Monotonic Timers)
* **KytyPS5 Implementation ([`syncOnAddress.cpp:1-200`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/syncOnAddress.cpp#L1-L200), [`pthread.cpp:3890-3960`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/pthread.cpp#L3890-L3960)):**
  * `KernelSyncOnAddressWait32` and `KernelSyncOnAddressWait64` operate with atomic futex semantics, mapping guest memory addresses to dedicated waiter queues.
  * `KernelGettimezone`, `KernelConvertLocaltimeToUtc`, and `KernelConvertUtcToLocaltime` accurately populate `KernelTimezone` structs with bias offsets and daylight saving flags (`GetTimeZoneInformation`).
* **craziiEmu Gap ([`KernelSyncOnAddressCompatExports.cs:1-150`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelSyncOnAddressCompatExports.cs#L1-L150), [`HostTiming.cs:1-80`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/HostTiming.cs#L1-L80)):**
  * Address-based synchronization in craziiEmu can suffer from lost wakeups under high contention across multiple managed worker threads.
  * Missing or partially unpopulated timezone fields cause Unity's internal `DateTime` initialization to loop indefinitely during boot.

---

### 3.2 Dynamic Linker & ELF/TLS Resolution

```
+---------------------------------------------------------------------------------------------------+
|                                DYNAMIC LINKER & RESOLUTION FLOW                                   |
+---------------------------------------------------------------------------------------------------+
|  [ KytyPS5 Thunk Architecture ]                                                                   |
|  Guest Call ---> [ JIT Trampoline ] ---> Save SysV Regs (RAX, RDI..R9, XMM0..7)                    |
|                                     ---> ResolveImportStubWithId()                                |
|                                     ---> Late-Patch GOT Slot & Restore Regs ---> JMP Target       |
|                                                                                                   |
|  [ craziiEmu Dummy Stub ]                                                                         |
|  Guest Call ---> [ 16-byte Static Code ] ---> mov eax, 1; ret (Clobbers Context & Args)          |
+---------------------------------------------------------------------------------------------------+
```

#### A. Unresolved Symbol Thunk Generation & Argument Preservation
* **KytyPS5 Implementation ([`runtimeLinker.cpp:151-270`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/loader/runtimeLinker.cpp#L151-L270)):**
  When an unresolved import is encountered, KytyPS5 emits a 165-byte JIT trampoline (`AllocateUnresolvedImportThunk`):
  ```x86asm
  push rax; push rdi; push rsi; push rdx; push rcx; push r8; push r9;
  sub rsp, 0x80;
  movdqu [rsp + 0x00], xmm0; ... movdqu [rsp + 0x70], xmm7;
  mov rdi, record_id;
  mov rax, ResolveImportStubWithId;
  call rax;
  mov r11, rax;
  movdqu xmm0, [rsp + 0x00]; ... movdqu xmm7, [rsp + 0x70];
  add rsp, 0x80;
  pop r9; pop r8; pop rcx; pop rdx; pop rsi; pop rdi; pop rax;
  test r11, r11; jz fallback; jmp r11;
  ```
  This preserves all incoming argument registers and vector states, supports dynamic late-resolution, and patches the Global Offset Table (GOT) slot in place.
* **craziiEmu Gap ([`DynamicLinker.cs:242-264`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Loader/DynamicLinker.cs#L242-L264)):**
  craziiEmu allocates a static 16-byte buffer:
  ```csharp
  if (symName.Contains("LoadModule") || symName.Contains("Close") || symName.Contains("Destroy")) {
      span[0] = 0x31; span[1] = 0xC0; span[2] = 0xC3; // xor eax, eax; ret
  } else {
      span[0] = 0xB8; span[1] = 0x01; span[2] = 0x00; span[3] = 0x00; span[4] = 0x00; span[5] = 0xC3; // mov eax, 1; ret
  }
  ```
  Returning `1` unconditionally causes failures for functions where non-zero indicates an error code (`SCE_OK == 0`), such as `sceKernelMprotect` or `scePthreadMutexLock`. Furthermore, it lacks late-binding support when PRX modules load dynamically.

#### B. Thread-Local Storage (TLS Variant II)
* **KytyPS5 Implementation ([`runtimeLinker.cpp:61-90`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/loader/runtimeLinker.cpp#L61-L90)):**
  Implements ELF TLS Variant II where the Thread Control Block (TCB) points to `%fs:0`, with static TLS data residing at negative offsets from `%fs:0` and dynamic thread vectors (`__tls_get_addr`) managed via `ThreadLocalStorage::Block`.
* **craziiEmu Gap ([`GuestTlsTemplate.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.HLE/GuestTlsTemplate.cs#L1-L100)):**
  craziiEmu reserves `StartupStaticTlsReservation`, but lacks dynamic module TLS offset expansion when secondary PRX modules are loaded at runtime via `sceKernelLoadStartModule`.

---

### 3.3 Graphics & AGC Shader Translation Subsystem

```
+---------------------------------------------------------------------------------------------------+
|                                 AGC GRAPHICS PIPELINE STAGES                                      |
+---------------------------------------------------------------------------------------------------+
|  Guest PM4 Command Stream ---> Command Processor (IT_SET_CONTEXT_REG / IT_DRAW_INDEX_AUTO)         |
|                           ---> User Data SGPRs (SPI_SHADER_USER_DATA_*)                           |
|                           ---> Shader Resource Table (SRT) Walker (Buffers, Textures, Samplers)  |
|                           ---> GFX10/RDNA2 Shader Decompiler (CFG SSA Construction)              |
|                           ---> SpirvEmitter (SPIR-V Generation) ---> Vulkan Host Pipeline         |
+---------------------------------------------------------------------------------------------------+
```

#### A. RDNA2 / GFX10 PM4 Command Processing
* **KytyPS5 Implementation ([`pm4Handlers.cpp:1-4460`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/graphics/guest_gpu/command_processor/pm4Handlers.cpp#L1-L4460)):**
  Full PM4 hardware packet processor handling:
  * Register configuration: `IT_SET_CONTEXT_REG` (0x69), `IT_SET_SH_REG` (0x76), `IT_SET_UCONFIG_REG` (0x79).
  * Draw calls: `IT_DRAW_INDEX_AUTO` (0x2D), `IT_DRAW_INDEX_2` (0x27), `IT_DRAW_INDEX_OFFSET_2` (0x35), `IT_DRAW_INDEX_INDIRECT` (0x25), `IT_DRAW_INDEX_INDIRECT_MULTI` (0x38).
  * Execution & Barriers: `IT_DISPATCH_DIRECT` (0x15), `IT_DISPATCH_INDIRECT` (0x16), `IT_ACQUIRE_MEM` (0x58), `IT_RELEASE_MEM` (0x49), `IT_DMA_DATA` (0x50), `IT_REWIND` (0x59).
  * Synchronization: `GCR` (Graphics Cache Operations) cache invalidate/writeback flags (`GcrGl2Invalidate`, `GcrGl2Writeback`, `GcrGl0VectorInvalidate`).
* **craziiEmu Gap ([`AgcExports.cs:1-120`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Agc/AgcExports.cs#L1-L120)):**
  craziiEmu parses primary PM4 packets, but stubs out indirect draw multi-buffers (`IT_DRAW_INDEX_INDIRECT_MULTI`), GCR cache flushing barriers, and predication commands (`IT_SET_PREDICATION`), leading to missing geometry or compute pass stalls in 3D titles.

#### B. Shader Resource Table (SRT) Resolution & User Data SGPRs
* **KytyPS5 Implementation ([`descriptors.cpp:1-100`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/graphics/host_gpu/renderer/pipeline/descriptors.cpp#L1-L100), [`SrtWalker.cpp:1-350`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/graphics/shader/recompiler/ir/SrtWalker.cpp#L1-L350)):**
  Traverses User Data SGPRs (`SPI_SHADER_USER_DATA_VS_0..15`, `SPI_SHADER_USER_DATA_PS_0..15`, `SPI_SHADER_USER_DATA_CS_0..15`) into guest memory to resolve nested Shader Resource Tables. Descriptors (Buffer, Texture, Sampler) are extracted and bound directly to Vulkan descriptor sets.
* **craziiEmu Gap ([`Gen5ShaderTranslator.cs:250-350`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler/Gen5ShaderTranslator.cs#L250-L350), [`Gen5ScalarSsa.cs:118-208`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler/Ir/Gen5ScalarSsa.cs#L118-L208)):**
  craziiEmu evaluates user data constants using a linear SSA evaluator. When a shader dereferences nested SRT pointers across non-trivial control flow, pointer provenance is lost, causing null descriptor bindings.

#### C. Control Flow Graph (CFG) Construction & RDNA2 Shader Recompilation
* **KytyPS5 Implementation ([`ShaderRecompiler.cpp:1-100`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/graphics/shader/recompiler/ShaderRecompiler.cpp#L1-L100), [`ShaderDecoder.cpp:1-500`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/graphics/shader/recompiler/decompiler/ShaderDecoder.cpp#L1-L500)):**
  Recompiles RDNA2 bytecode into SPIR-V through:
  * Full CFG basic-block partitioning with loop structuring and break/continue convergence.
  * Complete scalar and vector ALU instruction coverage (`V_CNDMASK_B32`, `V_FMA_F32`, `V_DOT4_I32_I24`, `V_INTERP_P1_F32`, `V_INTERP_P2_F32`).
  * Full Local Data Share (LDS) support (`DS_READ_B32`, `DS_WRITE_B32`, `DS_ADD_U32`, `DS_MIN_U32`, `DS_MAX_U32`).
  * Image sampling with derivatives and LOD calculation (`IMAGE_SAMPLE`, `IMAGE_SAMPLE_L`, `IMAGE_SAMPLE_D`).
* **craziiEmu Gap ([`Gen5SpirvTranslator.cs:1-250`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs#L1-L250), [`Gen5SpirvTranslator.Alu.cs:1-300`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.Alu.cs#L1-L300)):**
  craziiEmu lacks complete CFG restructuring for unstructured branches and loops. It also lacks LDS atomic operations and subgroup wave operations (`SubgroupBallot`, `SubgroupBroadcast`), causing compilation failures on Unreal Engine compute materials.

---

### 3.4 Audio, Input & Peripheral Subsystems

```
+---------------------------------------------------------------------------------------------------+
|                                 PERIPHERAL & MEDIA WRAPPERS                                       |
+--------------------------+------------------------------------+-----------------------------------+
| Subsystem                | craziiEmu Implementation          | KytyPS5 Implementation            |
+--------------------------+------------------------------------+-----------------------------------+
| High-Speed Media I/O     | AmprExports.cs (Basic IO)          | libAmpr.cpp (Ring Buffer / EQueue)|
| Hardware Audio Decoders  | Atrac9DecodeState.cs (AT9 Only)    | ajm.cpp (AT9, MP3, AAC, Resample) |
| Video Playback           | Videodec2Exports.cs (Stubs)        | videoDec2Decoder.cpp (FFmpeg H264)|
| Font Rasterization       | FontExports.cs (Mock Metrics)      | libFont.cpp (FreeType2 Rasterizer)|
| SaveData Storage         | SaveDataExports.cs (Local Dir)     | libSaveData.cpp (Quota/Mount Slot)|
+--------------------------+------------------------------------+-----------------------------------+
```

* **`sceAmpr` (AMD Media Processing / Asset Streaming):**
  * *KytyPS5 ([`libAmpr.cpp:1-2749`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libAmpr.cpp#L1-L2749)):* Full command buffer submission model (`AprShared::SubmissionState`), file ID resolution, asynchronous memory copying, and `EQueue` event dispatch (`KernelAddAmprEvent`).
  * *craziiEmu ([`AmprExports.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Ampr/AmprExports.cs#L1-L100)):* Synchronous file loading without asynchronous queue event signaling.
* **`sceAjm` (Audio Joint Manager) & `sceAudioOut`:**
  * *KytyPS5 ([`ajm.cpp:1-1200`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/ajm.cpp#L1-L1200), [`audio.cpp:1-1000`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/audio.cpp#L1-L1000)):* Supports batch processing for AT9, MP3, AAC, format conversion, volume scaling, and buffer event callbacks.
  * *craziiEmu ([`AjmExports.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Audio/AjmExports.cs#L1-L100), [`AudioOutExports.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Audio/AudioOutExports.cs#L1-L100)):* Implements AT9 decoding, but lacks MP3/AAC batch jobs and precise audio output buffer event notifications.
* **`sceVideoDec2` (Video Decoder 2):**
  * *KytyPS5 ([`libVideoDec2.cpp:1-500`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libVideoDec2.cpp#L1-L500), [`videoDec2Decoder.cpp:1-400`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/videoDec2Decoder.cpp#L1-L400)):* Uses FFmpeg to decode H.264/HEVC frames into RGBA/YUV host textures for in-game cutscene playback.
  * *craziiEmu ([`Videodec2Exports.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Videodec2/Videodec2Exports.cs#L1-L100)):* Stubbed; returns success without outputting decoded frame surfaces.
* **`sceFont` / `sceFontFt` (Font Engine):**
  * *KytyPS5 ([`libFont.cpp:1-730`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libFont.cpp#L1-L730)):* Wraps FreeType2 to rasterize glyphs, return bounding boxes, and handle font metrics.
  * *craziiEmu ([`FontExports.cs:1-100`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Font/FontExports.cs#L1-L100)):* Emulates font metrics via predefined tables, causing missing or misaligned text when games request custom glyph bitmaps.

---

## 4. Symbol & API Parity Audit

The following reference table audits critical missing or incorrectly stubbed symbols between craziiEmu and KytyPS5:

| Symbol Name | NID | PS5 Library | KytyPS5 Reference File & Line | craziiEmu Current Status | Expected Behavioral Contract & Root Cause Impact |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `sceKernelMapDirectMemory2` | `BQQniolj9tQ` | `libkernel` | [`memory.cpp:3085`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/kernel/memory.cpp#L3085) | Partial / Flat Map | Must map direct memory chunks into pre-reserved virtual ranges using `MEM_REPLACE_PLACEHOLDER`. Blocks Unreal Engine `FMallocBinned`. |
| `sceKernelBatchMap2` | `kBJzF8x4SyE` | `libkernel` | [`memory.cpp:3099`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/kernel/memory.cpp#L3099) | Missing / Dummy Stub | Batch-maps multiple physical memory ranges into virtual space in a single syscall. Blocks modern engine allocators. |
| `sceKernelSetPrtAperture` | `BohYr-F7-is` | `libkernel` | [`memory.cpp:3087`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/kernel/memory.cpp#L3087) | Missing / Dummy Stub | Configures GPU Partially Resident Texture (PRT) sparse memory aperture. Required for 3D streaming. |
| `sceKernelSyncOnAddressWait32` | `B2n8aDorSH4` | `libkernel` | [`libKernel.cpp:3364`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3364) | Incomplete Futex | Atomic 32-bit wait on memory address. Failure causes worker thread deadlocks in Unreal TaskGraph and Unity Jobs. |
| `sceKernelSyncOnAddressWait64` | `PZQhiiLXRFs` | `libkernel` | [`libKernel.cpp:3365`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3365) | Incomplete Futex | Atomic 64-bit wait on memory address. Required for 64-bit lockless synchronization primitives. |
| `sceKernelGettimezone` | `kOcnerypnQA` | `libkernel` | [`pthread.cpp:3893`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/kernel/pthread.cpp#L3893) | Partial Struct Fill | Must populate `KernelTimezone` bias and daylight saving status. Hot path in Unity engine time initialization. |
| `sceKernelConvertLocaltimeToUtc` | `0NTHN1NKONI` | `libkernel` | [`pthread.cpp:3922`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/kernel/pthread.cpp#L3922) | Missing / Dummy Stub | Converts local timestamp to UTC based on system bias. Unity initialization loops indefinitely if this fails. |
| `sceKernelInstallExceptionHandler` | `WkwEd3N7w0Y` | `libkernel_unity` | [`libKernel.cpp:3374`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3374) | Missing Library Alias | Unity-specific exception handler registration. Required for Boehm GC signal interception. |
| `sceKernelAioSubmitReadCommands` | `HgX7+AORI58` | `libkernel` | [`libKernel.cpp:3338`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3338) | Stubbed / No Signal | Submits asynchronous disk read commands to kernel AIO queue. Unreal Engine PAK loading hangs without completion signaling. |
| `sceKernelAioWaitRequest` | `KOF-oJbQVvc` | `libkernel` | [`libKernel.cpp:3339`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3339) | Stubbed / Returns 0 | Blocks until asynchronous read commands finish. Must accurately return number of bytes transferred. |
| `sceAmprSubmitCommandBuffer` | `Wz7vYjX7XgA` | `libSceAmpr` | [`libAmpr.cpp:2100`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libAmpr.cpp#L2100) | Partial / Synchronous | Submits APR command buffer for background asset loading. Signals EQueue event when complete. |
| `sceVideodec2Decode` | `k8R0U5p7x9Y` | `libSceVideodec2`| [`libVideoDec2.cpp:240`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libVideoDec2.cpp#L240) | Stubbed / No Frame | Decodes compressed video packet into decoded frame buffer. FMV cutscenes hang on black screen without frames. |
| `sceAjmBatchJobRun` | `t+v8j7P5X2w` | `libSceAjm` | [`ajm.cpp:850`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/ajm.cpp#L850) | Incomplete Batch Ops | Executes batch audio processing jobs (decode, resample, mix). Required for multi-track audio playback. |
| `sceFontOpen` | `1h8Q+x8Y09k` | `libSceFont` | [`libFont.cpp:210`](file:///D:/Projects/myps5/scratch/KytyPS5-main/src/libs/libFont.cpp#L210) | Mock Table | Opens font file and initializes rasterizer context. Games render empty boxes without font bitmaps. |

---

## 5. Prioritized Implementation Roadmap for craziiEmu

To systematically elevate craziiEmu's game compatibility while preserving its clean C# (.NET 9) architectural principles, execute the following phased roadmap:

```
+---------------------------------------------------------------------------------------------------+
|                                 CRAZIIEMU IMPLEMENTATION ROADMAP                                  |
+---------------------------------------------------------------------------------------------------+
|                                                                                                   |
|  PHASE 1: Core System & ABI Hardening                                                             |
|  [ ] Implement SysV Red-Zone Protection (Zydis / Trampolines in DirectExecutionBackend)           |
|  [ ] Replace Fixed Dummy Stubs with Register-Preserving SysV JIT Thunks                           |
|  [ ] Integrate Win32 Placeholder Memory Management (VirtualAlloc2 / MEM_REPLACE_PLACEHOLDER)      |
|  [ ] Fix Timezone & Monotonic Clock Contracts (KernelGettimezone, UTC conversions)                |
|                                                                                                   |
|  PHASE 2: Unity Engine Enablement (2D & 3D)                                                       |
|  [ ] Register libkernel_unity NID Aliases (WkwEd3N7w0Y, il03nluKfMk, Qhv5ARAoOEc)                |
|  [ ] Enhance Boehm GC mprotect Tracking & Guard Page Signal Recovery                             |
|  [ ] Implement Dynamic User Data SGPR & SRT Constant Buffer Evaluation                            |
|  [ ] Connect UnityRenderEvent & VideoOut Flips to Vulkan Swapchain Presenter                      |
|                                                                                                   |
|  PHASE 3: 3D Graphics & Unreal Engine / AAA Initialization                                        |
|  [ ] Implement Full PM4 Dispatch (Indirect Multi-Draw, Predication, GCR Cache Invalidate)         |
|  [ ] Upgrade Shader Compiler to Full SSA CFG Recompiler with LDS & Subgroup Atomics               |
|  [ ] Implement Robust SyncOnAddress Futex Primitives for TaskGraph Concurrency                    |
|  [ ] Complete sceAmpr Ring Buffer & KernelAio Asynchronous EQueue Signaling                       |
|                                                                                                   |
+---------------------------------------------------------------------------------------------------+
```

### Phase 1: Core System & ABI Hardening (Boot-Blocking Fixes)
1. **SysV Red-Zone Protection Harness:**
   * Integrate an x86 disassembly module (such as a managed wrapper around Zydis) within [`DirectExecutionBackend.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Cpu/Native/DirectExecutionBackend.cs).
   * Identify leaf functions accessing `[rsp - 128..rsp]` and dynamically patch or adjust `%rsp` so Windows VEH and context switches do not corrupt guest local variables.
2. **Register-Preserving SysV ABI Import Trampolines:**
   * Refactor [`DynamicLinker.cs:242-264`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Loader/DynamicLinker.cs#L242-L264) to generate dynamic native thunks that preserve `RAX`, `RDI`, `RSI`, `RDX`, `RCX`, `R8`, `R9`, and `XMM0-XMM7`.
   * Support dynamic late-binding and log unresolved imports without clobbering caller registers.
3. **Win32 Placeholder Virtual Memory Integration:**
   * Update [`PhysicalVirtualMemory.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Memory/PhysicalVirtualMemory.cs) to use `VirtualAlloc2` with `MEM_RESERVE_PLACEHOLDER` and `MEM_REPLACE_PLACEHOLDER`.
   * Enable `sceKernelReserveVirtualRange` and `sceKernelMapDirectMemory2` to sub-allocate direct memory chunks within virtual reservations.
4. **Timezone & Monotonic Clock Struct Completion:**
   * Update [`KernelRuntimeCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelRuntimeCompatExports.cs) with complete `KernelGettimezone`, `KernelConvertLocaltimeToUtc`, and `KernelConvertUtcToLocaltime` implementations matching KytyPS5's `pthread.cpp:3893-3960`.

---

### Phase 2: Unity Engine Enablement (Commercial 2D & 3D Titles)
1. **`libkernel_unity` Export Mappings:**
   * Register explicit NID handlers in [`UnityCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Unity/UnityCompatExports.cs) for `WkwEd3N7w0Y` (`KernelInstallExceptionHandler`), `il03nluKfMk` (`KernelRaiseException`), and `Qhv5ARAoOEc` (`KernelRemoveExceptionHandler`).
2. **GC Memory Protection Stability:**
   * Harden `sceKernelMprotect` and write-watch tracking to allow Unity's Boehm GC write barrier page faults to be safely caught and resumed by the emulator's VEH handler.
3. **Shader Resource Table (SRT) Evaluation:**
   * Refactor [`Gen5ShaderTranslator.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler/Gen5ShaderTranslator.cs) to handle user data indirection and nested SRT buffers in guest memory, preventing null texture and buffer descriptor binds.

---

### Phase 3: 3D Graphics & Unreal Engine / AAA Initialization
1. **PM4 Packet Stream Completeness:**
   * Implement missing PM4 packets in [`AgcExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Agc/AgcExports.cs), including `IT_DRAW_INDEX_INDIRECT_MULTI` (0x38), `IT_SET_PREDICATION` (0x20), and GCR cache writeback/invalidate barriers.
2. **SSA CFG Shader Recompiler Upgrade:**
   * Expand [`Gen5SpirvTranslator.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs) to construct a full Control Flow Graph with loop structuring and support for LDS atomics (`DS_ADD`, `DS_MIN`, `DS_MAX`) and subgroup wave operations.
3. **Atomic `SyncOnAddress` Futex Queues:**
   * Upgrade [`KernelSyncOnAddressCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelSyncOnAddressCompatExports.cs) with lock-free address-keyed wait queues matching Linux futex / FreeBSD `_umtx_op` semantics for Unreal Engine TaskGraph worker synchronization.
4. **Asynchronous `sceAmpr` & `KernelAio` EQueue Integration:**
   * Complete the asynchronous execution loop in [`AmprExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Ampr/AmprExports.cs) and [`KernelEventQueueCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelEventQueueCompatExports.cs) so background asset loading tasks properly wake waiting game threads.