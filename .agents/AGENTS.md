# Project-Scoped Rules for craziiEmu

## File Header Directives

Follow these strict rules for file headers across the `craziiEmu` codebase:

### 1. Editing Existing Files (Standard):
When editing an existing file originally from SharpEmu, use:
```csharp
// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
```

### 2. Editing Existing Files (Referred / Ported from KytyPS5):
When editing an existing file and explicitly referring/porting from KytyPS5 (ONLY if editing more than 20 lines of code), use:
```csharp
// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project
```
If editing 20 lines of code or fewer, do NOT add Kyty attribution; keep standard headers:
```csharp
// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
```

### 3. Newly Created Files (Referred from KytyPS5):
When creating a new file referred from KytyPS5, use:
```csharp
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project
```

### 4. Newly Created Files (General / Internal):
When creating a new file without KytyPS5 attribution, use:
```csharp
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
```
