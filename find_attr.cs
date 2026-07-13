using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

class Program
{
    static void Main()
    {
        string[] dlls = { "CraziiEmu.Core.dll", "CraziiEmu.HLE.dll", "CraziiEmu.Libs.dll", "CraziiEmu.Runner.dll" };
        foreach (var dll in dlls)
        {
            string path = Path.Combine(@"d:\Projects\myps5\CraziiEmu\artifacts\bin\Release\net10.0", dll);
            if (!File.Exists(path)) continue;
            try {
                var asm = Assembly.LoadFrom(path);
                foreach (var type in asm.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        var attrs = method.GetCustomAttributes();
                        foreach(var attr in attrs) {
                            if (attr.GetType().Name.Contains("UnmanagedCallersOnly")) {
                                Console.WriteLine($"FOUND: {type.FullName}.{method.Name} in {dll}");
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Error loading {dll}: {ex.Message}");
            }
        }
    }
}
