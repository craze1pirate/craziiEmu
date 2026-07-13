// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using CraziiEmu.Logging;

namespace CraziiEmu.UI;

/// <summary>
/// A log sink implementation that forwards all emulator log output to an action
/// (typically provided by the MainWindow) to display in the UI console.
/// </summary>
public class UiLogSink : ICraziiEmuLogSink
{
    private readonly Action<string> _appendLineAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiLogSink"/> class.
    /// </summary>
    /// <param name="appendLineAction">The action to call with formatted log lines.</param>
    public UiLogSink(Action<string> appendLineAction)
    {
        _appendLineAction = appendLineAction ?? throw new ArgumentNullException(nameof(appendLineAction));
    }

    /// <summary>
    /// Writes a log entry to the UI console by invoking the configured action.
    /// Thread safety and marshaling to the UI thread is the responsibility of the action delegate.
    /// </summary>
    /// <param name="entry">The log entry to display.</param>
    public void Write(in LogEntry entry)
    {
        var line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] [{entry.Category}] {entry.Message}";
        _appendLineAction(line);
    }
}
