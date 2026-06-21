// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SharpEmu.Core.Cpu;
using SharpEmu.HLE;

namespace SharpEmu.Core.Cpu.Native;

public sealed partial class DirectExecutionBackend
{
	private static ulong ImportDispatchGatewayManaged(nint backendHandle, int importIndex, nint argPackPtr)
	{
		try
		{
			if (!(GCHandle.FromIntPtr(backendHandle).Target is DirectExecutionBackend directExecutionBackend))
			{
				Console.Error.WriteLine(
					$"[LOADER][ERROR] ImportDispatchGatewayManaged: invalid backend handle 0x{backendHandle:X16}");
				return 18446744071562199042uL;
			}

			return directExecutionBackend.DispatchImport(importIndex, argPackPtr);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(
				$"[LOADER][ERROR] ImportDispatchGatewayManaged exception: {ex.GetType().Name}: {ex.Message}");
			return 18446744071562199298uL;
		}
	}

	private unsafe static int RawVectoredHandlerManaged(void* exceptionInfo)
	{
		return TryRecoverUnresolvedSentinel(exceptionInfo);
	}

	private unsafe static int RawUnhandledFilterManaged(void* exceptionInfo)
	{
		return TryRecoverUnresolvedSentinel(exceptionInfo);
	}

	private unsafe static int TryRecoverUnresolvedSentinel(void* exceptionInfo)
	{
		EXCEPTION_RECORD* exceptionRecord = ((EXCEPTION_POINTERS*)exceptionInfo)->ExceptionRecord;
		if (exceptionRecord->ExceptionCode != 3221225477u)
		{
			return 0;
		}
		void* contextRecord = ((EXCEPTION_POINTERS*)exceptionInfo)->ContextRecord;
		ulong value = ReadCtxU64(contextRecord, 248);
		ulong value2 = (ulong)exceptionRecord->ExceptionAddress;
		if (!IsUnresolvedSentinel(value) && !IsUnresolvedSentinel(value2))
		{
			return 0;
		}
		ulong rsp = ReadCtxU64(contextRecord, 152);
		WriteCtxU64(contextRecord, 120, 0uL);
		if (TryGetPlausibleReturnFromStack(rsp, out var returnRip, out var nextRsp))
		{
			WriteCtxU64(contextRecord, 152, nextRsp);
			WriteCtxU64(contextRecord, 248, returnRip);
			Interlocked.Increment(ref _rawSentinelRecoveries);
			return -1;
		}
		return 0;
	}

