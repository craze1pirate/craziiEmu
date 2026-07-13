import struct, sys
with open(r'C:\Users\crazy\Downloads\crispy-doom-ps5-v1.0-7.1.0\CrispyDoom\payloads\crispy-doom.elf', 'rb') as f:
    f.seek(32)
    phoff = struct.unpack('<Q', f.read(8))[0]
    f.seek(54)
    phentsize, phnum = struct.unpack('<HH', f.read(4))
    print(f'PHOFF={phoff} ENT={phentsize} NUM={phnum}')
    f.seek(phoff)
    for _ in range(phnum):
        ph = f.read(phentsize)
        ptype = struct.unpack('<I', ph[:4])[0]
        offset = struct.unpack('<Q', ph[8:16])[0]
        vaddr = struct.unpack('<Q', ph[16:24])[0]
        filesz = struct.unpack('<Q', ph[32:40])[0]
        memsz = struct.unpack('<Q', ph[40:48])[0]
        print(f'Type={ptype} Offset={offset:X} VAddr={vaddr:X} FileSz={filesz:X} MemSz={memsz:X}')
