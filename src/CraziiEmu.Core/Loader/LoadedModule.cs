// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Generic;

namespace CraziiEmu.Core.Loader;

public class LoadedModule
{
    public string Name { get; }
    public ulong BaseAddress { get; }
    public ElfImage Image { get; }

    public LoadedModule(string name, ulong baseAddress, ElfImage image)
    {
        Name = name;
        BaseAddress = baseAddress;
        Image = image;
    }
}
