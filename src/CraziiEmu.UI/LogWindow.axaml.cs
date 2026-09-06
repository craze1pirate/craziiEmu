// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CraziiEmu.UI;

/// <summary>
/// A standalone, lightweight logging window for CraziiEmu.
/// Displays emulator console output with virtualized rendering, syntax highlighting,
/// and clipboard/export actions.
/// </summary>
public partial class LogWindow : Window
{
    private const int MaxPendingQueueSize = 10_000;
    private const int MaxDisplayedLines = 5_000;
    private const int MaxBatchPerTick = 200;

    private readonly ConcurrentQueue<ConsoleLine> _pendingQueue = new();
    private readonly DispatcherTimer _batchTimer;

    /// <summary>
    /// Gets the observable collection of log entries bound to the console ListBox.
    /// </summary>
    public ObservableCollection<ConsoleLine> ConsoleMessages { get; } = new();

    /// <summary>
    /// Raised whenever this window is hidden or closed by the user.
    /// </summary>
    public event Action? OnHidden;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogWindow"/> class.
    /// </summary>
    public LogWindow()
    {
        InitializeComponent();

        ConsoleOutput.ItemsSource = ConsoleMessages;

        // Chrome controls
        DragHandle.PointerPressed += (_, e) => BeginMoveDrag(e);
        BtnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnClose.Click += (_, _) =>
        {
            Hide();
            OnHidden?.Invoke();
        };

        // Actions
        BtnClear.Click += (_, _) => ClearLogs();
        BtnCopy.Click += OnBtnCopyClicked;
        BtnExport.Click += OnBtnExportClicked;

        // UI Batch Timer (100 ms interval)
        _batchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _batchTimer.Tick += OnBatchTimerTick;
        _batchTimer.Start();
    }

    /// <summary>
    /// Enqueues a log line to be displayed in the console window.
    /// Thread-safe — may be invoked from any thread without dispatcher marshalling.
    /// </summary>
    public void EnqueueLine(ConsoleLine line)
    {
        // Prevent unbound memory growth if window remains hidden during heavy activity
        while (_pendingQueue.Count >= MaxPendingQueueSize)
        {
            _pendingQueue.TryDequeue(out _);
        }

        _pendingQueue.Enqueue(line);
    }

    /// <summary>
    /// Clears both the pending queue and displayed messages.
    /// </summary>
    public void ClearLogs()
    {
        while (_pendingQueue.TryDequeue(out _)) { }
        ConsoleMessages.Clear();
        TxtLineCount.Text = "0 lines";
    }

    private void OnBatchTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible && _pendingQueue.Count < MaxPendingQueueSize / 2)
        {
            // If the window is not visible, save CPU and UI layout cycles.
            // When pending queue approaches capacity, we'll flush to preserve history.
            return;
        }

        if (_pendingQueue.IsEmpty)
        {
            return;
        }

        int dequeued = 0;
        ConsoleLine? lastLine = null;

        while (dequeued < MaxBatchPerTick && _pendingQueue.TryDequeue(out var line))
        {
            ConsoleMessages.Add(line);
            lastLine = line;
            dequeued++;
        }

        while (ConsoleMessages.Count > MaxDisplayedLines)
        {
            ConsoleMessages.RemoveAt(0);
        }

        TxtLineCount.Text = $"{ConsoleMessages.Count:N0} lines";

        if (IsVisible && ChkAutoScroll.IsChecked == true && lastLine != null)
        {
            try
            {
                ConsoleOutput.ScrollIntoView(lastLine);
            }
            catch
            {
                // Guard against Avalonia layout re-entrance under high-frequency updates
            }
        }
    }

    private async void OnBtnCopyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        string text;
        if (ConsoleOutput.SelectedItems != null && ConsoleOutput.SelectedItems.Count > 1)
        {
            text = string.Join(Environment.NewLine, ConsoleOutput.SelectedItems.OfType<ConsoleLine>().Select(m => m.Text));
        }
        else
        {
            text = string.Join(Environment.NewLine, ConsoleMessages.Select(m => m.Text));
        }

        await topLevel.Clipboard.SetTextAsync(text);
    }

    private async void OnBtnExportClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || !topLevel.StorageProvider.CanSave)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Console Logs",
            DefaultExtension = "txt",
            SuggestedFileName = $"CraziiEmu_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        });

        if (file != null)
        {
            var text = string.Join(Environment.NewLine, ConsoleMessages.Select(m => m.Text));
            try
            {
                await using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);
            }
            catch
            {
                // Silently ignore or catch write exceptions
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the window should actually close instead of hiding.
    /// Used when the application is shutting down.
    /// </summary>
    public bool AllowClose { get; set; } = false;

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            // Don't destroy the window on close, hide it so logs can continue to be captured
            e.Cancel = true;
            Hide();
            OnHidden?.Invoke();
        }
        else
        {
            base.OnClosing(e);
        }
    }
}
