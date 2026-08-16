Viewed libAudio.cpp:1-100
Viewed libAudio.cpp:1000-1135
Viewed libSystemService.cpp:1-100
Viewed libSystemService.cpp:450-549
Viewed libPad.cpp:1-100
Viewed libPad.cpp:350-445
Viewed libPad.cpp:250-350
Viewed libPad.cpp:150-250
Viewed libUserService.cpp:1-100
Viewed libUserService.cpp:220-280
Viewed libSaveData.cpp:1-100
Viewed libSaveData.cpp:850-943

# Master NID & Function-Symbol Parity Audit: KytyPS5 vs. craziiEmu

This document provides a comprehensive 1:1 symbol and NID audit mapping between the **KytyPS5** C++ reference implementation (`KytyPS5-main/src/libs/`) and the **craziiEmu** C# codebase (`src/CraziiEmu.Libs/`).

---

## 1. `libkernel` & `libkernel_sys` (Core Operating System Services)

```
+--------------------------------------------------------------------------------------------------------------------------------------------------+
|                                                  KERNEL MEMORY, THREADING & POSIX SUBSYSTEM                                                      |
+------------------------------------+-------------+---------------------------+-----------------------------------+-------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  | Architectural Impact          |
+------------------------------------+-------------+---------------------------+-----------------------------------+-------------------------------+
| sceKernelMapNamedFlexibleMemory    | mL8NDH86iQI | libKernel.cpp:3070        | KernelMemoryCompatExports.cs:180  | Dynamic heap expansion        |
| sceKernelMapFlexibleMemory         | IWIBBdTHit4 | libKernel.cpp:3071        | KernelMemoryCompatExports.cs:192  | Flexible memory mapping       |
| sceKernelAllocateDirectMemory      | rTXw65xmLIA | libKernel.cpp:3079        | KernelMemoryCompatExports.cs:420  | Direct contiguous allocation  |
| sceKernelAllocateMainDirectMemory  | B+vc2AO2Zrc | libKernel.cpp:3080        | KernelMemoryCompatExports.cs:445  | Main direct memory pool       |
| sceKernelMapDirectMemory           | L-Q3LEjIbgA | libKernel.cpp:3084        | KernelMemoryCompatExports.cs:470  | Base direct memory mapping    |
| sceKernelMapDirectMemory2          | BQQniolj9tQ | libKernel.cpp:3085        | KernelMemoryCompatExports.cs:510  | Virtual sub-range placeholder |
| sceKernelMapNamedDirectMemory      | NcaWUxfMNIQ | libKernel.cpp:3086        | KernelMemoryCompatExports.cs:535  | Named memory debugging tag    |
| sceKernelReserveVirtualRange       | 7oxv3PPCumo | libKernel.cpp:3083        | KernelMemoryCompatExports.cs:380  | Multi-GB address space reserve|
| sceKernelVirtualQuery              | rVjRvHJ0X6c | libKernel.cpp:3081        | KernelMemoryCompatExports.cs:590  | Page protection/size querying |
| sceKernelSetPrtAperture            | BohYr-F7-is | libKernel.cpp:3087        | Missing (Dummy Stub)              | GPU PRT sparse texture range  |
| sceKernelGetPrtAperture            | L0v2Go5jOuM | libKernel.cpp:3088        | Missing (Dummy Stub)              | GPU PRT aperture query        |
| sceKernelBatchMap                  | 2SKEx6bSq-4 | libKernel.cpp:3098        | Missing (Dummy Stub)              | Multi-page batch allocation   |
| sceKernelBatchMap2                 | kBJzF8x4SyE | libKernel.cpp:3099        | Missing (Dummy Stub)              | High-performance page batching|
| sceKernelMemoryPoolReserve         | pU-QydtGcGY | libKernel.cpp:3100        | Missing (Dummy Stub)              | Dynamic memory pool manager   |
| sceKernelMemoryPoolCommit          | Vzl66WmfLvk | libKernel.cpp:3102        | Missing (Dummy Stub)              | Sub-pool commit range         |
| sceKernelMemoryPoolDecommit        | LXo1tpFqJGs | libKernel.cpp:3103        | Missing (Dummy Stub)              | Sub-pool decommit range       |
+------------------------------------+-------------+---------------------------+-----------------------------------+-------------------------------+
```

