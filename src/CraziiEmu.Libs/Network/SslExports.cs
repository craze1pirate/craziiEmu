// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Concurrent;
using System.Threading;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Network;

public static class SslExports
{
    private const int SslErrorInvalidId = unchecked((int)0x8095F006);
    private const int SslErrorOutOfSize = unchecked((int)0x8095F008);

    private static readonly ConcurrentDictionary<int, SslContext> _contexts = new();
    private static int _nextContextId;

    private sealed record SslContext(ulong PoolSize);

    [SysAbiExport(
        Nid = "hdpVEUDFW3s",
        ExportName = "sceSslInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceSsl")]
    public static int SslInit(CpuContext ctx)
    {
        var poolSize = ctx[CpuRegister.Rdi];
        if (poolSize == 0)
        {
            return SetReturn(ctx, SslErrorOutOfSize);
        }

        var id = Interlocked.Increment(ref _nextContextId);
        _contexts[id] = new SslContext(poolSize);

        TraceSsl("init", id, poolSize);
        ctx[CpuRegister.Rax] = unchecked((ulong)id);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "0K1yQ6Lv-Yc",
        ExportName = "sceSslTerm",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceSsl")]
    public static int SslTerm(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!_contexts.TryRemove(id, out _))
        {
            return SetReturn(ctx, SslErrorInvalidId);
        }

        TraceSsl("term", id, 0);
        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "viRXSHZYd0c",
        ExportName = "sceSslClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceSsl")]
    public static int SslClose(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        TraceSsl("close", id, 0);
        return SetReturn(ctx, 0);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static void TraceSsl(string operation, int id, ulong arg0)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CraziiEmu_LOG_SSL"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine(
            $"[LOADER][TRACE] ssl.{operation} id={id} arg0=0x{arg0:X16}");
    }
}
