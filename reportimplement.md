To make the commercial titles that are fully playable in **KytyPS5** playable in **craziiEmu**, we need to execute a systematic 4-Tier Engineering Strategy.

Each tier targets a specific engine category and resolves the exact low-level technical blockers preventing craziiEmu from booting and running those titles.

---

```
+---------------------------------------------------------------------------------------------------+
|                                  CRAZIIEMU PLAYABILITY ROADMAP                                    |
+---------------------------------------------------------------------------------------------------+
|                                                                                                   |
|  [ TIER 1: 2D & Custom Scripted Engines ] (Immediate Low-Hanging Fruit)                           |
|   Games: ANIMAL WELL, Dead Cells, Final Vendetta, Gex Trilogy, BloodRayne Betrayal, Atari 50th    |
|   Unlocks: Precise AudioOut Buffer Events, SaveData Directory Mounts, 2D Blit Shaders             |
|                                                                                                   |
|  [ TIER 2: Unity Engine Enablement ] (IL2CPP 2D & 3D Suite)                                       |
|   Games: DREDGE, Prince of Persia, ALEX KIDD, Blackwind, TMNT: Mutants Unleashed, Juicy Realm    |
|   Unlocks: SysV Red-Zone Protection, libkernel_unity Aliases, Timezone Structs, GC mprotect     |
|                                                                                                   |
|  [ TIER 3: Custom 3D & Classic Remasters ]                                                        |
|   Games: Tomb Raider I-VI Remastered, The Thing: Remastered, Raiden III/IV, Red Dead Redemption   |
|   Unlocks: RDNA2 Surface Untiling, SRT Descriptor Dereferencing, Indirect Draw Offsets            |
|                                                                                                   |
|  [ TIER 4: Unreal Engine 4 & 5 Suite ]                                                            |
|   Games: Stray, Crash Bandicoot 4, PAC-MAN WORLD 2 Re-PAC, CYGNI: All Guns Blazing                |
|   Unlocks: Win32 Placeholder Memory (FMallocBinned), SyncOnAddress Futexes, Compute LDS Atomics   |
|                                                                                                   |
+---------------------------------------------------------------------------------------------------+
```

---

## Tier 1: 2D & Custom Scripted Engines
**Target Games:** *ANIMAL WELL*, *Dead Cells*, *Final Vendetta*, *Gex Trilogy*, *BloodRayne Betrayal: Fresh Bites*, *Atari 50th*, *TMNT: The Cowabunga Collection*.

### Actionable Deliverables:

#### 1. Audio Presentation & Buffer Synchronization
* **Target File:** [`AudioOutExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Audio/AudioOutExports.cs)
* **Problem:** *Dead Cells* and *ANIMAL WELL* stream raw multi-channel PCM through `sceAudioOutOutput` (`n91w1sJ7WdM`) and wait on `sceAudioOutRegisterOutputBufferEvent` (`8e0-jM5s7XY`). Currently, craziiEmu does not trigger the event flag on buffer consumption, causing audio thread starvation and deadlocks.
* **Implementation:**
  * Connect `WinMmAudioPort` / SDL audio callback to count consumed audio frames.
  * When a buffer completes playback, trigger the registered `EQueue` event or `EventFlag` using `KernelEventQueueCompatExports.TriggerUserEvent()`.

#### 2. Complete `SaveData` Directory Slot Mounting
* **Target File:** [`SaveDataExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/SaveData/SaveDataExports.cs)
* **Problem:** 2D engines query `sceSaveDataMount3` (`ZP4e7rlzOUk`) and `sceSaveDataGetMountInfo` (`65VH0Qaaz6s`). If the returned mount point string (`/savedata0`) is invalid or has wrong permissions, the games halt at the title screen.
* **Implementation:**
  * Implement KytyPS5's `SaveDataMountSlots` (`saveDataMountSlots.h`), mapping virtual mount point `/savedata0` to `%APPDATA%/craziiEmu/SaveData/<TitleID>/<DirName>`.

---

## Tier 2: Unity Engine Enablement (IL2CPP 2D & 3D Suite)
**Target Games:** *DREDGE*, *Prince of Persia: The Lost Crown*, *ALEX KIDD IN MIRACLE WORLD DX*, *Blackwind*, *Bubble Bobble SUGAR DUNGEONS*, *Juicy Realm*, *PAC-MAN WORLD Re-PAC*, *TMNT: Mutants Unleashed*, *3D Billiards*, *3D MiniGolf*.

