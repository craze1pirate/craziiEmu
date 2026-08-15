// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Collections.Concurrent;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Np;

public static class NpWebApi2Exports
{
    private const int NpWebApi2ErrorInvalidArgument = unchecked((int)0x80553402);

    public sealed class UserContextState
    {
        public int UserContextId { get; }
        public int LibContextId { get; }
        public int UserId { get; }

        public UserContextState(int userContextId, int libContextId, int userId)
        {
            UserContextId = userContextId;
            LibContextId = libContextId;
            UserId = userId;
        }
    }

    private static int _initialized;
    private static int _nextFilterId;
    private static int _nextUserContextId;
    private static readonly ConcurrentDictionary<int, UserContextState> _userContexts = new();

    public static bool TryGetUserContext(int userContextId, out UserContextState? state)
    {
        return _userContexts.TryGetValue(userContextId, out state);
    }

    [SysAbiExport(
        Nid = "+o9816YQhqQ",
        ExportName = "sceNpWebApi2Initialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2Initialize(CpuContext ctx)
    {
        var httpContextId = unchecked((int)ctx[CpuRegister.Rdi]);
        var poolSize = ctx[CpuRegister.Rsi];

        if (httpContextId <= 0 || poolSize == 0)
        {
            return ctx.SetReturn(NpWebApi2ErrorInvalidArgument);
        }

        Interlocked.Exchange(ref _initialized, 1);
        TraceNpWebApi2("init", httpContextId, poolSize);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "WV1GwM32NgY",
        ExportName = "sceNpWebApi2PushEventCreateHandle",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2InitializeAlt(CpuContext ctx)
    {
        Interlocked.Exchange(ref _initialized, 1);
        TraceNpWebApi2("init-alt", unchecked((int)ctx[CpuRegister.Rdi]), ctx[CpuRegister.Rsi]);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "sk54bi6FtYM",
        ExportName = "sceNpWebApi2CreateUserContext",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2CreateUserContext(CpuContext ctx)
    {
        var libCtxId = unchecked((int)ctx[CpuRegister.Rdi]);
        var userId = unchecked((int)ctx[CpuRegister.Rsi]);

        var userContextId = Interlocked.Increment(ref _nextUserContextId);
        var userCtxState = new UserContextState(userContextId, libCtxId, userId);
        _userContexts[userContextId] = userCtxState;

        TraceNpWebApi2("create-user-context", userContextId, (ulong)userId);
        return ctx.SetReturn(userContextId);
    }

    [SysAbiExport(
        Nid = "9X9+cneTGUU",
        ExportName = "sceNpWebApi2DeleteUserContext",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2DeleteUserContext(CpuContext ctx)
    {
        var userContextId = unchecked((int)ctx[CpuRegister.Rdi]);
        if (!_userContexts.TryRemove(userContextId, out _))
        {
            return ctx.SetReturn(NpWebApi2ErrorInvalidArgument);
        }

        TraceNpWebApi2("delete-user-context", userContextId, 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "fIATVMo4Y1w",
        ExportName = "sceNpWebApi2PushEventDeleteHandle",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2PushEventDeleteHandle(CpuContext ctx)
    {
        var libCtxId = unchecked((int)ctx[CpuRegister.Rdi]);
        var handleId = unchecked((int)ctx[CpuRegister.Rsi]);
        TraceNpWebApi2("push-event-delete-handle", libCtxId, unchecked((ulong)handleId));
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "MsaFhR+lPE4",
        ExportName = "sceNpWebApi2PushEventCreateFilter",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2PushEventCreateFilter(CpuContext ctx)
    {
        var libCtxId = unchecked((int)ctx[CpuRegister.Rdi]);
        var handleId = unchecked((int)ctx[CpuRegister.Rsi]);
        var nameAddress = ctx[CpuRegister.Rdx];
        var serviceLabel = unchecked((uint)ctx[CpuRegister.Rcx]);
        var filterParam = ctx[CpuRegister.R8];
        var filterParamNum = ctx[CpuRegister.R9];

        var filterId = Interlocked.Increment(ref _nextFilterId);
        TraceNpWebApi2("push-event-create-filter", libCtxId, unchecked((ulong)filterId));
        return ctx.SetReturn(filterId);
    }

    private static int _nextCallbackId = 1;

    [SysAbiExport(
        Nid = "fY3QqeNkF8k",
        ExportName = "sceNpWebApi2PushEventRegisterCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2PushEventRegisterCallback(CpuContext ctx)
    {
        var userContextId = unchecked((int)ctx[CpuRegister.Rdi]);
        var filterId = unchecked((int)ctx[CpuRegister.Rsi]);
        var callback = ctx[CpuRegister.Rdx];
        var userArg = ctx[CpuRegister.Rcx];

        if (!_userContexts.ContainsKey(userContextId))
        {
            return ctx.SetReturn(NpWebApi2ErrorInvalidArgument);
        }

        var callbackId = Interlocked.Increment(ref _nextCallbackId);
        TraceNpWebApi2("push-event-register-callback", userContextId, unchecked((ulong)callbackId));
        return ctx.SetReturn(callbackId);
    }

    [SysAbiExport(
        Nid = "bEvXpcEk200",
        ExportName = "sceNpWebApi2Terminate",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi2")]
    public static int NpWebApi2Terminate(CpuContext ctx)
    {
        var libraryContextId = unchecked((int)ctx[CpuRegister.Rdi]);
        _userContexts.Clear();
        Interlocked.Exchange(ref _initialized, 0);
        TraceNpWebApi2("term", libraryContextId, 0);
        return ctx.SetReturn(0);
    }

    private static void TraceNpWebApi2(string operation, int id, ulong arg0)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_NP_WEB_API2"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine(
            $"[LOADER][TRACE] npwebapi2.{operation} id={id} arg0=0x{arg0:X16} initialized={Volatile.Read(ref _initialized)}");
    }
}
