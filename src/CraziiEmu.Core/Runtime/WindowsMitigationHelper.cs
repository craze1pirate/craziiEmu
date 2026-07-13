// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace CraziiEmu.Core.Runtime;

public static class WindowsMitigationHelper
{
    public const string MitigatedChildFlag = "--CraziiEmu-mitigated-child";
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint INFINITE = 0xFFFFFFFF;
    private const int PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY = 0x00020007;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_ALWAYS_OFF = 0x00000002UL << 40;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_CET_USER_SHADOW_STACKS_ALWAYS_OFF = 0x00000002UL << 28;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_USER_CET_SET_CONTEXT_IP_VALIDATION_ALWAYS_OFF = 0x00000002UL << 32;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_XTENDED_CONTROL_FLOW_GUARD_ALWAYS_OFF = 0x00000002UL << 40;

    public static string[] NormalizeInternalArguments(string[] args, out bool isMitigatedChild)
    {
        isMitigatedChild = false;
        if (args.Length == 0)
        {
            return args;
        }

        var list = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (string.Equals(arg, MitigatedChildFlag, StringComparison.Ordinal))
            {
                isMitigatedChild = true;
                continue;
            }

            list.Add(arg);
        }

        return list.ToArray();
    }

    public static bool TryRunMitigatedChild(string[] args, out int childExitCode)
    {
        childExitCode = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("CraziiEmu_DISABLE_MITIGATION_RELAUNCH"), "1", StringComparison.Ordinal))
        {
            return false;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var commandLine = Environment.CommandLine + " " + MitigatedChildFlag;
        var startupInfoEx = new STARTUPINFOEX();
        startupInfoEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        nint attributeList = 0;
        nint mitigationPolicies = 0;
        try
        {
            nuint attributeListSize = 0;
            _ = InitializeProcThreadAttributeList(0, 1, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal((nint)attributeListSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                return false;
            }

            startupInfoEx.lpAttributeList = attributeList;

            var policy1 = PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_ALWAYS_OFF;
            var policy2 =
                PROCESS_CREATION_MITIGATION_POLICY2_CET_USER_SHADOW_STACKS_ALWAYS_OFF |
                PROCESS_CREATION_MITIGATION_POLICY2_USER_CET_SET_CONTEXT_IP_VALIDATION_ALWAYS_OFF |
                PROCESS_CREATION_MITIGATION_POLICY2_XTENDED_CONTROL_FLOW_GUARD_ALWAYS_OFF;

            mitigationPolicies = Marshal.AllocHGlobal(sizeof(ulong) * 2);
            Marshal.WriteInt64(mitigationPolicies, unchecked((long)policy1));
            Marshal.WriteInt64(nint.Add(mitigationPolicies, sizeof(long)), unchecked((long)policy2));

            if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                (nint)PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY,
                mitigationPolicies,
                (nuint)(sizeof(ulong) * 2),
                0,
                0))
            {
                return false;
            }

            var cmdLineBuilder = new StringBuilder(commandLine);
            nint jobHandle = 0;
            if (!CreateProcessW(
                processPath,
                cmdLineBuilder,
                0,
                0,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                0,
                Environment.CurrentDirectory,
                ref startupInfoEx,
                out var processInfo))
            {
                return false;
            }

            try
            {
                jobHandle = CreateJobObjectW(0, null);
                if (jobHandle != 0 &&
                    TryEnableKillOnJobClose(jobHandle) &&
                    !AssignProcessToJobObject(jobHandle, processInfo.hProcess))
                {
                    CloseHandle(jobHandle);
                    jobHandle = 0;
                }

                ConsoleCancelEventHandler? cancelHandler = null;
                EventHandler? processExitHandler = null;
                cancelHandler = (_, eventArgs) =>
                {
                    _ = TerminateProcess(processInfo.hProcess, 1);
                    eventArgs.Cancel = true;
                };
                processExitHandler = (_, _) =>
                {
                    _ = TerminateProcess(processInfo.hProcess, 1);
                };
                Console.CancelKeyPress += cancelHandler;
                AppDomain.CurrentDomain.ProcessExit += processExitHandler;

                _ = WaitForSingleObject(processInfo.hProcess, INFINITE);
                Console.CancelKeyPress -= cancelHandler;
                AppDomain.CurrentDomain.ProcessExit -= processExitHandler;

                if (!GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                {
                    return false;
                }

                childExitCode = unchecked((int)exitCode);
                Console.Error.WriteLine("[DEBUG] Running in mitigated child process (CET/CFG disabled).");
                return true;
            }
            finally
            {
                if (jobHandle != 0)
                {
                    CloseHandle(jobHandle);
                }

                if (processInfo.hThread != 0)
                {
                    CloseHandle(processInfo.hThread);
                }

                if (processInfo.hProcess != 0)
                {
                    CloseHandle(processInfo.hProcess);
                }
            }
        }
        finally
        {
            if (attributeList != 0)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (mitigationPolicies != 0)
            {
                Marshal.FreeHGlobal(mitigationPolicies);
            }
        }
    }

    private static string BuildCommandLine(string processPath, IReadOnlyList<string> args)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteArgument(processPath));
        for (var i = 0; i < args.Count; i++)
        {
            builder.Append(' ');
            builder.Append(QuoteArgument(args[i]));
        }

        return builder.ToString();
    }

    private static bool TryEnableKillOnJobClose(nint jobHandle)
    {
        var extendedLimitInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(extendedLimitInfo, memory, false);
            return SetInformationJobObject(
                jobHandle,
                JobObjectExtendedLimitInformation,
                memory,
                unchecked((uint)size));
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static string QuoteArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = false;
        foreach (var c in argument)
        {
            if (char.IsWhiteSpace(c) || c == '"')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        builder.Append(argument);
        builder.Replace("\"", "\\\"");
        builder.Append('"');
        return builder.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InitializeProcThreadAttributeList(
        nint lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateProcThreadAttribute(
        nint lpAttributeList,
        uint dwFlags,
        nint attribute,
        nint lpValue,
        nuint cbSize,
        nint lpPreviousValue,
        nint lpReturnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetInformationJobObject(
        nint hJob,
        int jobObjectInfoClass,
        nint lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessW(
        string applicationName,
        StringBuilder commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        ref STARTUPINFOEX startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetExitCodeProcess(nint process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);
}