### Actionable Deliverables:

#### 1. SysV AMD64 Red-Zone Protection Layer
* **Target File:** [`DirectExecutionBackend.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Cpu/Native/DirectExecutionBackend.cs)
* **Problem:** Unity IL2CPP code relies on Clang's 128-byte red zone (`[rsp - 0x80..rsp]`) for matrix calculations and string operations. On Windows, context switches and Vectored Exception Handlers overwrite this area, causing non-deterministic `0xC0000005` memory faults.
* **Implementation:**
  * Port KytyPS5's [`redZonePatcher.cpp`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/loader/redZonePatcher.cpp) logic to C# (using managed Zydis / x86 decoder):
  ```csharp
  // Scan guest JIT code for leaf functions referencing [rsp - 0x80..rsp]
  // Adjust RSP at function entry or emit a trampoline allocating 128 bytes of shadow stack.
  ```

#### 2. Complete Unity Date/Time Subsystem
* **Target File:** [`KernelRuntimeCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelRuntimeCompatExports.cs)
* **Problem:** Unity's internal engine clock loops indefinitely if `KernelGettimezone` (`kOcnerypnQA`) or `KernelConvertLocaltimeToUtc` (`0NTHN1NKONI`) returns unpopulated struct fields.
* **Implementation:**
  * Mirror KytyPS5's [`pthread.cpp:3893-3960`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/pthread.cpp#L3893-L3960):
  ```csharp
  [SysAbiExport(Nid = "kOcnerypnQA", ExportName = "sceKernelGettimezone")]
  public static int KernelGettimezone(CpuContext ctx) {
      ulong tzPtr = ctx[CpuRegister.Rdi];
      if (tzPtr == 0) return KernelErrorEfault;
      // Populate tz_minuteswest (Bias) and tz_dsttime from TimeZoneInfo.Local
      ctx.Memory.WriteInt32(tzPtr, (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes * -1);
      ctx.Memory.WriteInt32(tzPtr + 4, TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now) ? 1 : 0);
      return 0;
  }
  ```

#### 3. Register `libkernel_unity` Exception Handlers
* **Target File:** [`UnityCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Unity/UnityCompatExports.cs)
* **Implementation:**
  * Register explicit export attributes for `WkwEd3N7w0Y` (`sceKernelInstallExceptionHandler`), `il03nluKfMk` (`sceKernelRaiseException`), and `Qhv5ARAoOEc` (`sceKernelRemoveExceptionHandler`) with `LibraryName = "libkernel_unity"`.

---

## Tier 3: Custom 3D Engines & Classic Remasters
**Target Games:** *Tomb Raider I-III Remastered*, *Tomb Raider IV-VI Remastered*, *The Thing: Remastered*, *Raiden III × MIKADO MANIAX*, *Raiden IV x MIKADO remix*, *Red Dead Redemption*.

### Actionable Deliverables:

#### 1. RDNA2 Surface Untiling & Format Conversions
* **Target File:** [`GnmTiling.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Agc/GnmTiling.cs)
* **Problem:** Textures, normal maps, and render targets are stored in memory using AMD RDNA2 micro/macro tiling. Without accurate untiling, textures render as static noise or corrupt color stripes.
* **Implementation:**
  * Complete the 2D/3D surface untiling pipeline ported from KytyPS5's [`tile.cpp:1-500`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/graphics/guest_gpu/tile.cpp#L1-L500) for standard texture formats (`BC1-BC7`, `R8G8B8A8_UNORM`, `R10G10B10A2_UNORM`, `R11G11B10_FLOAT`).

