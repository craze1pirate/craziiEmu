// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Collections.Concurrent;
using System.Threading;

namespace CraziiEmu.Libs.Network;

public static class HttpExports
{
    private const int HttpErrorInvalidId = unchecked((int)0x80431100);
    private const int HttpErrorInvalidValue = unchecked((int)0x804311FE);

    private static readonly ConcurrentDictionary<int, HttpContext> Contexts = new();
    private static readonly ConcurrentDictionary<int, HttpTemplate> Templates = new();
    private static int _nextContextId;
    private static int _nextTemplateId = 0x1000;

    private sealed record HttpContext(int NetMemoryId, int SslContextId, ulong PoolSize);

    private sealed record HttpTemplate(int ContextId, ulong UserAgentAddress, int HttpVersion, bool AutoProxyConfig);

    [SysAbiExport(
        Nid = "A9cVMUtEp4Y",
        ExportName = "sceHttpInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceHttp")]
    public static int HttpInit(CpuContext ctx)
    {
        var netMemoryId = unchecked((int)ctx[CpuRegister.Rdi]);
        var sslContextId = unchecked((int)ctx[CpuRegister.Rsi]);
        var poolSize = ctx[CpuRegister.Rdx];
        if (poolSize == 0)
        {
            return SetReturn(ctx, HttpErrorInvalidValue);
        }

        var id = Interlocked.Increment(ref _nextContextId);
        Contexts[id] = new HttpContext(netMemoryId, sslContextId, poolSize);
        TraceHttp("init", id, unchecked((ulong)netMemoryId), unchecked((ulong)sslContextId), poolSize, 0);
        ctx[CpuRegister.Rax] = unchecked((ulong)id);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "0gYjPTR-6cY",
        ExportName = "sceHttpCreateTemplate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceHttp")]
    public static int HttpCreateTemplate(CpuContext ctx)
    {
        var contextId = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!Contexts.ContainsKey(contextId))
        {
            return SetReturn(ctx, HttpErrorInvalidId);
        }

        var userAgentAddress = ctx[CpuRegister.Rsi];
        var httpVersion = unchecked((int)ctx[CpuRegister.Rdx]);
        var autoProxyConfig = ctx[CpuRegister.Rcx] != 0;
        var id = Interlocked.Increment(ref _nextTemplateId);
        Templates[id] = new HttpTemplate(contextId, userAgentAddress, httpVersion, autoProxyConfig);
        TraceHttp("create_template", id, unchecked((ulong)contextId), userAgentAddress, unchecked((ulong)httpVersion), autoProxyConfig ? 1UL : 0UL);
        ctx[CpuRegister.Rax] = unchecked((ulong)id);
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "4I8vEpuEhZ8",
        ExportName = "sceHttpDeleteTemplate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceHttp")]
    public static int HttpDeleteTemplate(CpuContext ctx)
    {
        var templateId = unchecked((int)ctx[CpuRegister.Rdi]);
        return Templates.TryRemove(templateId, out _)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, HttpErrorInvalidId);
    }

    [SysAbiExport(
        Nid = "Ik-KpLTlf7Q",
        ExportName = "sceHttpTerm",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceHttp")]
    public static int HttpTerm(CpuContext ctx)
    {
        var contextId = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!Contexts.TryRemove(contextId, out _))
        {
            return SetReturn(ctx, HttpErrorInvalidId);
        }

        foreach (var pair in Templates)
        {
            if (pair.Value.ContextId == contextId)
            {
                Templates.TryRemove(pair.Key, out _);
            }
        }

        return SetReturn(ctx, 0);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static void TraceHttp(string operation, int id, ulong arg0, ulong arg1, ulong arg2, ulong arg3)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CraziiEmu_LOG_HTTP"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine(
            $"[LOADER][TRACE] http.{operation} id={id} arg0=0x{arg0:X16} arg1=0x{arg1:X16} arg2=0x{arg2:X16} arg3=0x{arg3:X16}");
    }
}