	private unsafe ulong DispatchImport(int importIndex, nint argPackPtr)
	{
		long num = Interlocked.Increment(ref _importDispatchCount);
		MarkExecutionProgress();
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null)
		{
			LastError = "Import dispatch called without active CPU context";
			return 18446744071562199298uL;
		}
		if ((uint)importIndex >= (uint)_importEntries.Length)
		{
			LastError = $"Import dispatch index out of range: {importIndex}";
			return 18446744071562199042uL;
		}
		ImportStubEntry importStubEntry = _importEntries[importIndex];
		int num2 = Volatile.Read(in _rawSentinelRecoveries);
		if (num2 != _lastReportedRawSentinelRecoveries)
		{
			Console.Error.WriteLine($"[LOADER][TRACE] Raw sentinel recoveries: {num2} (last import index={importIndex})");
			_lastReportedRawSentinelRecoveries = num2;
		}
		cpuContext.Rip = importStubEntry.Address;
		cpuContext[CpuRegister.Rdi] = *(ulong*)argPackPtr;
		cpuContext[CpuRegister.Rsi] = *(ulong*)(argPackPtr + 8);
		cpuContext[CpuRegister.Rdx] = *(ulong*)(argPackPtr + 16);
		cpuContext[CpuRegister.Rcx] = *(ulong*)(argPackPtr + 24);
		cpuContext[CpuRegister.R8] = *(ulong*)(argPackPtr + 32);
		cpuContext[CpuRegister.R9] = *(ulong*)(argPackPtr + 40);
		cpuContext[CpuRegister.Rbx] = *(ulong*)(argPackPtr + 48);
		cpuContext[CpuRegister.Rbp] = *(ulong*)(argPackPtr + 56);
		cpuContext[CpuRegister.R12] = *(ulong*)(argPackPtr + 64);
		cpuContext[CpuRegister.R13] = *(ulong*)(argPackPtr + 72);
		cpuContext[CpuRegister.R14] = *(ulong*)(argPackPtr + 80);
		cpuContext[CpuRegister.R15] = *(ulong*)(argPackPtr + 88);
		cpuContext[CpuRegister.Rsp] = (ulong)argPackPtr + 96uL;
		ulong value = cpuContext[CpuRegister.Rdi];
		ulong value2 = cpuContext[CpuRegister.Rsi];
		ulong num3 = cpuContext[CpuRegister.Rdx];
		ulong num4 = cpuContext[CpuRegister.Rcx];
		ulong num5 = cpuContext[CpuRegister.R8];
		ulong num6 = cpuContext[CpuRegister.R9];
		ulong value3 = cpuContext[CpuRegister.Rbx];
		ulong value4 = cpuContext[CpuRegister.Rbp];
		ulong value5 = cpuContext[CpuRegister.R12];
		ulong value6 = cpuContext[CpuRegister.R13];
		ulong value7 = cpuContext[CpuRegister.R14];
		ulong value8 = cpuContext[CpuRegister.R15];
		ulong num7 = *(ulong*)(argPackPtr + 96);
		if (!IsLikelyReturnAddress(num7))
		{
			for (int i = 1; i <= 4; i++)
			{
				ulong num8 = *(ulong*)(argPackPtr + 96 + i * 8);
				if (IsLikelyReturnAddress(num8))
				{
					*(ulong*)(argPackPtr + 96) = num8;
					num7 = num8;
					Console.Error.WriteLine($"[LOADER][WARNING] Import#{num}: corrected suspicious return RIP using stack slot +0x{i * 8:X} -> 0x{num7:X16}");
					break;
				}
			}
		}
		TrackDistinctImportNid(importStubEntry.Nid);
		var probeImportReturn = Environment.GetEnvironmentVariable("SHARPEMU_PROBE_IMPORT_RET");
		if (!string.IsNullOrWhiteSpace(probeImportReturn) &&
			(string.Equals(probeImportReturn, "*", StringComparison.Ordinal) ||
			 string.Equals(probeImportReturn, importStubEntry.Nid, StringComparison.Ordinal)))
		{
			ProbeReturnRip(num7, num);
		}
		TrackStrlenPrelude(importStubEntry.Nid, num, num7);
		bool logBootstrap = string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_BOOTSTRAP"), "1", StringComparison.Ordinal);
		if (logBootstrap && string.Equals(importStubEntry.Nid, RuntimeStubNids.BootstrapBridge, StringComparison.Ordinal))
		{
			string symbolText = "<unreadable>";
			if (TryReadAsciiZ(value2, 256, out var sym))
			{
				symbolText = sym;
			}
			Console.Error.WriteLine(
				$"[LOADER][TRACE] bootstrap_call#{num}: op=0x{value:X16} sym_ptr=0x{value2:X16} sym='{symbolText}' out_ptr=0x{num3:X16} ret=0x{num7:X16}");
		}
		if (!ActiveForcedGuestExit && ShouldForceGuestExitOnImportLoop(importStubEntry.Nid, num7, num, value, value2) && TryForceGuestExitToHostStub(argPackPtr, num, num7, importStubEntry.Nid))
		{
			cpuContext[CpuRegister.Rax] = 1uL;
			return 1uL;
		}
		bool flag0 = ShouldSuppressStrlenTrace(importStubEntry.Nid);
		bool flag = num7 >= 2156221920u && num7 <= 2156225024u;
		bool flag2 = num7 >= 2156351360u && num7 <= 2156352080u;
		bool flag3 = num >= 1020 && num <= 1040;
		bool logAllImports = string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_ALL_IMPORTS"), "1", StringComparison.Ordinal);
		string importFilter = Environment.GetEnvironmentVariable("SHARPEMU_LOG_IMPORT_FILTER");
		bool flag4 = !string.IsNullOrWhiteSpace(importFilter);
		bool flag5 = false;
		ExportedFunction matchedExport = null;
		if (_moduleManager.TryGetExport(importStubEntry.Nid, out ExportedFunction export))
		{
			matchedExport = export;
			if (flag4)
			{
				flag5 = export.LibraryName.Contains(importFilter, StringComparison.OrdinalIgnoreCase)
					|| export.Name.Contains(importFilter, StringComparison.OrdinalIgnoreCase)
					|| importStubEntry.Nid.Contains(importFilter, StringComparison.OrdinalIgnoreCase);
			}
		}
		else if (flag4)
		{
			flag5 = importStubEntry.Nid.Contains(importFilter, StringComparison.OrdinalIgnoreCase);
		}
		bool flag6 = logAllImports || flag5;
		if (!flag0 && (flag6 || num <= 128 || (num >= 240 && num <= 400) || (num >= 900 && num <= 1300) || num % 100000 == 0L || (importStubEntry.Nid == "tsvEmnenz48" && (num <= 256 || num % 1000 == 0L)) || (importStubEntry.Nid == "rTXw65xmLIA" && (num <= 256 || num % 128 == 0)) || flag || flag2 || flag3))
		{
			if (matchedExport != null)
			{
				if (flag6)
				{
					Console.Error.WriteLine(
						$"[LOADER][TRACE] Import#{num}: {matchedExport.LibraryName}:{matchedExport.Name} ({importStubEntry.Nid}) " +
						$"rdi=0x{value:X16} rsi=0x{value2:X16} rdx=0x{num3:X16} rcx=0x{num4:X16} ret=0x{num7:X16}");
				}
				else
				{
					Console.Error.WriteLine($"[LOADER][TRACE] Import#{num}: {matchedExport.LibraryName}:{matchedExport.Name} ({importStubEntry.Nid})");
				}
			}
			else
			{
				if (flag6)
				{
					Console.Error.WriteLine(
						$"[LOADER][TRACE] Import#{num}: {importStubEntry.Nid} " +
						$"rdi=0x{value:X16} rsi=0x{value2:X16} rdx=0x{num3:X16} rcx=0x{num4:X16} ret=0x{num7:X16}");
				}
				else
				{
					Console.Error.WriteLine($"[LOADER][TRACE] Import#{num}: {importStubEntry.Nid}");
				}
			}
			if (flag6)
			{
				Console.Error.Flush();
			}
		}
		if (!flag0)
		{
			RecordRecentImportTrace($"#{num} nid={importStubEntry.Nid} ret=0x{num7:X16} rdi=0x{cpuContext[CpuRegister.Rdi]:X16} rsi=0x{cpuContext[CpuRegister.Rsi]:X16} rdx=0x{cpuContext[CpuRegister.Rdx]:X16}");
		}
		if (importStubEntry.Nid == "8zTFvBIAIN8" && num <= 256)
		{
			Console.Error.WriteLine($"[LOADER][TRACE] memset#{num}: dst=0x{cpuContext[CpuRegister.Rdi]:X16} val=0x{cpuContext[CpuRegister.Rsi] & 0xFF:X2} len=0x{cpuContext[CpuRegister.Rdx]:X16} ret=0x{num7:X16}");
		}
		if (importStubEntry.Nid == "tsvEmnenz48" && num <= 64)
		{
			Console.Error.WriteLine($"[LOADER][TRACE] __cxa_atexit#{num}: func=0x{cpuContext[CpuRegister.Rdi]:X16} arg=0x{cpuContext[CpuRegister.Rsi]:X16} dso=0x{cpuContext[CpuRegister.Rdx]:X16} ret=0x{num7:X16}");
		}
		if (importStubEntry.Nid == "bzQExy189ZI" || importStubEntry.Nid == "8G2LB+A3rzg")
		{
			Console.Error.WriteLine($"[LOADER][TRACE] {importStubEntry.Nid}#{num}: rdi=0x{cpuContext[CpuRegister.Rdi]:X16} rsi=0x{cpuContext[CpuRegister.Rsi]:X16} rdx=0x{cpuContext[CpuRegister.Rdx]:X16} ret=0x{num7:X16}");
		}
		if (flag6 || flag || flag2 || flag3)
		{
			Console.Error.WriteLine($"[LOADER][TRACE] ImportCtx#{num}: nid={importStubEntry.Nid} ret=0x{num7:X16} rdi=0x{cpuContext[CpuRegister.Rdi]:X16} rsi=0x{cpuContext[CpuRegister.Rsi]:X16} rdx=0x{cpuContext[CpuRegister.Rdx]:X16} rcx=0x{cpuContext[CpuRegister.Rcx]:X16}");
			Console.Error.WriteLine($"[LOADER][TRACE] ImportNV#{num}: rbx=0x{value3:X16} rbp=0x{value4:X16} r12=0x{value5:X16} r13=0x{value6:X16} r14=0x{value7:X16} r15=0x{value8:X16}");
			if (flag3)
			{
				ulong num9 = cpuContext[CpuRegister.Rsp];
				if (cpuContext.TryReadUInt64(num9, out var value9) && cpuContext.TryReadUInt64(num9 + 8, out var value10) && cpuContext.TryReadUInt64(num9 + 16, out var value11) && cpuContext.TryReadUInt64(num9 + 24, out var value12) && cpuContext.TryReadUInt64(num9 + 32, out var value13) && cpuContext.TryReadUInt64(num9 + 40, out var value14) && cpuContext.TryReadUInt64(num9 + 48, out var value15) && cpuContext.TryReadUInt64(num9 + 56, out var value16) && cpuContext.TryReadUInt64(num9 + 64, out var value17))
				{
					Console.Error.WriteLine($"[LOADER][TRACE] ImportStackHead#{num}: rsp=0x{num9:X16} [0]=0x{value9:X16} [20]=0x{value13:X16} [40]=0x{value17:X16}");
					Console.Error.WriteLine($"[LOADER][TRACE] ImportStack#{num}: rsp=0x{num9:X16} [0]=0x{value9:X16} [8]=0x{value10:X16} [10]=0x{value11:X16} [18]=0x{value12:X16} [20]=0x{value13:X16} [28]=0x{value14:X16} [30]=0x{value15:X16} [38]=0x{value16:X16} [40]=0x{value17:X16}");
				}
			}
			if (flag6 && string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_IMPORT_FRAMES"), "1", StringComparison.Ordinal))
			{
				TraceImportFrameChain(cpuContext, num);
			}
			if (flag6 && string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_IMPORT_RECENT"), "1", StringComparison.Ordinal))
			{
				DumpRecentImportTrace();
			}
			if (flag3)
			{
				Console.Error.Flush();
			}
		}
		if (importStubEntry.Nid == "Ou3iL1abvng")
		{
			if (string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_STACK_CHK"), "1", StringComparison.Ordinal))
			{
				var savedGuardAddress = value4 >= 0x10 ? value4 - 0x10 : 0;
				var guardKnown = TryReadUInt64Compat(value3, out var guardValue);
				var savedKnown = TryReadUInt64Compat(savedGuardAddress, out var savedGuardValue);
				Console.Error.WriteLine(
					$"[LOADER][TRACE] stack_chk_diag#{num}: ret=0x{num7:X16} guard_ptr=0x{value3:X16} " +
					$"guard={(guardKnown ? $"0x{guardValue:X16}" : "?")} saved@0x{savedGuardAddress:X16}={(savedKnown ? $"0x{savedGuardValue:X16}" : "?")} " +
					$"rbp=0x{value4:X16} rsp=0x{((ulong)argPackPtr + 96uL):X16}");
			}
			try
			{
				byte[] array = new byte[64];
				Marshal.Copy((nint)(num7 - 32), array, 0, array.Length);
				Console.Error.WriteLine($"[LOADER][TRACE] __stack_chk_fail return-site @0x{num7:X16}: {BitConverter.ToString(array).Replace("-", " ")}");
			}
			catch
			{
			}
			TryBypassStackChkFailTrap(num, num7);
		}
		try
		{
			OrbisGen2Result orbisGen2Result;
			bool dispatchResolved = true;
			var previousImportCallFrame = GuestThreadExecution.EnterImportCallFrame(num7, (ulong)argPackPtr + 104uL);
			try
			{
				if (string.Equals(importStubEntry.Nid, RuntimeStubNids.BootstrapBridge, StringComparison.Ordinal))
				{
					orbisGen2Result = DispatchBootstrapBridge();
				}
				else if (string.Equals(importStubEntry.Nid, RuntimeStubNids.KernelDynlibDlsym, StringComparison.Ordinal))
				{
					orbisGen2Result = DispatchKernelDynlibDlsym();
				}
				else
				{
					dispatchResolved = _moduleManager.TryDispatch(importStubEntry.Nid, cpuContext, out orbisGen2Result);
				}
			}
			finally
			{
				GuestThreadExecution.RestoreImportCallFrame(previousImportCallFrame);
			}
			if (dispatchResolved &&
				orbisGen2Result == OrbisGen2Result.ORBIS_GEN2_OK &&
				string.Equals(importStubEntry.Nid, "BohYr-F7-is", StringComparison.Ordinal))
			{
				RegisterPrtLazyCommitRange(value2, num3);
			}
			if (!dispatchResolved)
			{
				LastError = "Missing HLE export for NID: " + importStubEntry.Nid;
				Console.Error.WriteLine($"[LOADER][WARN] Import#{num} unresolved: nid={importStubEntry.Nid} ret=0x{num7:X16}");
				if (importStubEntry.Nid == "L-Q3LEjIbgA")
				{
					string value18 = string.Join(" ", importStubEntry.Nid.Select(delegate (char c)
					{
						int num10 = c;
						return num10.ToString("X2");
					}));
					Console.Error.WriteLine($"[LOADER][WARN] map_direct nid raw len={importStubEntry.Nid.Length} chars=[{value18}]");
					Delegate function;
					bool value19 = _moduleManager.TryGetFunction(importStubEntry.Nid, out function);
					ExportedFunction export2;
					bool value20 = _moduleManager.TryGetExport(importStubEntry.Nid, out export2);
					Console.Error.WriteLine($"[LOADER][WARN] map_direct lookup with import nid: function={value19}, export={value20}");
					Console.Error.WriteLine(_moduleManager.TryGetExport("L-Q3LEjIbgA", out ExportedFunction export3) ? $"[LOADER][WARN] Canonical map_direct exists as {export3.LibraryName}:{export3.Name}, target={export3.Target}, ctx_target={cpuContext.TargetGeneration}" : "[LOADER][WARN] Canonical map_direct export lookup also missing");
				}
			}
			else if (orbisGen2Result != OrbisGen2Result.ORBIS_GEN2_OK)
			{
				Console.Error.WriteLine(
					$"[LOADER][WARN] Import#{num} result: {orbisGen2Result} ({importStubEntry.Nid}) " +
					$"rdi=0x{value:X16} rsi=0x{value2:X16} rdx=0x{num3:X16} rcx=0x{num4:X16} ret=0x{num7:X16}");
			}
			cpuContext[CpuRegister.Rbx] = value3;
			cpuContext[CpuRegister.Rbp] = value4;
			cpuContext[CpuRegister.R12] = value5;
			cpuContext[CpuRegister.R13] = value6;
			cpuContext[CpuRegister.R14] = value7;
			cpuContext[CpuRegister.R15] = value8;
			cpuContext[CpuRegister.Rdi] = value;
			cpuContext[CpuRegister.Rsi] = value2;
			if (GuestThreadExecution.TryConsumeCurrentEntryExit(out var exitStatus, out var exitReason))
			{
				if (TryCompleteGuestEntryToHostStub(argPackPtr, num, num7, importStubEntry.Nid, exitReason, exitStatus))
				{
					cpuContext[CpuRegister.Rax] = unchecked((ulong)exitStatus);
				}
				else
				{
					LastError = $"Failed to complete guest entry after {importStubEntry.Nid}: missing host return sentinel";
					cpuContext[CpuRegister.Rax] = 18446744071562199298uL;
				}
			}
			if (GuestThreadExecution.TryConsumeCurrentThreadBlock(out var blockReason) &&
				TryYieldGuestThreadToHostStub(argPackPtr, num, num7, importStubEntry.Nid, blockReason))
			{
				cpuContext[CpuRegister.Rax] = 0uL;
			}
			if (flag || flag2 || flag3)
			{
				Console.Error.WriteLine($"[LOADER][TRACE] ImportRet#{num}: nid={importStubEntry.Nid} result={orbisGen2Result} rax=0x{cpuContext[CpuRegister.Rax]:X16}");
				if (flag3)
				{
					Console.Error.Flush();
				}
			}
			return cpuContext[CpuRegister.Rax];
		}
		catch (Exception ex)
		{
			LastError = $"HLE dispatch error for {importStubEntry.Nid}: {ex.GetType().Name}: {ex.Message}";
			cpuContext[CpuRegister.Rax] = 18446744071562199298uL;
			return 18446744071562199298uL;
		}
	}

	private void TraceImportFrameChain(CpuContext context, long dispatchIndex)
	{
		var frame = context[CpuRegister.Rbp];
		for (int i = 0; i < 16; i++)
		{
			if (!context.TryReadUInt64(frame, out var next) ||
				!context.TryReadUInt64(frame + sizeof(ulong), out var returnRip))
			{
				break;
			}

			var symbol = TryFormatNearestRuntimeSymbol(returnRip, out var formatted)
				? $" [{formatted}]"
				: string.Empty;
			Console.Error.WriteLine(
				$"[LOADER][TRACE] ImportFrame#{dispatchIndex}.{i}: rbp=0x{frame:X16} ret=0x{returnRip:X16}{symbol} next=0x{next:X16}");
			if (next <= frame || next - frame > 0x100000)
			{
				break;
			}

			frame = next;
		}
	}

	private unsafe bool TryForceGuestExitToHostStub(nint argPackPtr, long dispatchIndex, ulong returnRip, string nid)
	{
		ulong num = ActiveEntryReturnSentinelRip;
		if (num < 65536)
		{
			return false;
		}
		try
		{
			*(ulong*)(argPackPtr + 96) = num;
		}
		catch
		{
			return false;
		}
		ActiveForcedGuestExit = true;
		LastError = $"Detected repeating import loop at import#{dispatchIndex} ({nid}) and forced guest exit.";
		Console.Error.WriteLine($"[LOADER][ERROR] Import-loop guard fired at import#{dispatchIndex}: nid={nid} ret=0x{returnRip:X16} -> host_exit=0x{num:X16}");
		DumpRecentImportTrace();
		return true;
	}

	private unsafe bool TryCompleteGuestEntryToHostStub(nint argPackPtr, long dispatchIndex, ulong returnRip, string nid, string reason, int status)
	{
		ulong hostExit = ActiveEntryReturnSentinelRip;
		if (hostExit < 65536)
		{
			return false;
		}
		try
		{
			*(ulong*)(argPackPtr + 96) = hostExit;
		}
		catch
		{
			return false;
		}
		Console.Error.WriteLine(
			$"[LOADER][INFO] Guest entry exit at import#{dispatchIndex}: nid={nid} ret=0x{returnRip:X16} reason={reason} status={status}");
		return true;
	}

	private unsafe bool TryYieldGuestThreadToHostStub(nint argPackPtr, long dispatchIndex, ulong returnRip, string nid, string reason)
	{
		ulong hostExit = ActiveEntryReturnSentinelRip;
		if (hostExit < 65536)
		{
			return false;
		}
		try
		{
			*(ulong*)(argPackPtr + 96) = hostExit;
		}
		catch
		{
			return false;
		}

		ActiveGuestThreadYieldRequested = true;
		ActiveGuestThreadYieldReason = string.IsNullOrWhiteSpace(reason) ? nid : reason;
		Console.Error.WriteLine(
			$"[LOADER][INFO] Guest thread yield at import#{dispatchIndex}: nid={nid} ret=0x{returnRip:X16} reason={ActiveGuestThreadYieldReason}");
		return true;
	}

	private bool ShouldForceGuestExitOnImportLoop(string nid, ulong returnRip, long dispatchIndex, ulong arg0, ulong arg1)
	{
		if (dispatchIndex < 1200)
		{
			return false;
		}
		if (string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_DISABLE_IMPORT_LOOP_GUARD"), "1", StringComparison.Ordinal))
		{
			return false;
		}
		if (!_importNidHashCache.TryGetValue(nid, out var value))
		{
			value = StableHash64(nid);
			_importNidHashCache[nid] = value;
		}
		RecordImportLoopSignature(value, returnRip, BuildImportLoopSignature(value, returnRip, arg0, arg1));
		if (!HasRepeatingImportLoopPattern())
		{
			if (_importLoopPatternHits > 0)
			{
				_importLoopPatternHits--;
			}
			if (_importLoopPatternHits == 0)
			{
				_importLoopPatternStartTimestamp = 0;
			}
			return false;
		}
		if (_importLoopPatternStartTimestamp == 0)
		{
			_importLoopPatternStartTimestamp = Stopwatch.GetTimestamp();
		}
		_importLoopPatternHits++;
		var guardSeconds = GetImportLoopGuardSeconds();
		if (guardSeconds <= 0 || _importLoopPatternHits < 6)
		{
			return false;
		}

		var elapsedTicks = Stopwatch.GetTimestamp() - _importLoopPatternStartTimestamp;
		return elapsedTicks >= (long)(guardSeconds * Stopwatch.Frequency);
	}

	private static int GetImportLoopGuardSeconds()
	{
		if (int.TryParse(Environment.GetEnvironmentVariable("SHARPEMU_IMPORT_LOOP_GUARD_SECONDS"), out var seconds))
		{
			return Math.Max(0, seconds);
		}

		return DefaultImportLoopGuardSeconds;
	}

	private ulong BuildImportLoopSignature(ulong nidHash, ulong returnRip, ulong arg0, ulong arg1)
	{
		ulong num = returnRip >> 2;
		ulong num2 = ((arg0 >> 4) * 11400714819323198485uL) ^ ((arg1 >> 4) * 14029467366897019727uL);
		return num ^ nidHash * 11400714819323198485uL ^ num2;
	}

	private void RecordImportLoopSignature(ulong nidHash, ulong returnRip, ulong signature)
	{
		_importLoopSignatures[_importLoopSignatureWriteIndex] = signature;
		_importLoopNidHashes[_importLoopSignatureWriteIndex] = nidHash;
		_importLoopReturnRips[_importLoopSignatureWriteIndex] = returnRip;
		_importLoopSignatureWriteIndex = (_importLoopSignatureWriteIndex + 1) % _importLoopSignatures.Length;
		if (_importLoopSignatureCount < _importLoopSignatures.Length)
		{
			_importLoopSignatureCount++;
		}
	}

	private bool HasRepeatingImportLoopPattern()
	{
		int num = _importLoopSignatureCount;
		if (num < 96)
		{
			return false;
		}
		int num2 = Math.Min(48, num / 4);
		for (int i = 6; i <= num2; i++)
		{
			if (HasRepeatingImportLoopPattern(i, 4))
			{
				return true;
			}
		}
		return false;
	}

	private bool HasRepeatingImportLoopPattern(int period, int repeats)
	{
		int num = period * repeats;
		if (period <= 0 || repeats < 2 || _importLoopSignatureCount < num)
		{
			return false;
		}
		for (int i = 0; i < period; i++)
		{
			ulong importLoopSignatureFromTail = GetImportLoopSignatureFromTail(i);
			for (int j = 1; j < repeats; j++)
			{
				if (GetImportLoopSignatureFromTail(i + j * period) != importLoopSignatureFromTail)
				{
					return false;
				}
			}
		}
		return IsSevereImportLoopPattern(num);
	}

	private ulong GetImportLoopSignatureFromTail(int offset)
	{
		int num = _importLoopSignatureWriteIndex - 1 - offset;
		while (num < 0)
		{
			num += _importLoopSignatures.Length;
		}
		return _importLoopSignatures[num % _importLoopSignatures.Length];
	}

	private bool IsSevereImportLoopPattern(int sampleCount)
	{
		int num = CountDistinctImportLoopValuesFromTail(_importLoopNidHashes, sampleCount, 3);
		if (num > 2)
		{
			return false;
		}
		int num2 = CountDistinctImportLoopValuesFromTail(_importLoopReturnRips, sampleCount, 3);
		if (num2 > 2)
		{
			return false;
		}
		int num3 = Math.Min(_importLoopSignatureCount, Math.Max(sampleCount * 8, ImportLoopWideDiversityWindow));
		if (num3 <= sampleCount)
		{
			return true;
		}
		if (CountDistinctImportLoopValuesFromTail(_importLoopNidHashes, num3, 3) > 2)
		{
			return false;
		}
		return CountDistinctImportLoopValuesFromTail(_importLoopReturnRips, num3, 3) <= 2;
	}

	private int CountDistinctImportLoopValuesFromTail(ulong[] source, int sampleCount, int stopAfter)
	{
		int num = Math.Min(sampleCount, _importLoopSignatureCount);
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			ulong importLoopValueFromTail = GetImportLoopValueFromTail(source, i);
			bool flag = false;
			for (int j = 0; j < i; j++)
			{
				if (GetImportLoopValueFromTail(source, j) == importLoopValueFromTail)
				{
					flag = true;
					break;
				}
			}
			if (!flag && ++num2 >= stopAfter)
			{
				return num2;
			}
		}
		return num2;
	}

	private ulong GetImportLoopValueFromTail(ulong[] source, int offset)
	{
		int num = _importLoopSignatureWriteIndex - 1 - offset;
		while (num < 0)
		{
			num += source.Length;
		}
		return source[num % source.Length];
	}

	private bool ShouldSuppressStrlenTrace(string nid)
	{
		return string.Equals(nid, "j4ViWNHEgww", StringComparison.Ordinal) && !_logStrlenImports;
	}

	private void TrackDistinctImportNid(string nid)
	{
		if (string.IsNullOrWhiteSpace(nid) || string.Equals(_lastDistinctImportNid, nid, StringComparison.Ordinal))
		{
			return;
		}
		_lastDistinctImportNid = nid;
		_distinctImportNidHistory[_distinctImportNidHistoryWriteIndex] = nid;
		_distinctImportNidHistoryWriteIndex = (_distinctImportNidHistoryWriteIndex + 1) % _distinctImportNidHistory.Length;
		if (_distinctImportNidHistoryCount < _distinctImportNidHistory.Length)
		{
			_distinctImportNidHistoryCount++;
		}
	}

	private void TrackStrlenPrelude(string nid, long dispatchIndex, ulong returnRip)
	{
		if (!string.Equals(nid, "j4ViWNHEgww", StringComparison.Ordinal))
		{
			_consecutiveStrlenImports = 0;
			_strlenPreludeLogged = false;
			return;
		}
		_consecutiveStrlenImports++;
		if (_strlenPreludeLogged || _consecutiveStrlenImports < 24)
		{
			return;
		}
		_strlenPreludeLogged = true;
		List<string> list = GetRecentDistinctImportPrelude(maxCount: 5, skipNid: "j4ViWNHEgww");
		if (list.Count == 0)
		{
			Console.Error.WriteLine($"[LOADER][WARNING] Import#{dispatchIndex}: detected strlen burst (count={_consecutiveStrlenImports}) ret=0x{returnRip:X16}; no prelude NIDs recorded.");
			return;
		}
		Console.Error.WriteLine($"[LOADER][WARNING] Import#{dispatchIndex}: detected strlen burst (count={_consecutiveStrlenImports}) ret=0x{returnRip:X16}; last5_nids={string.Join(" -> ", list)}");
	}

	private List<string> GetRecentDistinctImportPrelude(int maxCount, string skipNid)
	{
		List<string> list = new List<string>(maxCount);
		if (maxCount <= 0 || _distinctImportNidHistoryCount == 0)
		{
			return list;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < _distinctImportNidHistoryCount && list.Count < maxCount; i++)
		{
			int num = _distinctImportNidHistoryWriteIndex - 1 - i;
			while (num < 0)
			{
				num += _distinctImportNidHistory.Length;
			}
			string text = _distinctImportNidHistory[num % _distinctImportNidHistory.Length];
			if (string.IsNullOrWhiteSpace(text) || string.Equals(text, skipNid, StringComparison.Ordinal) || !hashSet.Add(text))
			{
				continue;
			}
			if (_moduleManager.TryGetExport(text, out ExportedFunction export))
			{
				list.Add($"{export.LibraryName}:{export.Name}({text})");
			}
			else
			{
				list.Add(text);
			}
		}
		list.Reverse();
		return list;
	}

	private static ulong StableHash64(string text)
	{
		ulong num = 14695981039346656037uL;
		for (int i = 0; i < text.Length; i++)
		{
			num ^= text[i];
			num *= 1099511628211uL;
		}
		return num;
	}

	private OrbisGen2Result DispatchKernelDynlibDlsym()
	{
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null)
		{
			return OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
		}
		ulong symbolNameAddress = cpuContext[CpuRegister.Rsi];
		ulong outputAddress = cpuContext[CpuRegister.Rdx];
		if (!TryReadAsciiZ(symbolNameAddress, 512, out var symbolName))
		{
			cpuContext[CpuRegister.Rax] = 18446744073709551615uL;
			return OrbisGen2Result.ORBIS_GEN2_OK;
		}
		if (!TryResolveRuntimeSymbolAddress(symbolName, out var resolvedAddress))
		{
			cpuContext[CpuRegister.Rax] = 18446744073709551615uL;
			return OrbisGen2Result.ORBIS_GEN2_OK;
		}
		if (outputAddress == 0L || !TryWriteUInt64Compat(outputAddress, resolvedAddress))
		{
			cpuContext[CpuRegister.Rax] = 18446744073709551615uL;
			return OrbisGen2Result.ORBIS_GEN2_OK;
		}
		cpuContext[CpuRegister.Rax] = 0uL;
		return OrbisGen2Result.ORBIS_GEN2_OK;
	}

	private OrbisGen2Result DispatchBootstrapBridge()
	{
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null)
		{
			return OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
		}

		ulong bridgeHandle = cpuContext[CpuRegister.Rdi];
		ulong symbolNameAddress = cpuContext[CpuRegister.Rsi];
		ulong outputAddress = cpuContext[CpuRegister.Rdx];
		_ = TryReadAsciiZ(symbolNameAddress, 512, out var symbolName);

		OrbisGen2Result result = DispatchKernelDynlibDlsym();
		if (result != OrbisGen2Result.ORBIS_GEN2_OK)
		{
			return result;
		}
		bool logBootstrap = string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_BOOTSTRAP"), "1", StringComparison.Ordinal);
		if (logBootstrap)
		{
			Console.Error.WriteLine(
				$"[LOADER][TRACE] bootstrap_dispatch: handle=0x{bridgeHandle:X16} symbol='{symbolName}' out=0x{outputAddress:X16} rax=0x{cpuContext[CpuRegister.Rax]:X16}");
		}

		if (cpuContext[CpuRegister.Rax] == 0uL)
		{
			return OrbisGen2Result.ORBIS_GEN2_OK;
		}

		Console.Error.WriteLine(
			$"[LOADER][WARN] bootstrap_bridge unresolved: handle=0x{bridgeHandle:X} symbol='{symbolName}' out=0x{outputAddress:X16}");
		return OrbisGen2Result.ORBIS_GEN2_OK;
	}

