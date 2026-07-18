// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.HLE;

[Flags]
public enum GuestPageProtection
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}
