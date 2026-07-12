// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Core.HLE;

/// <summary>
/// Defines the ABI table to use for system call routing.
/// </summary>
public enum SyscallAbi
{
    /// <summary>
    /// Linux x86-64 ABI (e.g. sys_write = 1, sys_exit = 60).
    /// </summary>
    Linux,

    /// <summary>
    /// FreeBSD / Orbis OS ABI (e.g. sys_write = 4, sys_exit = 1).
    /// </summary>
    FreeBsd
}
