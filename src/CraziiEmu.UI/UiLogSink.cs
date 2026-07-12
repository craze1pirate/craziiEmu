// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Avalonia.Controls;
using Avalonia.Threading;
using CraziiEmu.Logging;

namespace CraziiEmu.UI;

/// <summary>
/// A log sink implementation that forwards all emulator log output to the
/// MainWindow's console TextBox via the Avalonia UI thread dispatcher.
/// </summary>
public class UiLogSink : ICraziiEmuLogSink
{
    private readonly TextBox _consoleOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiLogSink"/> class.
    /// </summary>
    /// <param name="consoleOutput">The TextBox control to append log messages to.</param>
    public UiLogSink(TextBox consoleOutput)
    {
        _consoleOutput = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
    }

    /// <summary>
    /// Writes a log entry to the console TextBox. Marshals to the UI thread if
    /// called from a background thread.
    /// </summary>
    /// <param name="entry">The log entry to display.</param>
    public void Write(in LogEntry entry)
    {
        var line = $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] [{entry.Category}] {entry.Message}\n";

        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendLine(line);
        }
        else
        {
            // Capture the string for the closure (LogEntry is a ref struct-like record)
            var captured = line;
            Dispatcher.UIThread.Post(() => AppendLine(captured));
        }
    }

    /// <summary>
    /// Appends a line of text to the console TextBox and scrolls to the end.
    /// Must be called on the UI thread.
    /// </summary>
    private void AppendLine(string line)
    {
        _consoleOutput.Text += line;
        _consoleOutput.CaretIndex = _consoleOutput.Text?.Length ?? 0;
    }
}
