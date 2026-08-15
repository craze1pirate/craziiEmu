// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Buffers.Binary;
using CraziiEmu.HLE;

namespace CraziiEmu.Libs.Dialog;

public static class LoginDialogExports
{
    // LoginDialog Constants
    public const int LOGIN_STATUS_NONE        = 0;
    public const int LOGIN_STATUS_INITIALIZED = 1;
    public const int LOGIN_STATUS_RUNNING     = 2;
    public const int LOGIN_STATUS_FINISHED    = 3;
    public const int LOGIN_RESULT_OK          = 0;
    public const int LOGIN_MODE_ALL_USERS     = 0;
    public const int LOGIN_MODE_NOT_LOGGED_IN = 1;
    public const int LOGIN_USER_INVALID       = -1;
    public const int LOGIN_USER_ID            = 1000;

    public const int LOGIN_ERROR_NOT_INITIALIZED     = unchecked((int)0x81340001u);
    public const int LOGIN_ERROR_ALREADY_INITIALIZED = unchecked((int)0x81340002u);
    public const int LOGIN_ERROR_PARAM_INVALID       = unchecked((int)0x81340003u);
    public const int LOGIN_ERROR_INVALID_STATE       = unchecked((int)0x81340005u);

    // SigninDialog Constants
    public const int SIGNIN_STATUS_NONE          = 0;
    public const int SIGNIN_STATUS_INITIALIZED   = 1;
    public const int SIGNIN_STATUS_RUNNING       = 2;
    public const int SIGNIN_STATUS_FINISHED      = 3;
    public const int SIGNIN_RESULT_USER_CANCELED = 1;

    public const int SIGNIN_ERROR_NOT_INITIALIZED     = unchecked((int)0x81350001u);
    public const int SIGNIN_ERROR_ALREADY_INITIALIZED = unchecked((int)0x81350002u);
    public const int SIGNIN_ERROR_PARAM_INVALID       = unchecked((int)0x81350003u);
    public const int SIGNIN_ERROR_INVALID_STATE       = unchecked((int)0x81350005u);

    // State Variables
    private static int _loginStatus = LOGIN_STATUS_NONE;
    private static int _loginSelectedUser = LOGIN_USER_ID;

    private static int _signinStatus = SIGNIN_STATUS_NONE;

    public static void ResetStateForTest()
    {
        _loginStatus = LOGIN_STATUS_NONE;
        _loginSelectedUser = LOGIN_USER_ID;
        _signinStatus = SIGNIN_STATUS_NONE;
    }

    // -------------------------------------------------------------------------
    // libSceLoginDialog Exports
    // -------------------------------------------------------------------------

