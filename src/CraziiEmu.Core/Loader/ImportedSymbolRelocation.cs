// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.Loader;

public readonly record struct ImportedSymbolRelocation(
    ulong TargetAddress,
    long Addend,
    string Nid,
    bool IsData);