	private bool TryResolveRuntimeSymbolAddress(string symbolName, out ulong address)
	{
		address = 0uL;
		if (string.IsNullOrWhiteSpace(symbolName))
		{
			return false;
		}
		if (_runtimeSymbolsByName.TryGetValue(symbolName, out var value) && IsRuntimeSymbolAddressUsable(value))
		{
			address = value;
			return true;
		}
		if (symbolName.StartsWith("_", StringComparison.Ordinal) && _runtimeSymbolsByName.TryGetValue(symbolName[1..], out value) && IsRuntimeSymbolAddressUsable(value))
		{
			address = value;
			return true;
		}
		if (_runtimeSymbolsByName.TryGetValue("_" + symbolName, out value) && IsRuntimeSymbolAddressUsable(value))
		{
			address = value;
			return true;
		}
		return false;
	}

	private static bool IsRuntimeSymbolAddressUsable(ulong value)
	{
		return value != 0 && !IsUnresolvedSentinel(value);
	}

	private bool TryReadAsciiZ(ulong address, int maxLength, out string value)
	{
		value = string.Empty;
		if (ActiveCpuContext == null || address == 0L || maxLength <= 0)
		{
			return false;
		}
		List<byte> list = new List<byte>(Math.Min(maxLength, 256));
		Span<byte> destination = stackalloc byte[1];
		for (int i = 0; i < maxLength; i++)
		{
			if (!TryReadByteCompat(address + (ulong)i, destination))
			{
				return false;
			}
			if (destination[0] == 0)
			{
				value = System.Text.Encoding.ASCII.GetString(list.ToArray());
				return true;
			}
			list.Add(destination[0]);
		}
		value = System.Text.Encoding.ASCII.GetString(list.ToArray());
		return true;
	}