#### 2. Shader Resource Table (SRT) Pointer Evaluation
* **Target File:** [`Gen5ShaderTranslator.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler/Gen5ShaderTranslator.cs)
* **Problem:** 3D titles pass nested table pointers via User Data SGPRs (`SPI_SHADER_USER_DATA_VS_0..15`, `SPI_SHADER_USER_DATA_PS_0..15`).
* **Implementation:**
  * Port KytyPS5's [`SrtWalker.cpp`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/graphics/shader/recompiler/ir/SrtWalker.cpp) and [`descriptors.cpp`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/graphics/host_gpu/renderer/pipeline/descriptors.cpp) to walk guest memory addresses referenced in SGPRs and bind descriptor sets dynamically into the Vulkan backend.

---

## Tier 4: Unreal Engine 4 & 5 Suite
**Target Games:** *Crash Bandicoot™ 4: It’s About Time*, *Stray*, *PAC-MAN WORLD 2 Re-PAC*, *CYGNI: All Guns Blazing*.

### Actionable Deliverables:

#### 1. Win32 Placeholder Virtual Memory Management (`VirtualAlloc2`)
* **Target File:** [`PhysicalVirtualMemory.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Core/Memory/PhysicalVirtualMemory.cs)
* **Problem:** Unreal Engine's `FMallocBinned2` reserves a 4GB/16GB address space using `sceKernelReserveVirtualRange` (`7oxv3PPCumo`) and maps direct memory chunks dynamically via `sceKernelMapDirectMemory2` (`BQQniolj9tQ`). CraziiEmu's flat allocator fails because it cannot split reservations into sub-allocated page mappings.
* **Implementation:**
  * Mirror KytyPS5's [`memory.cpp:50-100`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/kernel/memory.cpp#L50-L100):
  ```csharp
  // 1. Reserve Virtual Range:
  VirtualAlloc2(processHandle, baseAddr, size, MEM_RESERVE | MEM_RESERVE_PLACEHOLDER, PAGE_NOACCESS, null, 0);
  
  // 2. Map Direct Memory Chunk into Range:
  VirtualFreeEx(processHandle, subChunkAddr, subChunkSize, MEM_PRESERVE_PLACEHOLDER); // Split
  MapViewOfFile3(sectionHandle, processHandle, subChunkAddr, offset, subChunkSize, MEM_REPLACE_PLACEHOLDER, PAGE_READWRITE, null, 0);
  ```

#### 2. Atomic `SyncOnAddress` Futex Queue
* **Target File:** [`KernelSyncOnAddressCompatExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelSyncOnAddressCompatExports.cs)
* **Problem:** Unreal Engine's `TaskGraph` system dispatches dozens of worker threads that synchronize using `sceKernelSyncOnAddressWait32` (`B2n8aDorSH4`) and `sceKernelSyncOnAddressWake` (`q2y-wDIVWZA`). Lost wakeups cause worker thread deadlocks.
* **Implementation:**
  * Implement an address-keyed futex queue using `ConcurrentDictionary<ulong, AutoResetEventPool>` to guarantee zero lost wakeups under high contention.

#### 3. Compute Shader LDS (Local Data Share) Atomics
* **Target File:** [`Gen5SpirvTranslator.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs)
* **Problem:** Unreal Engine's lighting, skinning, and particle systems utilize RDNA2 compute shaders with LDS memory instructions (`ds_read_b32`, `ds_write_b32`, `ds_add_u32`, `s_barrier`).
* **Implementation:**
  * Map RDNA2 LDS addresses to Vulkan SPIR-V `Workgroup` storage class variables with `OpAtomicIAdd`, `OpAtomicMin`, and `OpControlBarrier`.

#### 4. Asynchronous `sceAmpr` & `KernelAio` PAK Streaming
* **Target File:** [`AmprExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Ampr/AmprExports.cs) & [`KernelFileExtendedExports.cs`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelFileExtendedExports.cs)
* **Implementation:**
  * Implement non-blocking disk reads for `KernelAioSubmitReadCommands` (`HgX7+AORI58`) on a dedicated thread pool and signal the registered `EQueue` upon read completion so UE4 Zen Loader / PAK streaming proceeds seamlessly.

---

## Summary of Execution Order

```
[ Step 1 ] Tier 1 (Audio Events + SaveData)       ===> Boots ANIMAL WELL, Dead Cells, Final Vendetta
[ Step 2 ] Tier 2 (Red-Zone + Unity Time/GC)      ===> Boots DREDGE, Prince of Persia, ALEX KIDD
[ Step 3 ] Tier 3 (RDNA2 Untiling + SRTs)         ===> Boots Tomb Raider I-VI, The Thing, RDR
[ Step 4 ] Tier 4 (Placeholders + Futexes + LDS)  ===> Boots Crash 4, Stray, CYGNI
```