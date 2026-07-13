// Copyright (C) 2026 craze1pirate - craziiEmu Project
// Copyright (C) 2026 par274 - sharpemu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Logging;

public interface ICraziiEmuLogSink
{
    void Write(in LogEntry entry);
}