    [SysAbiExport(Nid = "qP-EvQRl2Hc", ExportName = "sceLoginDialogInitialize", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogInitialize(CpuContext ctx)
    {
        if (_loginStatus != LOGIN_STATUS_NONE)
        {
            return ctx.SetReturn(LOGIN_ERROR_ALREADY_INITIALIZED);
        }

        _loginStatus = LOGIN_STATUS_INITIALIZED;
        _loginSelectedUser = LOGIN_USER_ID;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "vMQJRUKsf3U", ExportName = "sceLoginDialogTerminate", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogTerminate(CpuContext ctx)
    {
        if (_loginStatus == LOGIN_STATUS_NONE)
        {
            return ctx.SetReturn(LOGIN_ERROR_NOT_INITIALIZED);
        }

        _loginStatus = LOGIN_STATUS_NONE;
        _loginSelectedUser = LOGIN_USER_ID;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "S56ra1+Tymg", ExportName = "sceLoginDialogOpen", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogOpen(CpuContext ctx)
    {
        if (_loginStatus != LOGIN_STATUS_INITIALIZED && _loginStatus != LOGIN_STATUS_FINISHED)
        {
            return ctx.SetReturn(LOGIN_ERROR_INVALID_STATE);
        }

        ulong paramPtr = ctx[CpuRegister.Rdi];
        if (paramPtr == 0)
        {
            return ctx.SetReturn(LOGIN_ERROR_PARAM_INVALID);
        }

        Span<byte> paramBuf = stackalloc byte[64];
        if (!ctx.Memory.TryRead(paramPtr, paramBuf))
        {
            return ctx.SetReturn(LOGIN_ERROR_PARAM_INVALID);
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(paramBuf[0..4]);
        int mode = BinaryPrimitives.ReadInt32LittleEndian(paramBuf[4..8]);
        int initialFocus = BinaryPrimitives.ReadInt32LittleEndian(paramBuf[40..44]);

        if (size != 64 || (mode != LOGIN_MODE_ALL_USERS && mode != LOGIN_MODE_NOT_LOGGED_IN))
        {
            return ctx.SetReturn(LOGIN_ERROR_PARAM_INVALID);
        }

        _loginSelectedUser = initialFocus != LOGIN_USER_INVALID ? initialFocus : LOGIN_USER_ID;
        _loginStatus = LOGIN_STATUS_RUNNING;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "F0XIzrG5yvw", ExportName = "sceLoginDialogClose", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogClose(CpuContext ctx)
    {
        if (_loginStatus == LOGIN_STATUS_NONE)
        {
            return ctx.SetReturn(LOGIN_ERROR_NOT_INITIALIZED);
        }

        _loginStatus = LOGIN_STATUS_FINISHED;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "2rc+egSfb5A", ExportName = "sceLoginDialogUpdateStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogUpdateStatus(CpuContext ctx)
    {
        if (_loginStatus == LOGIN_STATUS_RUNNING)
        {
            _loginStatus = LOGIN_STATUS_FINISHED;
        }

        return ctx.SetReturn(_loginStatus);
    }

    [SysAbiExport(Nid = "HAiWUEwEfGo", ExportName = "sceLoginDialogGetStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogGetStatus(CpuContext ctx)
    {
        return ctx.SetReturn(_loginStatus);
    }

    [SysAbiExport(Nid = "Btkx21f1M8k", ExportName = "sceLoginDialogGetResult", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogGetResult(CpuContext ctx)
    {
        if (_loginStatus == LOGIN_STATUS_NONE)
        {
            return ctx.SetReturn(LOGIN_ERROR_NOT_INITIALIZED);
        }

        ulong resultPtr = ctx[CpuRegister.Rdi];
        if (resultPtr == 0)
        {
            return ctx.SetReturn(LOGIN_ERROR_PARAM_INVALID);
        }

        Span<byte> resultBuf = stackalloc byte[16];
        resultBuf.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[0..4], LOGIN_RESULT_OK);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[4..8], _loginSelectedUser);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[8..12], 0);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[12..16], 0);

        if (!ctx.Memory.TryWrite(resultPtr, resultBuf))
        {
            return ctx.SetReturn(LOGIN_ERROR_PARAM_INVALID);
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "3NPobi5lNmk", ExportName = "sceLoginDialogParamInitialize", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceLoginDialog")]
    public static int LoginDialogParamInitialize(CpuContext ctx)
    {
        ulong paramPtr = ctx[CpuRegister.Rdi];
        if (paramPtr == 0)
        {
            return ctx.SetReturn(0);
        }

        Span<byte> paramBuf = stackalloc byte[64];
        paramBuf.Clear();

        BinaryPrimitives.WriteInt32LittleEndian(paramBuf[0..4], 64); // size = 64
        BinaryPrimitives.WriteInt32LittleEndian(paramBuf[4..8], 0);  // mode = 0

        // exclude_users_from_login_list[4] at 0x08..0x18
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[(8 + i * 4)..(12 + i * 4)], LOGIN_USER_INVALID);
        }

        // exclude_users_from_logout_list[4] at 0x18..0x28
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(paramBuf[(24 + i * 4)..(28 + i * 4)], LOGIN_USER_INVALID);
        }

        // initial_focus at 0x28..0x2C
        BinaryPrimitives.WriteInt32LittleEndian(paramBuf[40..44], LOGIN_USER_INVALID);

        ctx.Memory.TryWrite(paramPtr, paramBuf);
        return ctx.SetReturn(0);
    }

    // -------------------------------------------------------------------------
    // libSceSigninDialog Exports
    // -------------------------------------------------------------------------

    [SysAbiExport(Nid = "mlYGfmqE3fQ", ExportName = "sceSigninDialogInitialize", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogInitialize(CpuContext ctx)
    {
        if (_signinStatus != SIGNIN_STATUS_NONE)
        {
            return ctx.SetReturn(SIGNIN_ERROR_ALREADY_INITIALIZED);
        }

        _signinStatus = SIGNIN_STATUS_INITIALIZED;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "LXlmS6PvJdU", ExportName = "sceSigninDialogTerminate", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogTerminate(CpuContext ctx)
    {
        if (_signinStatus == SIGNIN_STATUS_NONE)
        {
            return ctx.SetReturn(SIGNIN_ERROR_NOT_INITIALIZED);
        }

        _signinStatus = SIGNIN_STATUS_NONE;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "JlpJVoRWv7U", ExportName = "sceSigninDialogOpen", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogOpen(CpuContext ctx)
    {
        if (_signinStatus != SIGNIN_STATUS_INITIALIZED && _signinStatus != SIGNIN_STATUS_FINISHED)
        {
            return ctx.SetReturn(SIGNIN_ERROR_INVALID_STATE);
        }

        ulong paramPtr = ctx[CpuRegister.Rdi];
        if (paramPtr == 0)
        {
            return ctx.SetReturn(SIGNIN_ERROR_PARAM_INVALID);
        }

        Span<byte> paramBuf = stackalloc byte[16];
        if (!ctx.Memory.TryRead(paramPtr, paramBuf))
        {
            return ctx.SetReturn(SIGNIN_ERROR_PARAM_INVALID);
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(paramBuf[0..4]);
        if (size != 16)
        {
            return ctx.SetReturn(SIGNIN_ERROR_PARAM_INVALID);
        }

        _signinStatus = SIGNIN_STATUS_RUNNING;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "M3OkENHcyiU", ExportName = "sceSigninDialogClose", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogClose(CpuContext ctx)
    {
        if (_signinStatus == SIGNIN_STATUS_NONE)
        {
            return ctx.SetReturn(SIGNIN_ERROR_NOT_INITIALIZED);
        }

        if (_signinStatus != SIGNIN_STATUS_RUNNING && _signinStatus != SIGNIN_STATUS_FINISHED)
        {
            return ctx.SetReturn(SIGNIN_ERROR_INVALID_STATE);
        }

        _signinStatus = SIGNIN_STATUS_FINISHED;
        return ctx.SetReturn(0);
    }

    [SysAbiExport(Nid = "Bw31liTFT3A", ExportName = "sceSigninDialogUpdateStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogUpdateStatus(CpuContext ctx)
    {
        if (_signinStatus == SIGNIN_STATUS_RUNNING)
        {
            _signinStatus = SIGNIN_STATUS_FINISHED;
        }

        return ctx.SetReturn(_signinStatus);
    }

    [SysAbiExport(Nid = "2m077aeC+PA", ExportName = "sceSigninDialogGetStatus", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogGetStatus(CpuContext ctx)
    {
        return ctx.SetReturn(_signinStatus);
    }

    [SysAbiExport(Nid = "nqG7rqnYw1U", ExportName = "sceSigninDialogGetResult", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceSigninDialog")]
    public static int SigninDialogGetResult(CpuContext ctx)
    {
        if (_signinStatus == SIGNIN_STATUS_NONE)
        {
            return ctx.SetReturn(SIGNIN_ERROR_NOT_INITIALIZED);
        }

        if (_signinStatus != SIGNIN_STATUS_FINISHED)
        {
            return ctx.SetReturn(SIGNIN_ERROR_INVALID_STATE);
        }

        ulong resultPtr = ctx[CpuRegister.Rdi];
        if (resultPtr == 0)
        {
            return ctx.SetReturn(SIGNIN_ERROR_PARAM_INVALID);
        }

        Span<byte> resultBuf = stackalloc byte[16];
        resultBuf.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[0..4], SIGNIN_RESULT_USER_CANCELED);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[4..8], 0);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[8..12], 0);
        BinaryPrimitives.WriteInt32LittleEndian(resultBuf[12..16], 0);

        if (!ctx.Memory.TryWrite(resultPtr, resultBuf))
        {
            return ctx.SetReturn(SIGNIN_ERROR_PARAM_INVALID);
        }

        return ctx.SetReturn(0);
    }
}
