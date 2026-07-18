// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core.Runtime;
using CraziiEmu.Core.Cpu;
using CraziiEmu.HLE;
using CraziiEmu.Libs.VideoOut;
using CraziiEmu.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CraziiEmu.Core.Runtime;

public static class WindowsMitigationHelper
{
    private static readonly CraziiEmuLogger Log = CraziiEmuLog.For("CraziiEmu.CLI");
    private static readonly object ConsoleMirrorSync = new();
    private static StreamWriter? _consoleMirrorFile;
    private const int DefaultImportTraceLimit = 32;
    private const string MitigatedChildFlag = "--sharpemu-mitigated-child";
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint INFINITE = 0xFFFFFFFF;
    private const int PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY = 0x00020007;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const string MitigatedChildEnvironment = "CRAZIIEMU_MITIGATED_CHILD";
    private const ulong PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_ALWAYS_OFF = 0x00000002UL << 40;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_CET_USER_SHADOW_STACKS_ALWAYS_OFF = 0x00000002UL << 28;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_USER_CET_SET_CONTEXT_IP_VALIDATION_ALWAYS_OFF = 0x00000002UL << 32;
    private const ulong PROCESS_CREATION_MITIGATION_POLICY2_XTENDED_CONTROL_FLOW_GUARD_ALWAYS_OFF = 0x00000002UL << 40;
    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    /// <summary>
    /// The supported host execution model, checked before any emulation
    /// starts: the CPU backend executes guest x86-64 code natively, so the
    /// host process must be x86-64 — win-x64/linux-x64 on x64 hardware, or
    /// osx-x64 under Rosetta 2 on Apple Silicon (Rosetta translates the
    /// whole process, so it still reports as X64 here). An arm64 process
    /// (e.g. the osx-arm64 build) can browse the GUI but cannot run games;
    /// failing up front distinguishes that from MoltenVK, signal-handler,
    /// or guest-memory startup problems.
    /// </summary>
    private static bool CheckHostArchitecture()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return true;
        }

        Console.Error.WriteLine(
            $"[LOADER][ERROR] Unsupported process architecture " +
            $"{RuntimeInformation.ProcessArchitecture}: guest code executes " +
            "natively, so CraziiEmu must run as an x86-64 process.");
        if (OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine(
                "[LOADER][ERROR] On Apple Silicon, use the osx-x64 build under " +
                "Rosetta 2 (install with: softwareupdate --install-rosetta).");
        }

        return false;
    }

    /// <summary>
    /// Applies MoltenVK performance defaults before the Vulkan loader is
    /// loaded. Existing user-provided values always take precedence.
    /// </summary>
    private static void ConfigureMoltenVkDefaults()
    {
        try
        {
            _ = MacSetEnv("MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS", "0", 0);
            _ = MacSetEnv("MVK_CONFIG_SHOULD_MAXIMIZE_CONCURRENT_COMPILATION", "1", 0);
            _ = MacSetEnv("MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS", "1", 0);
            _ = MacSetEnv("MVK_CONFIG_RESUME_LOST_DEVICE", "1", 0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[LOADER][WARN] Failed to set MoltenVK defaults: {exception.Message}");
        }
    }

    /// <summary>
    /// Makes a Vulkan loader visible to GLFW's dlopen("libvulkan.1.dylib").
    /// Homebrew's Vulkan libraries are arm64-only and cannot load into this
    /// x86-64 (Rosetta 2) process, so a universal libMoltenVK.dylib placed
    /// next to the executable (named libvulkan.1.dylib) is preloaded here;
    /// dyld then resolves GLFW's bare-name dlopen to the loaded image.
    /// </summary>
    private static void PreloadMacVulkanLoader()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "libvulkan.1.dylib"),
            Path.Combine(AppContext.BaseDirectory, "libMoltenVK.dylib"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sharpemu", "x64lib", "libvulkan.1.dylib"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out _))
            {
                Console.Error.WriteLine($"[LOADER][INFO] Vulkan loader preloaded: {candidate}");
                return;
            }
        }

        if (NativeLibrary.TryLoad("libvulkan.1.dylib", out _))
        {
            return;
        }

        Console.Error.WriteLine(
            "[LOADER][WARN] No x86-64 Vulkan loader found; video output will be unavailable. " +
            "Place a universal libMoltenVK.dylib (from the MoltenVK releases) next to CraziiEmu " +
            "as libvulkan.1.dylib.");
    }

    /// <summary>
    /// Makes console writes UTF-8 so the GUI's pipe reader (and any modern
    /// terminal) decodes non-ASCII text correctly. Without this, redirected
    /// output falls back to the OS ANSI code page and characters like "—"
    /// arrive mangled.
    /// </summary>
    private static void UseUtf8ConsoleOutput()
    {
        try
        {
            // Also recreates the redirected Console.Out/Error writers with
            // the new encoding.
            Console.OutputEncoding = Encoding.UTF8;
            return;
        }
        catch (Exception)
        {
            // No attached console (GUI-subsystem child with piped output):
            // wrap the raw handles instead.
        }

        if (Console.IsOutputRedirected)
        {
            Console.SetOut(new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            });
        }

        if (Console.IsErrorRedirected)
        {
            Console.SetError(new StreamWriter(
                Console.OpenStandardError(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            });
        }
    }

    private static void RebindStdHandleToConsole(int stdHandle)
    {
        if (IsHandleValid(GetStdHandle(stdHandle)) || GetConsoleWindow() == 0)
        {
            return;
        }

        var conOut = CreateFileW(
            "CONOUT$",
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            0,
            OPEN_EXISTING,
            0,
            0);
        if (IsHandleValid(conOut))
        {
            _ = SetStdHandle(stdHandle, conOut);
        }
    }

    private static bool IsHandleValid(nint handle)
    {
        return handle != 0 && handle != -1;
    }

    public static string[] NormalizeInternalArguments(string[] args, out bool isMitigatedChild)
    {
        isMitigatedChild = false;
        var trustedMitigatedChild = string.Equals(
            Environment.GetEnvironmentVariable(MitigatedChildEnvironment),
            "1",
            StringComparison.Ordinal);
        if (args.Length == 0)
        {
            return args;
        }

        var list = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (string.Equals(arg, MitigatedChildFlag, StringComparison.Ordinal))
            {
                isMitigatedChild = trustedMitigatedChild;
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
        ConfigureInheritedStdHandles(ref startupInfoEx.StartupInfo);

        nint attributeList = 0;
        nint mitigationPolicies = 0;
        var previousChildEnvironment = Environment.GetEnvironmentVariable(MitigatedChildEnvironment);
        try
        {
            nuint attributeListSize = 0;
            _ = InitializeProcThreadAttributeList(0, 1, 0, ref attributeListSize);
            attributeList = Marshal.AllocHGlobal((nint)attributeListSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                childExitCode = 5;
                Console.Error.WriteLine($"[ERROR] Failed to initialize mitigation attributes: {Marshal.GetLastWin32Error()}");
                return true;
            }

            startupInfoEx.lpAttributeList = attributeList;

            var policy1 = PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_ALWAYS_OFF;
            var policy2 =
                PROCESS_CREATION_MITIGATION_POLICY2_CET_USER_SHADOW_STACKS_ALWAYS_OFF |
                PROCESS_CREATION_MITIGATION_POLICY2_USER_CET_SET_CONTEXT_IP_VALIDATION_ALWAYS_OFF;

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
                childExitCode = 5;
                Console.Error.WriteLine($"[ERROR] Failed to apply mitigation attributes: {Marshal.GetLastWin32Error()}");
                return true;
            }

            var cmdLineBuilder = new StringBuilder(commandLine);
            nint jobHandle = 0;
            Environment.SetEnvironmentVariable(MitigatedChildEnvironment, "1");
            var created = CreateProcessW(
                processPath,
                cmdLineBuilder,
                0,
                0,
                true,
                EXTENDED_STARTUPINFO_PRESENT,
                0,
                Environment.CurrentDirectory,
                ref startupInfoEx,
                out var processInfo);
            Environment.SetEnvironmentVariable(MitigatedChildEnvironment, previousChildEnvironment);
            if (!created)
            {
                childExitCode = 5;
                Console.Error.WriteLine($"[ERROR] Failed to launch mitigated child process: {Marshal.GetLastWin32Error()}");
                return true;
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
            Environment.SetEnvironmentVariable(MitigatedChildEnvironment, previousChildEnvironment);

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

    private static bool TryGetLogFileArgument(IReadOnlyList<string> args, out string path)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (string.Equals(argument, "--log-file", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count &&
                    !string.IsNullOrWhiteSpace(args[i + 1]) &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal) &&
                    ShouldConsumeLogFilePath(args, i + 1))
                {
                    path = args[i + 1];
                    return true;
                }

                path = BuildDefaultLogFilePath(TryFindEbootPathToken(args));
                return true;
            }

            const string logFilePrefix = "--log-file=";
            if (argument.StartsWith(logFilePrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(argument[logFilePrefix.Length..]))
            {
                path = argument[logFilePrefix.Length..];
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string BuildDefaultLogFilePath(string? ebootPath)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var logsDirectory = Path.Combine(baseDirectory, "user", "logs");
        var name = TryReadTitleId(ebootPath) ?? "UNKNOWN";

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return Path.Combine(logsDirectory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    private static string? TryReadTitleId(string? ebootPath)
    {
        if (string.IsNullOrWhiteSpace(ebootPath))
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(ebootPath));
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            foreach (var paramPath in new[]
            {
                Path.Combine(directory, "sce_sys", "param.json"),
                Path.Combine(directory, "param.json"),
            })
            {
                if (!File.Exists(paramPath))
                {
                    continue;
                }

                using var stream = File.OpenRead(paramPath);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("titleId", out var titleIdElement) &&
                    titleIdElement.ValueKind == JsonValueKind.String)
                {
                    var titleId = titleIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(titleId))
                    {
                        return titleId.Trim();
                    }
                }
            }
        }
        catch
        {
            // Logging should never block launch; unknown title ids use a stable fallback.
        }

        return null;
    }

    private static string? TryFindEbootPathToken(IReadOnlyList<string> args)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            var argument = args[i];
            if (string.IsNullOrWhiteSpace(argument) ||
                argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            return argument;
        }

        return null;
    }

    private static bool ShouldConsumeLogFilePath(IReadOnlyList<string> args, int candidateIndex)
    {
        var candidate = args[candidateIndex];
        if (LooksLikeLogFilePath(candidate))
        {
            return true;
        }

        for (var i = candidateIndex + 1; i < args.Count; i++)
        {
            var argument = args[i];
            if (!string.IsNullOrWhiteSpace(argument) &&
                !argument.StartsWith("--", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeLogFilePath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryEnableConsoleFileMirror(string path)
    {
        lock (ConsoleMirrorSync)
        {
            if (_consoleMirrorFile is not null)
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);
                _consoleMirrorFile = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true,
                };

                Console.SetOut(new TeeTextWriter(Console.Out, _consoleMirrorFile));
                Console.SetError(new TeeTextWriter(Console.Error, _consoleMirrorFile));
                Console.Error.WriteLine($"[DEBUG] Log file: {Path.GetFullPath(path)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Could not open log file '{path}': {ex.Message}");
            }
        }
    }

    private static void DropConsoleFileMirror()
    {
        lock (ConsoleMirrorSync)
        {
            try
            {
                _consoleMirrorFile?.Flush();
                _consoleMirrorFile?.Dispose();
            }
            catch
            {
            }

            _consoleMirrorFile = null;
        }
    }

    private static void ConfigureInheritedStdHandles(ref STARTUPINFO startupInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var input = GetStdHandle(STD_INPUT_HANDLE);
        var output = GetStdHandle(STD_OUTPUT_HANDLE);
        var error = GetStdHandle(STD_ERROR_HANDLE);
        if (!IsHandleValid(output) && !IsHandleValid(error))
        {
            return;
        }

        if (IsHandleValid(input))
        {
            _ = SetHandleInformation(input, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
            startupInfo.hStdInput = input;
        }

        if (IsHandleValid(output))
        {
            _ = SetHandleInformation(output, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
            startupInfo.hStdOutput = output;
        }

        if (IsHandleValid(error))
        {
            _ = SetHandleInformation(error, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);
            startupInfo.hStdError = error;
        }

        startupInfo.dwFlags |= STARTF_USESTDHANDLES;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int stdHandle, nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(nint handle, uint mask, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("libSystem", EntryPoint = "setenv")]
    private static extern int MacSetEnv(string name, string value, int overwrite);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
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
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        nint lpAttributeList,
        uint dwAttributeCount,
        uint dwFlags,
        ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        nint lpAttributeList,
        uint dwFlags,
        nint Attribute,
        nint lpValue,
        nuint cbSize,
        nint lpPreviousValue,
        nint lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        int JobObjectInformationClass,
        nint lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
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
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _first;
        private readonly TextWriter _second;

        public TeeTextWriter(TextWriter first, TextWriter second)
        {
            _first = first;
            _second = second;
        }

        public override Encoding Encoding => _first.Encoding;

        public override void Write(char value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void Write(string? value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            _first.Write(buffer, index, count);
            _second.Write(buffer, index, count);
        }

        public override void Flush()
        {
            _first.Flush();
            _second.Flush();
        }
    }
}




