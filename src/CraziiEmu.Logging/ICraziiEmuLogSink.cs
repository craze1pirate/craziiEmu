// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Logging;

public interface ICraziiEmuLogSink
{
    void Write(in LogEntry entry);
}