	private bool TryReadByteCompat(ulong address, Span<byte> destination)
	{
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null || destination.Length == 0)
		{
			return false;
		}
		if (cpuContext.Memory.TryRead(address, destination))
		{
			return true;
		}
		try
		{
			destination[0] = Marshal.ReadByte((nint)address);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private bool TryReadUInt64Compat(ulong address, out ulong value)
	{
		value = 0;
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null || address == 0L)
		{
			return false;
		}
		if (cpuContext.TryReadUInt64(address, out value))
		{
			return true;
		}
		try
		{
			value = unchecked((ulong)Marshal.ReadInt64((nint)address));
			return true;
		}
		catch
		{
			value = 0;
			return false;
		}
	}

	private bool TryWriteUInt64Compat(ulong address, ulong value)
	{
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null || address == 0L)
		{
			return false;
		}
		if (cpuContext.TryWriteUInt64(address, value))
		{
			return true;
		}
		try
		{
			Marshal.WriteInt64((nint)address, unchecked((long)value));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void TryBypassStackChkFailTrap(long dispatchIndex, ulong returnRip)
	{
		var cpuContext = ActiveCpuContext;
		if (cpuContext == null || returnRip < 32)
		{
			return;
		}
		try
		{
			byte[] array = new byte[19];
			ulong num = returnRip - 23;
			Marshal.Copy((nint)num, array, 0, array.Length);
			if (array[0] != 117 || array[1] != 16 || array[2] != 72 || array[3] != 137 || array[4] != 216 || array[5] != 72 || array[6] != 131 || array[7] != 196 || array[9] != 91 || array[10] != 65 || array[11] != 92 || array[12] != 65 || array[13] != 94 || array[14] != 65 || array[15] != 95 || array[16] != 93 || array[17] != 195 || array[18] != 232)
			{
				return;
			}
			ulong value = returnRip - 21;
			ulong address = cpuContext[CpuRegister.Rsp];
			if (cpuContext.TryWriteUInt64(address, value))
			{
				if (_stackChkBypassSites.Add(num) && TryPatchStackChkFailBranch(num))
				{
					Console.Error.WriteLine($"[LOADER][WARNING] Import#{dispatchIndex}: patched stack_chk_fail tail branch at 0x{num:X16} -> NOP NOP");
				}
				Console.Error.WriteLine($"[LOADER][WARNING] Import#{dispatchIndex}: redirected __stack_chk_fail return to epilogue 0x{value:X16}");
			}
		}
		catch
		{
		}
	}

	private unsafe static bool TryPatchStackChkFailBranch(ulong branchAddress)
	{
		uint flNewProtect = default(uint);
		if (!VirtualProtect((void*)branchAddress, 2u, 64u, &flNewProtect))
		{
			return false;
		}
		try
		{
			if (Marshal.ReadByte((nint)branchAddress) != 117)
			{
				return false;
			}
			Marshal.WriteByte((nint)branchAddress, 144);
			Marshal.WriteByte((nint)(branchAddress + 1), 144);
			FlushInstructionCache(GetCurrentProcess(), (void*)branchAddress, 2u);
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			VirtualProtect((void*)branchAddress, 2u, flNewProtect, &flNewProtect);
		}
	}

	private unsafe void TryPatchEa020eLookupCall(long dispatchIndex, ulong returnRip)
	{
		if (_patchedEa020eLookupCall || returnRip != 0x0000000800EA01A6uL)
		{
			return;
		}
		const ulong num = 0x0000000800EA020EuL;
		nint num2 = unchecked((nint)num);
		uint flNewProtect = default(uint);
		try
		{
			if (Marshal.ReadByte(num2) != 232 || !VirtualProtect((void*)num, 5u, 64u, &flNewProtect))
			{
				return;
			}
			for (int i = 0; i < 5; i++)
			{
				Marshal.WriteByte(num2 + i, 144);
			}
			FlushInstructionCache(GetCurrentProcess(), (void*)num, 5u);
			_patchedEa020eLookupCall = true;
			Console.Error.WriteLine($"[LOADER][WARNING] Import#{dispatchIndex}: patched hash-lookup call at 0x{num:X16} -> NOP*5");
		}
		catch
		{
		}
		finally
		{
			if (flNewProtect != 0)
			{
				VirtualProtect((void*)num, 5u, flNewProtect, &flNewProtect);
			}
		}
	}
}
