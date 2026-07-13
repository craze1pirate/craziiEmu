// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE;

public interface ISymbolCatalog
{
    bool TryGetByNid(string nid, out SysAbiSymbol symbol);

    bool TryGetByExportName(string exportName, out SysAbiSymbol symbol);
}
