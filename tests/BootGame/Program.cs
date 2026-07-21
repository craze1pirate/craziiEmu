using System;
using System.IO;
using CraziiEmu.Core.Runtime;

class Program
{
    static void Main(string[] args)
    {
        string ebootPath = @"C:\Users\crazy\Downloads\[PS5ID]-New Super Lucky's Tale 01.001 PPSA34823\[PS5ID]-New Super Lucky's Tale 01.001 PPSA34823\eboot.bin";
        
        var options = new CraziiEmuRuntimeOptions();
        using var runtime = CraziiEmuRuntime.CreateDefault(options);
        
        try
        {
            Console.WriteLine($"Starting emulation of {ebootPath}...");
            var result = runtime.Run(ebootPath);
            Console.WriteLine($"Finished with result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Emulation Halted: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