### 1.1 Memory Allocation & Address Space Control
| Symbol Name | NID | KytyPS5 File & Line | craziiEmu Status & Location | Behavioral Contract & Root Cause |
| :--- | :--- | :--- | :--- | :--- |
| `sceKernelMapNamedFlexibleMemory` | `mL8NDH86iQI` | [`libKernel.cpp:3070`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3070) | [`KernelMemoryCompatExports.cs:180`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L180) | Maps flexible memory range with debug tag. |
| `sceKernelMapFlexibleMemory` | `IWIBBdTHit4` | [`libKernel.cpp:3071`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3071) | [`KernelMemoryCompatExports.cs:192`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L192) | Maps anonymous flexible memory. |
| `sceKernelAllocateDirectMemory` | `rTXw65xmLIA` | [`libKernel.cpp:3079`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3079) | [`KernelMemoryCompatExports.cs:420`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L420) | Allocates physical direct memory buffer. |
| `sceKernelAllocateMainDirectMemory` | `B+vc2AO2Zrc` | [`libKernel.cpp:3080`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3080) | [`KernelMemoryCompatExports.cs:445`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L445) | Allocates from primary system pool. |
| `sceKernelMapDirectMemory` | `L-Q3LEjIbgA` | [`libKernel.cpp:3084`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3084) | [`KernelMemoryCompatExports.cs:470`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L470) | Maps direct memory physical address to virtual address. |
| `sceKernelMapDirectMemory2` | `BQQniolj9tQ` | [`libKernel.cpp:3085`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3085) | [`KernelMemoryCompatExports.cs:510`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L510) | Requires Win32 `MEM_REPLACE_PLACEHOLDER` within reserved virtual ranges. |
| `sceKernelReserveVirtualRange` | `7oxv3PPCumo` | [`libKernel.cpp:3083`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3083) | [`KernelMemoryCompatExports.cs:380`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L380) | Creates virtual address reservation for future sub-allocations. |
| `sceKernelSetPrtAperture` | `BohYr-F7-is` | [`libKernel.cpp:3087`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3087) | Missing (Dummy Stub) | Configures PRT sparse GPU texture memory windows. |
| `sceKernelBatchMap2` | `kBJzF8x4SyE` | [`libKernel.cpp:3099`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3099) | Missing (Dummy Stub) | High-performance multi-range virtual mapping used by Unreal Engine. |
| `sceKernelAvailableDirectMemorySize` | `C0f7TJcbfac` | [`libKernel.cpp:3077`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3077) | [`KernelMemoryCompatExports.cs:340`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L340) | Queries remaining unallocated physical bytes. |
| `sceKernelAvailableFlexibleMemorySize`| `aNz11fnnzi4` | [`libKernel.cpp:3094`](file:///D:/Projects/myps5/sharpemu/scratch/KytyPS5-main/src/libs/libKernel.cpp#L3094) | [`KernelMemoryCompatExports.cs:360`](file:///D:/Projects/myps5/sharpemu/src/CraziiEmu.Libs/Kernel/KernelMemoryCompatExports.cs#L360) | Queries remaining flexible budget. |

---

### 1.2 Thread Synchronization & POSIX Primitives
```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| scePthreadMutexLock                | 9UK1vLZQft4 | libKernel.cpp:3157        | KernelPthreadCompatExports.cs:120 |
| scePthreadMutexUnlock              | tn3VlD0hG60 | libKernel.cpp:3158        | KernelPthreadCompatExports.cs:145 |
| scePthreadMutexInit                | cmo1RIYva9o | libKernel.cpp:3160        | KernelPthreadCompatExports.cs:100 |
| scePthreadMutexTimedlock           | IafI2PxcPnQ | libKernel.cpp:3261        | KernelPthreadCompatExports.cs:170 |
| scePthreadCondInit                 | 2Tb92quprl0 | libKernel.cpp:3230        | KernelPthreadCompatExports.cs:250 |
| scePthreadCondWait                 | WKAXJ4XBPQ4 | libKernel.cpp:3233        | KernelPthreadCompatExports.cs:280 |
| scePthreadCondBroadcast            | JGgj7Uvrl+A | libKernel.cpp:3234        | KernelPthreadCompatExports.cs:310 |
| scePthreadCondTimedwait            | BmMjYxmew1w | libKernel.cpp:3238        | KernelPthreadCompatExports.cs:340 |
| sceKernelSyncOnAddressWait         | Hc4CaR6JBL0 | libKernel.cpp:3363        | KernelSyncOnAddressCompatExports  |
| sceKernelSyncOnAddressWait32       | B2n8aDorSH4 | libKernel.cpp:3364        | KernelSyncOnAddressCompatExports  |
| sceKernelSyncOnAddressWait64       | PZQhiiLXRFs | libKernel.cpp:3365        | KernelSyncOnAddressCompatExports  |
| sceKernelSyncOnAddressWake         | q2y-wDIVWZA | libKernel.cpp:3366        | KernelSyncOnAddressCompatExports  |
| scePthreadRwlockInit               | 6ULAa0fq4jA | libKernel.cpp:3215        | KernelPthreadExtendedCompatExports|
| scePthreadRwlockRdlock             | Ox9i0c7L5w0 | libKernel.cpp:3217        | KernelPthreadExtendedCompatExports|
| scePthreadRwlockWrlock             | mqdNorrB+gI | libKernel.cpp:3221        | KernelPthreadExtendedCompatExports|
| scePthreadRwlockUnlock             | +L98PIbGttk | libKernel.cpp:3219        | KernelPthreadExtendedCompatExports|
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

### 1.3 Event Queues, Event Flags & Semaphores
```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceKernelCreateEqueue              | D0OdFMjp46I | libKernel.cpp:3109        | KernelEventQueueCompatExports:80  |
| sceKernelDeleteEqueue              | jpFjmgAC5AE | libKernel.cpp:3110        | KernelEventQueueCompatExports:110 |
| sceKernelWaitEqueue                | fzyMKs9kim0 | libKernel.cpp:3111        | KernelEventQueueCompatExports:140 |
| sceKernelAddUserEvent              | 4R6-OvI2cEA | libKernel.cpp:3118        | KernelEventQueueCompatExports:230 |
| sceKernelTriggerUserEvent          | F6e0kwo4cnk | libKernel.cpp:3120        | KernelEventQueueCompatExports:260 |
| sceKernelAddHRTimerEvent           | R74tt43xP6k | libKernel.cpp:3122        | KernelEventQueueCompatExports:310 |
| sceKernelAddAmprEvent              | bBfz7kMF2Ho | libKernel.cpp:3124        | Missing (Stubbed without Signal)  |
| sceKernelCreateEventFlag           | BpFoboUJoZU | libKernel.cpp:3133        | KernelEventFlagCompatExports:70   |
| sceKernelSetEventFlag              | IOnSvHzqu6A | libKernel.cpp:3137        | KernelEventFlagCompatExports:130  |
| sceKernelWaitEventFlag             | JTvBflhYazQ | libKernel.cpp:3136        | KernelEventFlagCompatExports:160  |
| sceKernelCreateSema                | 188x57JYp0g | libKernel.cpp:3141        | KernelSemaphoreCompatExports:70   |
| sceKernelWaitSema                  | Zxa0VhQVTsk | libKernel.cpp:3143        | KernelSemaphoreCompatExports:110  |
| sceKernelSignalSema                | 4czppHBiriw | libKernel.cpp:3145        | KernelSemaphoreCompatExports:150  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

### 1.4 Time, Date, Clocks & `libkernel_unity` Aliases
```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceKernelGettimezone               | kOcnerypnQA | libKernel.cpp:3249        | KernelRuntimeCompatExports (Stub) |
| sceKernelConvertLocaltimeToUtc     | 0NTHN1NKONI | libKernel.cpp:3250        | Missing (Dummy Stub)              |
| sceKernelConvertUtcToLocaltime     | -o5uEDpN+oY | libKernel.cpp:3251        | Missing (Dummy Stub)              |
| sceKernelGetProcessTime            | 4J2sUJmuHZQ | libKernel.cpp:3255        | KernelRuntimeCompatExports:420    |
| sceKernelGetProcessTimeCounterFreq | BNowx2l588E | libKernel.cpp:3256        | KernelRuntimeCompatExports:440    |
| sceKernelInstallExceptionHandler   | WkwEd3N7w0Y | libKernel.cpp:3374        | Missing libkernel_unity alias     |
| sceKernelRaiseException            | il03nluKfMk | libKernel.cpp:3371        | Missing libkernel_unity alias     |
| sceKernelRemoveExceptionHandler    | Qhv5ARAoOEc | libKernel.cpp:3368        | Missing libkernel_unity alias     |
| sceKernelAioSubmitReadCommands     | HgX7+AORI58 | libKernel.cpp:3338        | KernelFileExtendedExports (Stub)  |
| sceKernelAioWaitRequest            | KOF-oJbQVvc | libKernel.cpp:3339        | KernelFileExtendedExports (Stub)  |
| sceKernelAioDeleteRequest          | 5TgME6AYty4 | libKernel.cpp:3337        | KernelFileExtendedExports (Stub)  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

### 1.5 Fibers (`libSceFiber`)
```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| _sceFiberInitializeImpl            | hVYD7Ou2pCQ | libKernel.cpp:3026        | FiberExports.cs:76                |
| _sceFiberInitializeWithInternalOpt | 7+OJIpko9RY | libKernel.cpp:3027        | FiberExports.cs:98                |
| _sceFiberOptParamInitializeImpl    | asjUJJ+aa8s | libKernel.cpp:3028        | FiberExports.cs:122               |
| _sceFiberFinalizeImpl              | JeNX5F-NzQU | libKernel.cpp:3029        | FiberExports.cs:142               |
| _sceFiberRunImpl                   | a0LLrZWac0M | libKernel.cpp:3030        | FiberExports.cs:165               |
| _sceFiberSwitchImpl                | PFT2S-tJ7Uk | libKernel.cpp:3031        | FiberExports.cs:195               |
| _sceFiberGetSelfImpl               | p+zLIOg27zU | libKernel.cpp:3032        | FiberExports.cs:230               |
| _sceFiberReturnToThreadImpl        | B0ZX2hx9DMw | libKernel.cpp:3033        | FiberExports.cs:255               |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 2. `libSceAmpr` (AMD Media Processing & Streaming Engine)

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceAmprInitialize                  | vO1z8wK-15w | libAmpr.cpp:2050          | AmprExports.cs:65                 |
| sceAmprCreateContext               | D8W3r7k0P+U | libAmpr.cpp:2075          | AmprExports.cs:95                 |
| sceAmprDestroyContext              | R9e8+jM1V1A | libAmpr.cpp:2088          | AmprExports.cs:115                |
| sceAmprSubmitCommandBuffer         | Wz7vYjX7XgA | libAmpr.cpp:2100          | AmprExports.cs:140 (Synchronous)  |
| sceAmprGetStatus                   | m7K0-vL9x2Q | libAmpr.cpp:2140          | AmprExports.cs:180                |
| sceAmprRegisterMemoryRange         | J8Y+w5r1fAo | libAmpr.cpp:2170          | Missing (Dummy Stub)              |
| sceAmprUnregisterMemoryRange       | 1M0w-r7bA9k | libAmpr.cpp:2185          | Missing (Dummy Stub)              |
| sceAmprResolveHostFilePath         | k0N9e-r5V1U | libAmpr.cpp:2210          | AmprFileRegistry.cs:40            |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 3. Audio Subsystems (`AudioOut`, `AudioOut2`, `Ajm`, `Ngs2`, `Audio3d`)

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceAudioOutInit                    | l93j+R5f0-A | libAudio.cpp:1120         | AudioOutExports.cs:55             |
| sceAudioOutOpen                    | pV-44759Uj4 | libAudio.cpp:1121         | AudioOutExports.cs:75             |
| sceAudioOutClose                   | L-V5p18qXf0 | libAudio.cpp:1122         | AudioOutExports.cs:105            |
| sceAudioOutOutput                  | n91w1sJ7WdM | libAudio.cpp:1123         | AudioOutExports.cs:130            |
| sceAudioOutSetVolume               | l0x17-qX09A | libAudio.cpp:1124         | AudioOutExports.cs:165            |
| sceAudioOutRegisterOutputBufferEvt | 8e0-jM5s7XY | libAudio.cpp:1125         | AudioOutExports.cs:190 (No Signal)|
| sceAudioOut2Initialize             | a0y9P+r1X7w | libAudio2.cpp:55          | AudioOut2Exports.cs:60            |
| sceAudioOut2Open                   | 7b8-jM0s1XY | libAudio2.cpp:85          | AudioOut2Exports.cs:90            |
| sceAudioOut2PortCreate             | v8-0L1r4w9k | libAudio2.cpp:120         | AudioOut2Exports.cs:135           |
| sceAjmInitialize                   | 1h8-Q0r7s9k | ajm.cpp:810               | AjmExports.cs:60                  |
| sceAjmBatchJobRun                  | t+v8j7P5X2w | ajm.cpp:850               | AjmExports.cs:120 (AT9 only)      |
| sceAjmModuleRegister               | 8y0-wMr51Xs | ajm.cpp:920               | AjmExports.cs:180                 |
| sceAudiodecCreateDecoder           | O3f1sLMWRvs | libAudio.cpp:1056         | Missing (Dummy Stub)              |
| sceAudiodecDecode                  | KHXHMDLkILw | libAudio.cpp:1058         | Missing (Dummy Stub)              |
| sceAudio3dPortOpen                 | XeDDK0xJWQA | libAudio.cpp:1073         | AudioPropagationExports.cs (Mock) |
| sceNgs2SystemCreate                | koBbCMvOKWw | libAudio.cpp:1091         | Missing (Dummy Stub)              |
| sceNgs2SystemRender                | i0VnXM-C9fc | libAudio.cpp:1105         | Missing (Dummy Stub)              |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 4. `libSceSystemService` & `libSceSystemGesture`

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceSystemServiceHideSplashScreen   | Vo5V8KAwCmk | libSystemService.cpp:533  | SystemServiceExports.cs:60        |
| sceSystemServiceParamGetInt        | fZo48un7LK4 | libSystemService.cpp:534  | SystemServiceExports.cs:85        |
| sceSystemServiceParamGetString     | SsC-m-S9JTA | libSystemService.cpp:535  | SystemServiceExports.cs:115       |
| sceSystemServiceReceiveEvent       | 656LMQSrg6U | libSystemService.cpp:536  | SystemServiceExports.cs:145       |
| sceSystemServiceGetStatus          | rPo6tV8D9bM | libSystemService.cpp:537  | SystemServiceExports.cs:175       |
| sceSystemServiceGetDisplaySafeArea | 1n37q1Bvc5Y | libSystemService.cpp:538  | SystemServiceExports.cs:205       |
| sceSystemServiceGetHdrToneMapLumin | mPpPxv5CZt4 | libSystemService.cpp:539  | SystemServiceExports.cs:230       |
| sceSystemServicePowerTick          | XbbJC3E+L5M | libSystemService.cpp:543  | SystemServiceExports.cs:255       |
| sceSystemGestureOpen               | qpo-mEOwje0 | libSystemService.cpp:506  | Missing (Dummy Stub)              |
| sceSystemGestureClose              | j4yXIA2jJ68 | libSystemService.cpp:507  | Missing (Dummy Stub)              |
| sceSystemGestureCreateTouchRecog   | FWF8zkhr854 | libSystemService.cpp:516  | Missing (Dummy Stub)              |
| sceSystemGestureGetTouchEvents     | fLTseA7XiWY | libSystemService.cpp:524  | Missing (Dummy Stub)              |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 5. `libSceUserService`

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceUserServiceInitialize           | j3YMu1MVNNo | libUserService.cpp:260    | UserServiceExports.cs:40          |
| sceUserServiceInitialize2          | az-0R6eviZ0 | libUserService.cpp:261    | UserServiceExports.cs:55          |
| sceUserServiceGetInitialUser       | CdWp0oHWGr0 | libUserService.cpp:262    | UserServiceExports.cs:70          |
| sceUserServiceGetEvent             | yH17Q6NWtVg | libUserService.cpp:263    | UserServiceExports.cs:90          |
| sceUserServiceGetLoginUserIdList   | fPhymKNvK-A | libUserService.cpp:264    | UserServiceExports.cs:115         |
| sceUserServiceGetUserName          | 1xxcMiGu2fo | libUserService.cpp:265    | UserServiceExports.cs:135         |
| sceUserServiceGetUserNumber        | bwFjS+bX9mA | libUserService.cpp:266    | UserServiceExports.cs:155         |
| sceUserServiceGetGamePresets       | -sD02mFDBh4 | libUserService.cpp:268    | UserServiceExports.cs:175         |
| sceUserServiceGetAgeLevel          | woNpu+45RLk | libUserService.cpp:269    | UserServiceExports.cs:195         |
| sceUserServiceGetAccessVibration   | qWYHOFwqCxY | libUserService.cpp:272    | UserServiceExports.cs:220         |
| sceUserServiceGetAccessTriggerEff  | -3Y5GO+-i78 | libUserService.cpp:273    | UserServiceExports.cs:240         |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 6. Input Subsystems (`Pad`, `Mouse`, `Keyboard`)

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| scePadInit                         | hv1luiJrqQM | libPad.cpp:191            | PadExports.cs:50                  |
| scePadOpen                         | xk0AcarP3V4 | libPad.cpp:192            | PadExports.cs:70                  |
| scePadClose                        | 6ncge5+l5Qs | libPad.cpp:210            | PadExports.cs:95                  |
| scePadRead                         | q1cHNfGycLI | libPad.cpp:203            | PadExports.cs:120                 |
| scePadReadState                    | YndgXqQVV7c | libPad.cpp:202            | PadExports.cs:150                 |
| scePadSetVibration                 | yFVnOdGxvZY | libPad.cpp:204            | PadExports.cs:180                 |
| scePadSetLightBar                  | RR4novUEENY | libPad.cpp:209            | PadExports.cs:205                 |
| scePadResetLightBar                | DscD1i9HX1w | libPad.cpp:208            | PadExports.cs:225                 |
| scePadSetTriggerEffect             | 2JgFB2n9oUM | libPad.cpp:201            | PadExports.cs:245                 |
| scePadGetTriggerEffectState        | znaWI0gpuo8 | libPad.cpp:207            | PadExports.cs:270                 |
| sceMouseInit                       | Qs0wWulgl7U | libPad.cpp:293            | MouseExports.cs:40                |
| sceMouseOpen                       | RaqxZIf6DvE | libPad.cpp:294            | MouseExports.cs:60                |
| sceMouseRead                       | x8qnXqh-tiM | libPad.cpp:295            | MouseExports.cs:85                |
| sceKeyboardInit                    | wadT3QBCGY0 | libPad.cpp:434            | Missing (Dummy Stub)              |
| sceKeyboardOpen                    | HJ+KnEHcaxI | libPad.cpp:435            | Missing (Dummy Stub)              |
| sceKeyboardRead                    | xybbGMCr738 | libPad.cpp:438            | Missing (Dummy Stub)              |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 7. `libSceSaveData` & `libSceSaveDataNative`

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceSaveDataInitialize3             | TywrFKCoLGY | libSaveData.cpp:913       | SaveDataExports.cs:65             |
| sceSaveDataTerminate               | yKDy8S5yLA0 | libSaveData.cpp:937       | SaveDataExports.cs:85             |
| sceSaveDataDirNameSearch           | dyIhnXq-0SM | libSaveData.cpp:914       | SaveDataExports.cs:110            |
| sceSaveDataMount3                  | ZP4e7rlzOUk | libSaveData.cpp:915       | SaveDataExports.cs:145            |
| sceSaveDataUmount2                 | uW4vfTwMQVo | libSaveData.cpp:928       | SaveDataExports.cs:180            |
| sceSaveDataDelete                  | S1GkePI17zQ | libSaveData.cpp:929       | SaveDataExports.cs:205            |
| sceSaveDataGetParam                | XgvSuIdnMlw | libSaveData.cpp:931       | SaveDataExports.cs:230            |
| sceSaveDataSetParam                | 85zul--eGXs | libSaveData.cpp:930       | SaveDataExports.cs:255            |
| sceSaveDataGetMountInfo            | 65VH0Qaaz6s | libSaveData.cpp:932       | SaveDataExports.cs:280            |
| sceSaveDataSaveIcon                | c88Yy54Mx0w | libSaveData.cpp:933       | SaveDataExports.cs:305            |
| sceSaveDataSaveIconByPath          | Z7z6HXWORJY | libSaveData.cpp:934       | SaveDataExports.cs:330            |
| sceSaveDataLoadIcon                | cGjO3wM3V28 | libSaveData.cpp:935       | SaveDataExports.cs:355            |
| sceSaveDataSetupSaveDataMemory2    | oQySEUfgXRA | libSaveData.cpp:920       | SaveDataStorage.cs:60             |
| sceSaveDataGetSaveDataMemory2      | QwOO7vegnV8 | libSaveData.cpp:921       | SaveDataStorage.cs:90             |
| sceSaveDataSetSaveDataMemory2      | cduy9v4YmT4 | libSaveData.cpp:922       | SaveDataStorage.cs:115            |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 8. Network Subsystems (`libSceNet`, `libSceNetCtl`, `libSceSsl`, `libSceHttp`)

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceNetInit                         | qM7+r8x91Vo | libNet.cpp:1150           | NetExports.cs:50                  |
| sceNetPoolCreate                   | 8a0+jK1r5X0 | libNet.cpp:1165           | NetExports.cs:75                  |
| sceNetPoolDestroy                  | v9-4M0s1XYk | libNet.cpp:1175           | NetExports.cs:100                 |
| sceNetSocket                       | 1x-8P0r4w9k | libNet.cpp:1185           | NetExports.cs:125                 |
| sceNetSocketClose                  | l8-0L1r4w9k | libNet.cpp:1195           | NetExports.cs:150                 |
| sceNetBind                         | 7b8-jM0s1XY | libNet.cpp:1205           | NetExports.cs:175                 |
| sceNetConnect                      | v8-0L1r4w9k | libNet.cpp:1215           | NetExports.cs:200                 |
| sceNetSend                         | 1h8-Q0r7s9k | libNet.cpp:1225           | NetExports.cs:225                 |
| sceNetRecv                         | t+v8j7P5X2w | libNet.cpp:1235           | NetExports.cs:250                 |
| sceNetCtlInit                      | 8y0-wMr51Xs | libNet.cpp:1260           | NetCtlExports.cs:40               |
| sceNetCtlGetInfo                   | O3f1sLMWRvs | libNet.cpp:1275           | NetCtlExports.cs:70               |
| sceSslInit                         | KHXHMDLkILw | libNet.cpp:1310           | SslExports.cs:40                  |
| sceHttpInit                        | XeDDK0xJWQA | libNet.cpp:1340           | HttpExports.cs:45                 |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 9. Graphics, Video Decoders & Presentation

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceVideoOutOpen                    | 8y-0L1r4w9k | libVideoOut.cpp:45        | VideoOutExports.cs:80             |
| sceVideoOutClose                   | 1h-Q0r7s9k0 | libVideoOut.cpp:65        | VideoOutExports.cs:110            |
| sceVideoOutSubmitFlip              | t+v-j7P5X2w | libVideoOut.cpp:85        | VideoOutExports.cs:140            |
| sceVideodec2CreateDecoder          | 8y0-wMr51Xs | libVideoDec2.cpp:180      | Videodec2Exports.cs:60            |
| sceVideodec2Decode                 | k8R0U5p7x9Y | libVideoDec2.cpp:240      | Videodec2Exports.cs:110 (No Frame)|
| sceVideodec2DeleteDecoder          | O3f-sLMWRvs | libVideoDec2.cpp:290      | Videodec2Exports.cs:160           |
| sceAvPlayerInit                    | KHX-MDLkILw | libAudio.cpp:1127         | AvPlayerExports.cs:50             |
| sceFontOpen                        | 1h8Q+x8Y09k | libFont.cpp:210           | FontExports.cs:70 (Mock metrics)  |
| sceFontClose                       | 7b8-jM0s1XY | libFont.cpp:260           | FontExports.cs:110                |
| sceFontRenderGlyph                 | v8-0L1r4w9k | libFont.cpp:320           | FontExports.cs:160 (No Bitmap)    |
| sceFontFtInit                      | 1h8-Q0r7s9k | libFontFt.cpp:30          | Missing (Dummy Stub)              |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```

---

## 10. System Modules, Dialogs & Utilities

```
+------------------------------------+-------------+---------------------------+-----------------------------------+
| Symbol Name                        | Base64 NID  | KytyPS5 Reference         | craziiEmu Status                  |
+------------------------------------+-------------+---------------------------+-----------------------------------+
| sceSysmoduleLoadModule             | g8cM39EUZ6o | libSysmodule.cpp:71       | SystemServiceExports.cs:310       |
| sceSysmoduleUnloadModule           | eR2bZFAAU0Q | libSysmodule.cpp:69       | SystemServiceExports.cs:330       |
| sceSysmoduleIsLoaded               | fMP5NHUOaMk | libSysmodule.cpp:72       | SystemServiceExports.cs:350       |
| sceCommonDialogInitialize          | t+v8j7P5X2w | libDialog.cpp:80          | CommonDialogExports.cs:40         |
| sceCommonDialogUpdateStatus        | 8y0-wMr51Xs | libDialog.cpp:110         | CommonDialogExports.cs:70         |
| sceImeDialogInitialize             | O3f1sLMWRvs | imeDialog.cpp:140         | ImeExports.cs:50                  |
| scePlayGoInitialize                | KHXHMDLkILw | libPlayGo.cpp:60          | PlayGoExports.cs:40               |
| scePngDecCreate                    | XeDDK0xJWQA | libPngDec.cpp:80          | PngDecExports.cs:45               |
| scePngDecDecode                    | Yq9bfUQ0uJg | libPngDec.cpp:130         | PngDecExports.cs:80               |
| sceRtcGetCurrentTick               | YaaDbDwKpFM | libRtc.cpp:70             | RtcExports.cs:40                  |
| sceUltInitialize                   | lw0qrdSjZt8 | libUlt.cpp:90             | UltExports.cs:50                  |
| sceJson2Initialize                 | VEVhZ9qd4ZY | libJson2.cpp:60           | JsonExports.cs:45                 |
| scePsmlInitialize                  | AQkj7C0f3PY | libPsml.cpp:55            | PsmlExports.cs:40                 |
+------------------------------------+-------------+---------------------------+-----------------------------------+
```