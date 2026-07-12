// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using CraziiEmu.Core.Runtime;
using CraziiEmu.Core.Cpu;
using CraziiEmu.HLE;
using CraziiEmu.Logging;
using System.Runtime.InteropServices;
using System.Text;

namespace CraziiEmu.CLI;

internal static partial class Program
{
    private static readonly CraziiEmuLogger Log = CraziiEmuLog.For("CraziiEmu.CLI");
    private const int DefaultImportTraceLimit = 32;
    private static int Main(string[] args)
    {
        Console.Error.WriteLine($"[DEBUG] CraziiEmu starting with {args.Length} args");

        args = WindowsMitigationHelper.NormalizeInternalArguments(args, out var isMitigatedChild);
        if (!isMitigatedChild && WindowsMitigationHelper.TryRunMitigatedChild(args, out var childExitCode))
        {
            return childExitCode;
        }

        if (!TryParseArguments(args, out var ebootPath, out var runtimeOptions, out var logLevel))
        {
            PrintUsage();
            return 1;
        }

        CraziiEmuLog.MinimumLevel = logLevel;

        ebootPath = Path.GetFullPath(ebootPath);
        Console.Error.WriteLine($"[DEBUG] Full path: {ebootPath}");
        
        if (!File.Exists(ebootPath))
        {
            Log.Error($"EBOOT file was not found: {ebootPath}");
            return 2;
        }

        Console.Error.WriteLine("[DEBUG] Creating runtime...");

        using var runtime = CraziiEmuRuntime.CreateDefault(runtimeOptions);

        OrbisGen2Result result;
        try
        {
            Console.Error.WriteLine($"[DEBUG] Running: {ebootPath}");
            result = runtime.Run(ebootPath);
            Console.Error.WriteLine($"[DEBUG] Result: {result}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] Exception: {ex}");
            Log.Error("CraziiEmu failed to run.", ex);
            return 3;
        }

        Log.Info($"CraziiEmu execution completed. Result={result} (0x{(int)result:X8})");
        if (!string.IsNullOrWhiteSpace(runtime.LastSessionSummary))
        {
            Log.Info(runtime.LastSessionSummary);
        }

        if (!string.IsNullOrWhiteSpace(runtime.LastBasicBlockTrace))
        {
            Log.Info("BB trace:");
            Log.Info(runtime.LastBasicBlockTrace);
        }

        if (!string.IsNullOrWhiteSpace(runtime.LastMilestoneLog))
        {
            Log.Info(runtime.LastMilestoneLog);
        }

        if (result != OrbisGen2Result.ORBIS_GEN2_OK && !string.IsNullOrWhiteSpace(runtime.LastExecutionDiagnostics))
        {
            Log.Warn(runtime.LastExecutionDiagnostics);
        }

        if (runtimeOptions.ImportTraceLimit > 0 && !string.IsNullOrWhiteSpace(runtime.LastExecutionTrace))
        {
            Log.Info("Import trace:");
            Log.Info(runtime.LastExecutionTrace);
        }

        return result == OrbisGen2Result.ORBIS_GEN2_OK ? 0 : 4;
    }

    private static bool TryParseArguments(
        string[] args,
        out string ebootPath,
        out CraziiEmuRuntimeOptions runtimeOptions,
        out LogLevel logLevel)
    {
        if (args.Length == 0)
        {
            ebootPath = string.Empty;
            runtimeOptions = default;
            logLevel = CraziiEmuLog.MinimumLevel;
            return false;
        }

        var strictDynlibResolution = false;
        var importTraceLimit = 0;
        var cpuEngine = CpuExecutionEngine.NativeOnly;
        logLevel = CraziiEmuLog.MinimumLevel;
        var pathTokens = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (string.Equals(argument, "--strict", StringComparison.OrdinalIgnoreCase))
            {
                strictDynlibResolution = true;
                continue;
            }

            if (string.Equals(argument, "--trace-imports", StringComparison.OrdinalIgnoreCase))
            {
                importTraceLimit = DefaultImportTraceLimit;
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var explicitLimit))
                {
                    importTraceLimit = Math.Max(0, explicitLimit);
                    i++;
                }

                continue;
            }

            if (string.Equals(argument, "--log-level", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || !CraziiEmuLog.TryParseLevel(args[i + 1], out logLevel))
                {
                    ebootPath = string.Empty;
                    runtimeOptions = default;
                    return false;
                }

                i++;
                continue;
            }

            if (string.Equals(argument, "--cpu-engine", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || !TryParseCpuEngine(args[i + 1], out cpuEngine))
                {
                    ebootPath = string.Empty;
                    runtimeOptions = default;
                    return false;
                }

                i++;
                continue;
            }

            const string logLevelPrefix = "--log-level=";
            if (argument.StartsWith(logLevelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var valueText = argument[logLevelPrefix.Length..];
                if (!CraziiEmuLog.TryParseLevel(valueText, out logLevel))
                {
                    ebootPath = string.Empty;
                    runtimeOptions = default;
                    return false;
                }

                continue;
            }

            const string cpuEnginePrefix = "--cpu-engine=";
            if (argument.StartsWith(cpuEnginePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var valueText = argument[cpuEnginePrefix.Length..];
                if (!TryParseCpuEngine(valueText, out cpuEngine))
                {
                    ebootPath = string.Empty;
                    runtimeOptions = default;
                    logLevel = CraziiEmuLog.MinimumLevel;
                    return false;
                }

                continue;
            }

            const string tracePrefix = "--trace-imports=";
            if (argument.StartsWith(tracePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var valueText = argument[tracePrefix.Length..];
                if (!int.TryParse(valueText, out importTraceLimit))
                {
                    ebootPath = string.Empty;
                    runtimeOptions = default;
                    logLevel = CraziiEmuLog.MinimumLevel;
                    return false;
                }

                importTraceLimit = Math.Max(0, importTraceLimit);
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                ebootPath = string.Empty;
                runtimeOptions = default;
                logLevel = CraziiEmuLog.MinimumLevel;
                return false;
            }

            pathTokens.Add(argument);
        }

        if (pathTokens.Count == 0)
        {
            ebootPath = string.Empty;
            runtimeOptions = default;
            logLevel = CraziiEmuLog.MinimumLevel;
            return false;
        }

        ebootPath = string.Join(' ', pathTokens);
        runtimeOptions = new CraziiEmuRuntimeOptions
        {
            CpuEngine = cpuEngine,
            StrictDynlibResolution = strictDynlibResolution,
            ImportTraceLimit = importTraceLimit,
        };
        return true;
    }

    private static bool TryParseCpuEngine(string valueText, out CpuExecutionEngine engine)
    {
        if (string.Equals(valueText, "native", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(valueText, "native-only", StringComparison.OrdinalIgnoreCase))
        {
            engine = CpuExecutionEngine.NativeOnly;
            return true;
        }

        engine = CpuExecutionEngine.NativeOnly;
        return false;
    }


    private static void PrintUsage()
    {
        Console.WriteLine("Usage: CraziiEmu.CLI [options] <eboot.bin>");
        Console.WriteLine("Options:");
        Console.WriteLine("  --strict           Enable strict dynlib resolution");
        Console.WriteLine("  --trace-imports=N  Limit import tracing (default 32)");
        Console.WriteLine("  --log-level=LEVEL  Set log level (Trace, Debug, Info, Warning, Error)");
        Console.WriteLine("  --cpu-engine=TYPE  Set CPU execution engine (Native)");
    }
}

