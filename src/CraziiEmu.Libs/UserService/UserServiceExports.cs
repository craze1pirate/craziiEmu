// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using System.Buffers.Binary;
using System.Text;
using System.Threading;

namespace CraziiEmu.Libs.UserService;

public static class UserServiceExports
{
    private const int OrbisUserServiceErrorInvalidArgument = unchecked((int)0x80960005);
    private const int OrbisUserServiceErrorNoEvent = unchecked((int)0x80960007);
    private const int OrbisUserServiceErrorInvalidParameter = unchecked((int)0x80960009);
    private const int OrbisUserServiceErrorBufferTooShort = unchecked((int)0x8096000A);
    private const int PrimaryUserId = 1;
    private const int InvalidUserId = -1;
    private const string PrimaryUserName = "CraziiEmu";
    private static int _loginEventDelivered;

    [SysAbiExport(
        Nid = "j3YMu1MVNNo",
        ExportName = "sceUserServiceInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceInitialize(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "CdWp0oHWGr0",
        ExportName = "sceUserServiceGetInitialUser",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceGetInitialUser(CpuContext ctx)
    {
        var userIdAddress = ctx[CpuRegister.Rdi];
        if (userIdAddress == 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidArgument);
        }

        return TryWriteInt32(ctx, userIdAddress, PrimaryUserId)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "fPhymKNvK-A",
        ExportName = "sceUserServiceGetLoginUserIdList",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceGetLoginUserIdList(CpuContext ctx)
    {
        var userIdListAddress = ctx[CpuRegister.Rdi];
        if (userIdListAddress == 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidArgument);
        }

        Span<byte> userIds = stackalloc byte[sizeof(int) * 4];
        BinaryPrimitives.WriteInt32LittleEndian(userIds[0x00..], PrimaryUserId);
        BinaryPrimitives.WriteInt32LittleEndian(userIds[0x04..], InvalidUserId);
        BinaryPrimitives.WriteInt32LittleEndian(userIds[0x08..], InvalidUserId);
        BinaryPrimitives.WriteInt32LittleEndian(userIds[0x0C..], InvalidUserId);
        return ctx.Memory.TryWrite(userIdListAddress, userIds)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "yH17Q6NWtVg",
        ExportName = "sceUserServiceGetEvent",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceGetEvent(CpuContext ctx)
    {
        var eventAddress = ctx[CpuRegister.Rdi];
        if (eventAddress == 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidArgument);
        }

        if (Interlocked.Exchange(ref _loginEventDelivered, 1) != 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorNoEvent);
        }

        Span<byte> payload = stackalloc byte[sizeof(int) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(payload[0..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload[sizeof(int)..], PrimaryUserId);
        return ctx.Memory.TryWrite(eventAddress, payload)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "1xxcMiGu2fo",
        ExportName = "sceUserServiceGetUserName",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceGetUserName(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var nameAddress = ctx[CpuRegister.Rsi];
        var capacity = ctx[CpuRegister.Rdx];
        if (userId != PrimaryUserId)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidParameter);
        }

        if (nameAddress == 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidArgument);
        }

        var nameBytes = Encoding.UTF8.GetBytes(PrimaryUserName);
        if (capacity <= (ulong)nameBytes.Length)
        {
            return SetReturn(ctx, OrbisUserServiceErrorBufferTooShort);
        }

        Span<byte> output = stackalloc byte[nameBytes.Length + 1];
        nameBytes.CopyTo(output);
        return ctx.Memory.TryWrite(nameAddress, output)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "D-CzAxQL0XI",
        ExportName = "sceUserServiceGetPlatformPrivacySetting",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceUserService")]
    public static int UserServiceGetPlatformPrivacySetting(CpuContext ctx)
    {
        var parameterId = unchecked((int)ctx[CpuRegister.Rdi]);
        var valueAddress = ctx[CpuRegister.Rsi];
        if (parameterId != 1000)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidParameter);
        }

        if (valueAddress == 0)
        {
            return SetReturn(ctx, OrbisUserServiceErrorInvalidArgument);
        }

        return TryWriteInt32(ctx, valueAddress, 0)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    private static bool TryWriteInt32(CpuContext ctx, ulong address, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return ctx.Memory.TryWrite(address, bytes);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }
}
