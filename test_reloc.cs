using System;
using System.IO;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
class P { 
    static void Main() { 
        var b = File.ReadAllBytes(@"C:\Users\crazy\Downloads\crispy-doom-ps5-v1.0-7.1.0\CrispyDoom\payloads\crispy-doom.elf"); 
        var e = ELFReader.Load(b); 
        foreach(var sec in e.Sections) {
            if (sec is IRelocationSection relaSec) {
                foreach(var rela in relaSec.Relocations) {
                    if (rela.Offset == 0x58F890) {
                        Console.WriteLine($"Found relocation for 0x58F890: Type={rela.Type} Addend={rela.Addend}");
                    }
                }
            }
        }
    } 
}
