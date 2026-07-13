using System;
using System.IO;
using System.Threading;
using CraziiEmu.Core.Cpu;
using CraziiEmu.Core.Gpu;
using CraziiEmu.Core.HLE;
using CraziiEmu.Core.Loader;
using CraziiEmu.Core.Memory;

namespace CraziiEmu.Runner;

public static class Program2
{
    public static void Main(string[] args)
    {
        try {
            string targetFilePath = @"d:\Projects\myps5\CraziiEmu\tests\fixtures\test_hello_static.elf";
            string targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetFilePath)) ?? string.Empty;

            using var vmm = new VirtualMemoryManager(4UL * 1024 * 1024 * 1024);
            var syscallRouter = new SyscallRouter(vmm);
            syscallRouter.OnStdoutWrite += Console.Write;

            var linker = new DynamicLinker(vmm, depName => null);
            
            // Register HLE stub to see if it causes crash
            var hle = new LibKernelHle(vmm, linker);

            var engine = new EmulatorEngine(vmm, syscallRouter);
            
            byte[] elfData = File.ReadAllBytes(targetFilePath);
            var mainModule = linker.LoadModule(Path.GetFileName(targetFilePath), elfData);

            engine.Run(mainModule.BaseAddress + mainModule.Image.Header.EntryPoint);
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
