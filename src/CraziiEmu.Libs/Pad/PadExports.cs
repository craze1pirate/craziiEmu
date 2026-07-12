// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.HLE;
using CraziiEmu.HLE.Input;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CraziiEmu.Libs.Pad;

public static class PadExports
{
    private const int OrbisPadErrorInvalidHandle = unchecked((int)0x80920003);
    private const int OrbisPadErrorNotInitialized = unchecked((int)0x80920005);
    private const int OrbisPadErrorDeviceNotConnected = unchecked((int)0x80920007);
    private const int OrbisPadErrorDeviceNoHandle = unchecked((int)0x80920008);
    private const int PrimaryUserId = 1;
    private const int StandardPortType = 0;
    private const int PrimaryPadHandle = 1;
    private const int ControllerInformationSize = 0x1C;
    private const int PadDataSize = 0x78;

    private static bool _initialized;

    [SysAbiExport(
        Nid = "hv1luiJrqQM",
        ExportName = "scePadInit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadInit(CpuContext ctx)
    {
        _initialized = true;
        return SetReturn(ctx, 0);
    }

    [SysAbiExport(
        Nid = "xk0AcarP3V4",
        ExportName = "scePadOpen",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadOpen(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var type = unchecked((int)ctx[CpuRegister.Rsi]);
        var index = unchecked((int)ctx[CpuRegister.Rdx]);
        var parameterAddress = ctx[CpuRegister.Rcx];
        if (!_initialized)
        {
            return SetReturn(ctx, OrbisPadErrorNotInitialized);
        }

        if (userId == -1)
        {
            return SetReturn(ctx, OrbisPadErrorDeviceNoHandle);
        }

        if (userId != PrimaryUserId || type != StandardPortType || index != 0 || parameterAddress != 0)
        {
            return SetReturn(ctx, OrbisPadErrorDeviceNotConnected);
        }

        Console.Error.WriteLine("[LOADER][INFO] Keyboard controls: Arrow keys = D-pad, WASD = left stick, IJKL = right stick, Z/Enter = Cross, X/Esc = Circle, C = Square, V = Triangle, Q = L1, E = R1, R = L2, F = R2, Tab/Backspace = Options");
        return SetReturn(ctx, PrimaryPadHandle);
    }

    [SysAbiExport(
        Nid = "clVvL4ZDntw",
        ExportName = "scePadSetMotionSensorState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadSetMotionSensorState(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        return handle == PrimaryPadHandle
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, OrbisPadErrorInvalidHandle);
    }

    [SysAbiExport(
        Nid = "gjP9-KQzoUk",
        ExportName = "scePadGetControllerInformation",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadGetControllerInformation(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        var informationAddress = ctx[CpuRegister.Rsi];
        if (handle != PrimaryPadHandle)
        {
            return SetReturn(ctx, OrbisPadErrorInvalidHandle);
        }

        if (informationAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> information = stackalloc byte[ControllerInformationSize];
        BinaryPrimitives.WriteSingleLittleEndian(information[0x00..], 44.86f);
        BinaryPrimitives.WriteUInt16LittleEndian(information[0x04..], 1920);
        BinaryPrimitives.WriteUInt16LittleEndian(information[0x06..], 943);
        information[0x08] = 30;
        information[0x09] = 30;
        information[0x0A] = StandardPortType;
        information[0x0B] = 1;
        information[0x0C] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(information[0x10..], 0);

        return ctx.Memory.TryWrite(informationAddress, information)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "YndgXqQVV7c",
        ExportName = "scePadReadState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadReadState(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        var dataAddress = ctx[CpuRegister.Rsi];
        if (handle != PrimaryPadHandle)
        {
            return SetReturn(ctx, OrbisPadErrorInvalidHandle);
        }

        if (dataAddress == 0)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        return WriteNeutralPadData(ctx, dataAddress)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "q1cHNfGycLI",
        ExportName = "scePadRead",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libScePad")]
    public static int PadRead(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        var dataAddress = ctx[CpuRegister.Rsi];
        var count = unchecked((int)ctx[CpuRegister.Rdx]);
        if (handle != PrimaryPadHandle)
        {
            return SetReturn(ctx, OrbisPadErrorInvalidHandle);
        }

        if (dataAddress == 0 || count < 1 || count > 64)
        {
            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        return WriteNeutralPadData(ctx, dataAddress)
            ? SetReturn(ctx, 1)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    private static uint MapGamepadButtons(ushort x)
    {
        uint b = 0;
        if ((x & GamepadHandler.DPAD_UP) != 0) b |= 0x0010;
        if ((x & GamepadHandler.DPAD_DOWN) != 0) b |= 0x0040;
        if ((x & GamepadHandler.DPAD_LEFT) != 0) b |= 0x0080;
        if ((x & GamepadHandler.DPAD_RIGHT) != 0) b |= 0x0020;
        if ((x & GamepadHandler.START) != 0) b |= 0x0008; // Options
        if ((x & GamepadHandler.BACK) != 0) b |= 0x0001; // Create
        if ((x & GamepadHandler.LEFT_THUMB) != 0) b |= 0x0002; // L3
        if ((x & GamepadHandler.RIGHT_THUMB) != 0) b |= 0x0004; // R3
        if ((x & GamepadHandler.LEFT_SHOULDER) != 0) b |= 0x0400; // L1
        if ((x & GamepadHandler.RIGHT_SHOULDER) != 0) b |= 0x0800; // R1
        if ((x & GamepadHandler.BTN_A) != 0) b |= 0x4000; // Cross
        if ((x & GamepadHandler.BTN_B) != 0) b |= 0x2000; // Circle
        if ((x & GamepadHandler.BTN_X) != 0) b |= 0x8000; // Square
        if ((x & GamepadHandler.BTN_Y) != 0) b |= 0x1000; // Triangle
        return b;
    }

    private static bool WriteNeutralPadData(CpuContext ctx, ulong dataAddress)
    {
        Span<byte> data = stackalloc byte[PadDataSize];
        data.Clear();
        
        var acceptsKeyboardInput = IsEmulatorWindowFocused();
        uint buttons = acceptsKeyboardInput ? ReadKeyboardButtons() : 0;
        
        byte leftX = 128, leftY = 128, rightX = 128, rightY = 128;
        byte l2 = 0, r2 = 0;

        var input = CraziiEmu.HLE.Configuration.CraziiEmuConfig.Instance.Input;

        if (acceptsKeyboardInput)
        {
            leftX = ReadAnalogStick(IsKeyDown(input.LeftStickLeft), IsKeyDown(input.LeftStickRight));
            leftY = ReadAnalogStick(IsKeyDown(input.LeftStickUp), IsKeyDown(input.LeftStickDown));
            rightX = ReadAnalogStick(IsKeyDown(input.RightStickLeft), IsKeyDown(input.RightStickRight));
            rightY = ReadAnalogStick(IsKeyDown(input.RightStickUp), IsKeyDown(input.RightStickDown));
            l2 = IsKeyDown(input.L2) ? (byte)255 : (byte)0;
            r2 = IsKeyDown(input.R2) ? (byte)255 : (byte)0;
        }

        // Merge Gamepad Input
        if (GamepadHandler.GetState(out var pad))
        {
            buttons |= MapGamepadButtons(pad.wButtons);
            
            // XInput analog to PS4 analog
            byte padLeftX = (byte)((pad.sThumbLX + 32768) >> 8);
            byte padLeftY = (byte)(255 - ((pad.sThumbLY + 32768) >> 8));
            byte padRightX = (byte)((pad.sThumbRX + 32768) >> 8);
            byte padRightY = (byte)(255 - ((pad.sThumbRY + 32768) >> 8));

            // Override keyboard analog if gamepad is deflected outside deadzone (~12%)
            if (Math.Abs(pad.sThumbLX) > 4000 || Math.Abs(pad.sThumbLY) > 4000)
            {
                leftX = padLeftX;
                leftY = padLeftY;
            }
            if (Math.Abs(pad.sThumbRX) > 4000 || Math.Abs(pad.sThumbRY) > 4000)
            {
                rightX = padRightX;
                rightY = padRightY;
            }

            if (pad.bLeftTrigger > 0)
            {
                l2 = pad.bLeftTrigger;
                buttons |= 0x0100;
            }
            if (pad.bRightTrigger > 0)
            {
                r2 = pad.bRightTrigger;
                buttons |= 0x0200;
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data[0x00..], buttons);
        data[0x04] = leftX;
        data[0x05] = leftY;
        data[0x06] = rightX;
        data[0x07] = rightY;
        data[0x08] = l2;
        data[0x09] = r2;
        
        BinaryPrimitives.WriteSingleLittleEndian(data[0x18..], 1.0f);
        data[0x4C] = 1;
        var timestampTicks = Stopwatch.GetTimestamp();
        var timestampMicroseconds =
            ((ulong)(timestampTicks / Stopwatch.Frequency) * 1_000_000UL) +
            ((ulong)(timestampTicks % Stopwatch.Frequency) * 1_000_000UL / (ulong)Stopwatch.Frequency);
        BinaryPrimitives.WriteUInt64LittleEndian(
            data[0x50..],
            timestampMicroseconds);
        data[0x68] = 1;

        return ctx.Memory.TryWrite(dataAddress, data);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    private static bool IsKeyDown(int vk) =>
        (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsEmulatorWindowFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static uint ReadKeyboardButtons()
    {
        uint buttons = 0;
        var input = CraziiEmu.HLE.Configuration.CraziiEmuConfig.Instance.Input;

        // D-pad
        if (IsKeyDown(input.DpadLeft)) buttons |= 0x0080;
        if (IsKeyDown(input.DpadRight)) buttons |= 0x0020;
        if (IsKeyDown(input.DpadUp)) buttons |= 0x0010;
        if (IsKeyDown(input.DpadDown)) buttons |= 0x0040;
        
        // Face buttons
        if (IsKeyDown(input.Cross)) buttons |= 0x4000;
        if (IsKeyDown(input.Circle)) buttons |= 0x2000;
        if (IsKeyDown(input.Square)) buttons |= 0x8000;
        if (IsKeyDown(input.Triangle)) buttons |= 0x1000;
        
        // Shoulder buttons
        if (IsKeyDown(input.L1)) buttons |= 0x0400;
        if (IsKeyDown(input.R1)) buttons |= 0x0800;
        if (IsKeyDown(input.L2)) buttons |= 0x0100;
        if (IsKeyDown(input.R2)) buttons |= 0x0200;
        
        // Stick Clicks
        if (IsKeyDown(input.L3)) buttons |= 0x0002;
        if (IsKeyDown(input.R3)) buttons |= 0x0004;

        // System
        if (IsKeyDown(input.Options)) buttons |= 0x0008;
        if (IsKeyDown(input.Create)) buttons |= 0x0001;
        
        return buttons;
    }

    private static byte ReadAnalogStick(bool negative, bool positive)
    {
        if (negative && !positive) return 0;
        if (positive && !negative) return 255;
        return 128;
    }
}
