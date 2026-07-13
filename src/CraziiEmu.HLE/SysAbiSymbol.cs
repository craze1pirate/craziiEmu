// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE;

public readonly struct SysAbiSymbol
{
    public SysAbiSymbol(string nid, string aliasName, string exportName, Generation target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nid);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportName);

        Nid = nid;
        AliasName = aliasName;
        ExportName = exportName;
        Target = target;
    }

    public string Nid { get; }

    public string AliasName { get; }

    public string ExportName { get; }

    public Generation Target { get; }
}
