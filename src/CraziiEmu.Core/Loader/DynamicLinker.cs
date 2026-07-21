// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CraziiEmu.Core.Memory;

namespace CraziiEmu.Core.Loader;

public class DynamicLinker
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    private readonly VirtualMemoryManager _vmm;
    private readonly Func<string, byte[]?> _dependencyResolver;
    
    private readonly Dictionary<string, Dictionary<string, ulong>> _hleStubs = new();
    private readonly Dictionary<string, LoadedModule> _loadedModules = new();
    private readonly Dictionary<string, ulong> _dummyStubs = new();

    public DynamicLinker(VirtualMemoryManager vmm, Func<string, byte[]?> dependencyResolver)
    {
        _vmm = vmm;
        _dependencyResolver = dependencyResolver;
    }

    public void RegisterHleStub(string moduleName, string symbolName, ulong address)
    {
        if (!_hleStubs.TryGetValue(moduleName, out var moduleStubs))
        {
            moduleStubs = new Dictionary<string, ulong>();
            _hleStubs[moduleName] = moduleStubs;
        }
        moduleStubs[symbolName] = address;
    }

    public bool TryGetSymbolNameByAddress(ulong address, out string? symbolName)
    {
        foreach (var moduleStubs in _hleStubs.Values)
        {
            foreach (var kvp in moduleStubs)
            {
                if (kvp.Value == address)
                {
                    symbolName = kvp.Key;
                    return true;
                }
            }
        }
        symbolName = null;
        return false;
    }

    public LoadedModule LoadModule(string name, byte[] elfData)
    {
        if (_loadedModules.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var elfImage = ElfLoader.Load(elfData);
        
        ulong minVA = ulong.MaxValue;
        ulong maxVA = 0;
        foreach (var ph in elfImage.ProgramHeaders)
        {
            if (ph.HeaderType == ProgramHeaderType.Load)
            {
                minVA = Math.Min(minVA, ph.VirtualAddress);
                maxVA = Math.Max(maxVA, ph.VirtualAddress + ph.MemorySize);
            }
        }

        ulong memSize = maxVA - minVA;
        ulong baseAddress = _vmm.AllocateCpu(memSize);
        
        // Ensure memory is committed safely using VirtualAlloc (MEM_COMMIT = 0x1000, PAGE_READWRITE = 0x04)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            VirtualAlloc((IntPtr)baseAddress, (nuint)memSize, 0x1000, 0x04);
        }
        else
        {
            // For Linux, we don't strictly need this since mmap MAP_NORESERVE commits on first write safely,
            // but we can touch it safely since Linux signal handler doesn't have the same strict UnmanagedCallersOnly issues.
            unsafe
            {
                for (ulong p = baseAddress; p < baseAddress + memSize; p += 4096)
                {
                    *(byte*)p = 0;
                }
            }
        }

        foreach (var ph in elfImage.ProgramHeaders)
        {
            if (ph.HeaderType == ProgramHeaderType.Load)
            {
                ulong targetAddress = baseAddress + (ph.VirtualAddress - minVA);
                var span = _vmm.GetSpan(targetAddress, (int)ph.FileSize);
                new ReadOnlySpan<byte>(elfData, (int)ph.Offset, (int)ph.FileSize).CopyTo(span);
                
                if (ph.MemorySize > ph.FileSize)
                {
                    var bssSpan = _vmm.GetSpan(targetAddress + ph.FileSize, (int)(ph.MemorySize - ph.FileSize));
                    bssSpan.Clear();
                }
            }
        }

        var module = new LoadedModule(name, baseAddress, elfImage);
        _loadedModules[name] = module;

        foreach (var needed in elfImage.NeededLibraries)
        {
            if (!_loadedModules.ContainsKey(needed))
            {
                var depData = _dependencyResolver(needed);
                if (depData != null)
                {
                    LoadModule(needed, depData);
                }
            }
        }

        ResolveRelocations(module, minVA);

        return module;
    }

    private void ResolveRelocations(LoadedModule module, ulong minVA)
    {
        foreach (var rela in module.Image.Relocations)
        {
            ulong targetAddress = module.BaseAddress + (rela.Offset - minVA);
            
            ulong symValue = 0;
            if (rela.SymbolIndex != 0 && rela.SymbolIndex < module.Image.Symbols.Count)
            {
                var sym = module.Image.Symbols[(int)rela.SymbolIndex];
                string? symName = null;
                if (module.Image.StringTable.TryGetValue(sym.NameIndex, out var name))
                {
                    symName = name;
                }

                if (symName != null)
                {
                    symValue = ResolveSymbol(symName, module.Image.NeededLibraries);
                    if (symValue == 0)
                    {
                        symValue = GetOrCreateDummyStub(symName);
                    }
                }
                
                if (symValue == 0 && sym.Value != 0)
                {
                    symValue = module.BaseAddress + (sym.Value - minVA);
                }
            }

            var span = _vmm.GetSpan(targetAddress, 8);
            
            const uint R_X86_64_64 = 1;
            const uint R_X86_64_GLOB_DAT = 5;
            const uint R_X86_64_JUMP_SLOT = 7;
            const uint R_X86_64_RELATIVE = 8;
            
            ulong result = 0;
            
            switch (rela.RelocationType)
            {
                case R_X86_64_64:
                case R_X86_64_GLOB_DAT:
                case R_X86_64_JUMP_SLOT:
                    result = symValue + (ulong)rela.Addend;
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span, result);
                    // Diagnostic: log the specific GOT slot that the crash-site caller uses
                    if (targetAddress == 0x0000000802322A38UL)
                    {
                        string? diagSymName = null;
                        if (rela.SymbolIndex != 0 && rela.SymbolIndex < module.Image.Symbols.Count)
                        {
                            var diagSym = module.Image.Symbols[(int)rela.SymbolIndex];
                            module.Image.StringTable.TryGetValue(diagSym.NameIndex, out diagSymName);
                        }
                        Console.Error.WriteLine(
                            $"[CRASH-DIAG] GOT slot 0x{targetAddress:X16} resolved to 0x{result:X16} " +
                            $"symName='{diagSymName ?? "<null>"}' type={rela.RelocationType} " +
                            $"symIdx={rela.SymbolIndex}");
                    }
                    break;
                    
                case R_X86_64_RELATIVE:
                    result = module.BaseAddress + (ulong)(rela.Addend - (long)minVA);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span, result);
                    break;
            }
        }
    }

    private ulong ResolveSymbol(string symName, IReadOnlyList<string> neededLibraries)
    {
        foreach (var needed in neededLibraries)
        {
            if (_hleStubs.TryGetValue(needed, out var stubs))
            {
                if (stubs.TryGetValue(symName, out var address))
                {
                    return address;
                }
            }
        }

        foreach (var needed in neededLibraries)
        {
            if (_loadedModules.TryGetValue(needed, out var module))
            {
                // We need to know minVA of the needed library to resolve correctly.
                // For simplicity, let's just find it from ProgramHeaders.
                ulong modMinVA = ulong.MaxValue;
                foreach (var ph in module.Image.ProgramHeaders) {
                    if (ph.HeaderType == ProgramHeaderType.Load) modMinVA = Math.Min(modMinVA, ph.VirtualAddress);
                }
                
                foreach (var sym in module.Image.Symbols)
                {
                    if (sym.Value != 0 && module.Image.StringTable.TryGetValue(sym.NameIndex, out var name) && name == symName)
                    {
                        return module.BaseAddress + (sym.Value - modMinVA);
                    }
                }
            }
        }

        return 0;
    }

    private ulong GetOrCreateDummyStub(string symName)
    {
        if (_dummyStubs.TryGetValue(symName, out ulong stub)) return stub;
        stub = _vmm.AllocateCpu(16);
        var span = _vmm.GetSpan(stub, 16);
        
        // Functions like sceVideoOutOpen return a handle (must be >0), otherwise games panic.
        // Default to returning 1 (success handle) for everything except functions that we KNOW must return 0.
        if (symName.Contains("LoadModule") || symName.Contains("Close") || symName.Contains("Destroy"))
        {
            span[0] = 0x31; span[1] = 0xC0; // xor eax, eax
            span[2] = 0xC3; // ret
        }
        else
        {
            span[0] = 0xB8; span[1] = 0x01; span[2] = 0x00; span[3] = 0x00; span[4] = 0x00; // mov eax, 1
            span[5] = 0xC3; // ret
        }
        
        Console.Error.WriteLine($"[DynamicLinker] Created dummy HLE stub for unresolved import '{symName}' at 0x{stub:X16}");
        _dummyStubs[symName] = stub;
        return stub;
    }
}
