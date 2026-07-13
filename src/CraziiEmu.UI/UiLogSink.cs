// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using CraziiEmu.Logging;
using Avalonia.Threading;

namespace CraziiEmu.UI;

/// <summary>
/// A log entry suitable for binding to the UI console.
/// </summary>
public class ConsoleLine
{
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = "White";
}

/// <summary>
/// A log sink implementation that forwards all emulator log output to an action
/// (typically provided by the MainWindow) to display in the UI console.
/// </summary>
public class UiLogSink : ICraziiEmuLogSink
{
    private readonly Action<ConsoleLine> _appendLineAction;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiLogSink"/> class.
    /// </summary>
    /// <param name="appendLineAction">The action to call with formatted log lines.</param>
    public UiLogSink(Action<ConsoleLine> appendLineAction)
    {
        _appendLineAction = appendLineAction ?? throw new ArgumentNullException(nameof(appendLineAction));
    }

    /// <summary>
    /// Writes a log entry to the UI console by invoking the configured action.
    /// Marshals to the UI thread automatically.
    /// </summary>
    /// <param name="entry">The log entry to display.</param>
    public void Write(in LogEntry entry)
    {
        var color = entry.Level switch
        {
            LogLevel.Warning => "Yellow",
            LogLevel.Error => "Red",
            LogLevel.Trace => "Gray",
            _ => "White"
        };

        var line = new ConsoleLine 
        { 
            Text = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] [{entry.Category}] {entry.Message}",
            Color = color
        };

        if (Dispatcher.UIThread.CheckAccess())
            _appendLineAction(line);
        else
            Dispatcher.UIThread.Post(() => _appendLineAction(line));
    }
}
