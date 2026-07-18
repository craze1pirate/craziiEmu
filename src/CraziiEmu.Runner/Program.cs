// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Threading;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Gpu;
using CraziiEmu.Core.Loader; // Resolves DynamicLinker namespace

namespace CraziiEmu.Runner
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("Starting CraziiEmu Standalone Runner");
            Console.WriteLine("---------------------------------------------------------");

            // Initialize virtual memory manager (64GB)
            using var vmm = new VirtualMemoryManager(64UL * 1024 * 1024 * 1024);
            var router = new SyscallRouter(vmm);
            var linker = new DynamicLinker(vmm, name => Array.Empty<byte>());
            
            var moduleManager = new CraziiEmu.HLE.ModuleManager();
            moduleManager.RegisterExports(CraziiEmu.Generated.SysAbiExportRegistry.CreateExports(CraziiEmu.HLE.Generation.Gen4 | CraziiEmu.HLE.Generation.Gen5));
            moduleManager.Freeze();

            var engine = new EmulatorEngine(vmm, router)
            {
                ModuleManager = moduleManager,
                DynamicLinker = linker
            };
            var display = new DisplayController(vmm);

            // Hook up stdout console writes

            ulong entryPoint = 0x401790;

            if (args.Length == 0)
            {
                Console.WriteLine("[Demo Mode] No ELF provided. Injecting blue-screen homebrew payload...");

                // 2. Fill the entire 3.68MB virtual vRAM with solid, OPAQUE Cyan/Blue pixels (Alpha = 255)
                Span<byte> vramSpan = vmm.GetSpan(0x200000000, 1280 * 720 * 4);
                for (int i = 0; i < vramSpan.Length; i += 4)
                {
                    vramSpan[i] = 0xFF;     // Blue channel
                    vramSpan[i + 1] = 0xFF; // Green channel
                    vramSpan[i + 2] = 0x00; // Red channel
                    vramSpan[i + 3] = 0xFF; // Alpha channel (Opaque!)
                }

                // 3. The standard 26-byte machine-code sequence our CPU executes
                byte[] payload = new byte[]
                {
                    0x48, 0xC7, 0xC7, 0x00, 0x00, 0x00, 0x00, // mov rdi, 0 (translated to vRAM start)
                    0x48, 0xC7, 0xC0, 0xFF, 0x00, 0x00, 0x00, // mov rax, 0x0000FFFF (Cyan/Blue color)
                    0x48, 0x89, 0x07,                         // mov [rdi], rax (write color to vRAM)
                    0x48, 0xC7, 0xC0, 0x3C, 0x00, 0x00, 0x00, // mov rax, 60 (sys_exit)
                    0x48, 0xC7, 0xC7, 0x00, 0x00, 0x00, 0x00, // mov rdi, 0 (exit code 0)
                    0x0F, 0x05                                // syscall
                };

                // Allocate memory from the VMM first to register the pages in the page table
                entryPoint = vmm.AllocateCpu((ulong)payload.Length);

                // Copy the payload directly to the newly allocated virtual memory
                payload.CopyTo(vmm.GetSpan(entryPoint, payload.Length));
            }
            else
            {
                string elfPath = args[0];
                Console.WriteLine($"Loading target executable: {elfPath}");
                
                try
                {
                    byte[] elfBytes = System.IO.File.ReadAllBytes(elfPath);
                    
                    bool isSelf = elfBytes.Length >= 4 && (
                        // Sony standard SCE\0 magic (Vita/PS3)
                        (elfBytes[0] == 0x53 && elfBytes[1] == 0x43 && elfBytes[2] == 0x45 && elfBytes[3] == 0x00) ||
                        // Sony Orbis/Prospero SELF magic (PS4/PS5)
                        (elfBytes[0] == 0x4F && elfBytes[1] == 0x15 && elfBytes[2] == 0x3D && elfBytes[3] == 0x1D)
                    );

                    if (isSelf)
                    {
                        var selfLoader = new SelfLoader();
                        var image = selfLoader.Load(elfBytes, new VmmAdapter(vmm), moduleManager);
                        engine.SelfImportStubs = image.ImportStubs;
                        entryPoint = image.EntryPoint;

                        if (image.RuntimeSymbols.TryGetValue("module_start", out ulong moduleStart))
                        {
                            Console.WriteLine($"[Runner] Found module_start at 0x{moduleStart:X16}, overriding entry point.");
                            entryPoint = moduleStart;
                        }
                        else if (image.RuntimeSymbols.TryGetValue("_start", out ulong startSym))
                        {
                            Console.WriteLine($"[Runner] Found _start at 0x{startSym:X16}, overriding entry point.");
                            entryPoint = startSym;
                        }
                        
                        if (image.ProcParamAddress != 0)
                        {
                            try
                            {
                                var span = vmm.GetSpan(image.ProcParamAddress, 64);
                                ulong realEntry = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(0x18, 8));
                                Console.WriteLine($"[Runner] ProcParam EntryPoint (0x18) = 0x{realEntry:X16}");
                            }
                            catch { }
                        }
                        if (true)
                        {
                            try
                            {
                                ulong imageBase = image.EntryPoint - image.ElfHeader.EntryPoint;
                                var span = vmm.GetSpan(image.ElfHeader.EntryPoint + imageBase - 0x70, 64);
                                Console.WriteLine("[Runner] ELF Header bytes:");
                                for (int i = 0; i < 64; i++) Console.Write($"{span[i]:X2} ");
                                Console.WriteLine();
                            }
                            catch { }
                        }
                    }

                    else
                    {
                        var module = linker.LoadModule(elfPath, elfBytes);
                        entryPoint = module.BaseAddress + module.Image.Header.EntryPoint;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load ELF: {ex.Message}");
                    return;
                }
            }

            // Run the CPU interpreter loop on a background thread
            var cpuThread = new Thread(() =>
            {
                try
                {
                    ulong stackSize = 1024 * 1024;
                    ulong stackBase = vmm.AllocateCpu(stackSize);
                    engine.Context[CraziiEmu.HLE.CpuRegister.Rsp] = stackBase + stackSize - 8;

                    // Initialize FS/GS for TLS (TCB)
                    ulong tlsBaseAlloc = vmm.AllocateCpu(8192);
                    ulong tlsBase = tlsBaseAlloc + 4096;
                    engine.Context.FsBase = tlsBase;
                    engine.Context.GsBase = tlsBase;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(vmm.GetSpan(tlsBase, 8), tlsBase);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(vmm.GetSpan(tlsBase + 0x10, 8), tlsBase);

                    // Inject PS5 Payload SDK args
                    ulong payloadArgsBase = vmm.AllocateCpu(4096);
                    ulong stubBase = vmm.AllocateCpu(4096);
                    
                    var stubSpan = vmm.GetSpan(stubBase, 8);
                    byte[] dlsymGadget = new byte[] {
                        0xB8, 0x4F, 0x02, 0x00, 0x00, // mov eax, 591
                        0x0F, 0x05,                   // syscall
                        0xC3                          // ret
                    };
                    dlsymGadget.CopyTo(stubSpan);

                    var argsSpan = vmm.GetSpan(payloadArgsBase, 64);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(0, 8), stubBase);

                    router.RegisterDlsymStub("sceKernelDlsym", stubBase);

                    ulong rwpipePtr = vmm.AllocateCpu(8);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(vmm.GetSpan(rwpipePtr, 4), 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(vmm.GetSpan(rwpipePtr + 4, 4), 1);
                    
                    ulong rwpairPtr = vmm.AllocateCpu(8);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(vmm.GetSpan(rwpairPtr, 4), 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(vmm.GetSpan(rwpairPtr + 4, 4), 1);
                    
                    ulong payloadoutPtr = vmm.AllocateCpu(8);

                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(8, 8), rwpipePtr);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(16, 8), rwpairPtr);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(24, 8), 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(32, 8), 0);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(40, 8), payloadoutPtr);

                    ulong framebufferPtr = vmm.AllocateGpu(0);
                    ulong inputStatePtr = vmm.AllocateCpu(8); // empty inputs for CLI
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(48, 8), framebufferPtr);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(argsSpan.Slice(56, 8), inputStatePtr);

                    engine.Context[CraziiEmu.HLE.CpuRegister.Rdi] = payloadArgsBase;

                    engine.Run(entryPoint);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nEmulation error: {ex.Message}");
                }
            });
            cpuThread.Start();

            // Run the window and display blitter on the main thread.
            // InitializeWindow() blocks via _window.Run() until the window is closed.
            display.InitializeWindow();

            // Window has closed — join the CPU thread before exiting
            cpuThread.Join();

            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine("CraziiEmu Execution Finished.");
            Console.WriteLine("---------------------------------------------------------");
        }
    }
}
