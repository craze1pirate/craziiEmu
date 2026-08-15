// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Buffers.Binary;

namespace CraziiEmu.Libs.Np;

public static class NpManagerExports
{
    private const int NpTitleIdSize = 16;
    private const int NpTitleSecretSize = 128;
    private const int NpErrorInvalidArgument = unchecked((int)0x80550003);
    private const int NpErrorSignedOut = unchecked((int)0x80550006);
    private const int NpErrorAborted = unchecked((int)0x80550012);
    private const int NpErrorRequestMax = unchecked((int)0x80550013);
    private const int NpErrorRequestNotFound = unchecked((int)0x80550014);
    private const int NpRequestMax = 128;

    private enum NpRequestState { Free, Ready, Aborted, Complete }

    private sealed class NpRequest
    {
        public NpRequestState State;
        public bool Async;
        public int Result;
    }

    private static readonly object _requestGate = new();
    private static readonly List<NpRequest> _requests = new();

    [SysAbiExport(
        Nid = "hw5KNqAAels",
        ExportName = "sceNpRegisterNpReachabilityStateCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterNpReachabilityStateCallback(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        var userdata = ctx[CpuRegister.Rsi];
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "KfGZg2y73oM",
        ExportName = "sceNpCheckNpReachability",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckNpReachability(CpuContext ctx)
    {
        var reqId = unchecked((int)ctx[CpuRegister.Rdi]);
        var userId = unchecked((int)ctx[CpuRegister.Rsi]);

        if (reqId <= 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        // KytyPS5 completes the request with np_error_signed_out so that
        // PSNCore.prx takes the offline path instead of trying to initialise
        // online session objects that crash on NULL pointers.
        var result = CompleteSignedOut(reqId);
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    [SysAbiExport(
        Nid = "3Zl8BePTh9Y",
        ExportName = "sceNpCheckCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallback(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "S7QTn72PrDw",
        ExportName = "sceNpDeleteRequest",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpDeleteRequest(CpuContext ctx)
    {
        var reqId = unchecked((int)ctx[CpuRegister.Rdi]);
        var result = DeleteRequest(reqId);
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    [SysAbiExport(
        Nid = "GpLQDNKICac",
        ExportName = "sceNpCreateRequest",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCreateRequest(CpuContext ctx)
    {
        var reqId = CreateRequest(async: false);
        ctx[CpuRegister.Rax] = unchecked((ulong)reqId);
        return reqId > 0 ? (int)OrbisGen2Result.ORBIS_GEN2_OK : reqId;
    }

    [SysAbiExport(
        Nid = "Oad3rvY-NJQ",
        ExportName = "sceNpHasSignedUp",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpHasSignedUp(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var hasSignedUpAddress = ctx[CpuRegister.Rsi];

        if (hasSignedUpAddress == 0)
        {
            return ctx.SetReturn(NpErrorInvalidArgument);
        }

        Span<byte> boolValue = stackalloc byte[1];
        boolValue[0] = 0; // false
        if (!ctx.Memory.TryWrite(hasSignedUpAddress, boolValue))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "JELHf4xPufo",
        ExportName = "sceNpCheckCallbackForLib",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallbackForLib(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    // Offline profile: the online id payload is left untouched and the call
    // reports success, matching the other offline NpManager stubs here.
    [SysAbiExport(
        Nid = "XDncXQIJUSk",
        ExportName = "sceNpGetOnlineId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetOnlineId(CpuContext ctx)
    {
        // Gen5 ABI: user ID, then output structure.
        return WriteOfflineOnlineId(ctx, ctx[CpuRegister.Rsi]);
    }

    [SysAbiExport(
        Nid = "VfRSmPmj8Q8",
        ExportName = "sceNpRegisterStateCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallback(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "qQJfO8HAiaY",
        ExportName = "sceNpRegisterStateCallbackA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallbackA(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "0c7HbXRKUt4",
        ExportName = "sceNpRegisterStateCallbackForToolkit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManagerForToolkit")]
    public static int NpRegisterStateCallbackForToolkit(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "eQH7nWPcAgc",
        ExportName = "sceNpGetState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetState(CpuContext ctx)
    {
        var stateAddress = ctx[CpuRegister.Rsi];
        if (stateAddress == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> stateBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(stateBytes, 1);
        return ctx.Memory.TryWrite(stateAddress, stateBytes)
            ? ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK)
            : ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "rbknaUjpqWo",
        ExportName = "sceNpGetAccountIdA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetAccountIdA(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var accountIdAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || accountIdAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        // The offline profile exposed by sceNpGetState is signed in. Keep the
        // account query consistent with that state: Unity's PSN integration
        // treats SIGNED_OUT as an exceptional state and retries it every frame.
        // A stable local-only id is sufficient for titles which only use the
        // value as a profile key.
        Span<byte> accountId = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(accountId, 1);
        return ctx.Memory.TryWrite(accountIdAddress, accountId)
            ? SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "JT+t00a3TxA",
        ExportName = "sceNpGetAccountCountryA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetAccountCountryA(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var countryAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || countryAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        Span<byte> country = stackalloc byte[4];
        country[0] = (byte)'U';
        country[1] = (byte)'S';
        country[2] = 0;
        country[3] = 0;
        return ctx.Memory.TryWrite(countryAddress, country)
            ? SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "e-ZuhGEoeC4",
        ExportName = "sceNpGetNpReachabilityState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetNpReachabilityState(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var stateAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || stateAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        Span<byte> state = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(state, 0); // Unavailable while offline.
        return ctx.Memory.TryWrite(stateAddress, state)
            ? SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "Ec63y59l9tw",
        ExportName = "sceNpSetNpTitleId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpSetNpTitleId(CpuContext ctx)
    {
        var titleIdAddress = ctx[CpuRegister.Rdi];
        var titleSecretAddress = ctx[CpuRegister.Rsi];
        if (titleIdAddress == 0 || titleSecretAddress == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> titleId = stackalloc byte[NpTitleIdSize];
        Span<byte> titleSecret = stackalloc byte[NpTitleSecretSize];
        if (!ctx.Memory.TryRead(titleIdAddress, titleId) ||
            !ctx.Memory.TryRead(titleSecretAddress, titleSecret))
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TraceNp($"set_np_title_id title='{ReadTitleId(titleId)}'");
        return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static string ReadTitleId(ReadOnlySpan<byte> bytes)
    {
        var length = 0;
        while (length < 12 && length < bytes.Length && bytes[length] != 0)
        {
            length++;
        }

        return length == 0
            ? string.Empty
            : System.Text.Encoding.ASCII.GetString(bytes[..length]);
    }

    private static void TraceNp(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CRAZIIEMU_LOG_NP"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine($"[LOADER][TRACE] np.{message}");
    }

    private static int WriteOfflineOnlineId(CpuContext ctx, ulong address)
    {
        if (address == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // SceNpOnlineId is a 16-byte handle plus four trailing bytes.
        Span<byte> onlineId = stackalloc byte[20];
        "Player"u8.CopyTo(onlineId);
        return ctx.Memory.TryWrite(address, onlineId)
            ? ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK)
            : ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    // --- NP Request State Machine (ported from KytyPS5) ---

    private static int CreateRequest(bool async)
    {
        lock (_requestGate)
        {
            for (int i = 0; i < _requests.Count; i++)
            {
                if (_requests[i].State == NpRequestState.Free)
                {
                    _requests[i].State = NpRequestState.Ready;
                    _requests[i].Async = async;
                    _requests[i].Result = 0;
                    return i + 1;
                }
            }

            if (_requests.Count >= NpRequestMax)
            {
                return NpErrorRequestMax;
            }

            _requests.Add(new NpRequest { State = NpRequestState.Ready, Async = async });
            return _requests.Count;
        }
    }

    private static int CompleteSignedOut(int reqId)
    {
        lock (_requestGate)
        {
            if (reqId <= 0 || reqId > _requests.Count ||
                _requests[reqId - 1].State == NpRequestState.Free)
            {
                return NpErrorRequestNotFound;
            }

            var request = _requests[reqId - 1];
            if (request.State == NpRequestState.Complete)
            {
                request.Result = NpErrorInvalidArgument;
                return NpErrorInvalidArgument;
            }
            if (request.State == NpRequestState.Aborted)
            {
                request.Result = NpErrorAborted;
                return NpErrorAborted;
            }

            request.State = NpRequestState.Complete;
            request.Result = NpErrorSignedOut;
            return request.Async ? 0 : NpErrorSignedOut;
        }
    }

    private static int DeleteRequest(int reqId)
    {
        lock (_requestGate)
        {
            if (reqId <= 0 || reqId > _requests.Count ||
                _requests[reqId - 1].State == NpRequestState.Free)
            {
                return NpErrorRequestNotFound;
            }

            _requests[reqId - 1].State = NpRequestState.Free;
            _requests[reqId - 1].Async = false;
            _requests[reqId - 1].Result = 0;
            return 0;
        }
    }
}
