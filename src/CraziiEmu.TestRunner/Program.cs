// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.IO;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Runtime;

namespace CraziiEmu.TestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var options = new CraziiEmuRuntimeOptions { CpuEngine = CpuExecutionEngine.Interpreter };
            var runtime = CraziiEmuRuntime.CreateDefault(options);
            
            Console.WriteLine("Loading ELF...");
            var image = runtime.LoadImage(@"C:\Users\crazy\Downloads\crispy-doom-ps5-v1.0-7.1.0\CrispyDoom\payloads\crispy-doom.elf");
            ulong entryPoint = image.EntryPoint;
            
            Console.WriteLine($"Running ELF at entry point 0x{entryPoint:X}...");
            try
            {
                runtime.Run(@"C:\Users\crazy\Downloads\crispy-doom-ps5-v1.0-7.1.0\CrispyDoom\payloads\crispy-doom.elf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
    }
}
