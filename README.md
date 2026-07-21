<div align="center">
  <img src="./assets/images/logo.png" width="28%" alt="CraziiEmu Logo"/>
  <h1>CraziiEmu</h1>
  <p><strong>An experimental PlayStation 5 compatibility layer and research platform built with C# and .NET.</strong></p>

  ![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue)
  ![Framework](https://img.shields.io/badge/.NET-10-purple)
  ![Graphics](https://img.shields.io/badge/Graphics-Vulkan-orange)
  ![License](https://img.shields.io/badge/License-GPL--2.0-green)
</div>

---

> [!WARNING]  
> ### Experimental Software
> CraziiEmu is an early-stage PlayStation 5 emulator intended for research, reverse engineering, and emulator development. Compatibility is currently limited, many kernel services remain incomplete, and crashes or missing functionality are expected.

> [!IMPORTANT]  
> ### ⚖️ Legal Notice
> CraziiEmu does **not** include or distribute:
> * PlayStation 5 firmware or Sony proprietary libraries
> * Cryptographic keys, game content, or copyrighted assets
> 
> Users are strictly responsible for supplying legally obtained files extracted from hardware they own. **This project does not support piracy.**

---

## Overview

**CraziiEmu** is an experimental PlayStation 5 compatibility layer written entirely in C# using modern .NET. 

Built upon the excellent open-source foundation of the **[SharpEmu](https://github.com/par274/sharpemu)** project, CraziiEmu extends the architecture with a highly-polished desktop frontend, runtime optimizations, expanded High-Level Emulation (HLE) implementations, and ongoing compatibility work. 

Currently, development and compilation natively target **Windows x64**.

---

## ✨ Features

### Premium Desktop Interface
Built using **Avalonia UI**, the frontend provides a console-like experience rather than a traditional windowed debugger:
- **Modern, Dark-Themed Dashboard:** A highly responsive, console-inspired scrolling game library.
- **Dynamic Theming:** Seamlessly maps native background artwork from your game library, adapting the interface on the fly.
- **Integrated Console:** A real-time, built-in diagnostic logging terminal for trace monitoring.
- **Configurable Settings:** Manage graphics, audio, debugging, and comprehensive controller configurations all within the UI.
- **Fullscreen Experience:** The emulator natively defaults to fullscreen mode, seamlessly toggled via `F11`.

### Advanced Input Mapping
- Native support for Keyboard, DualSense, DualShock 4, and Xbox controllers.
- Interactive custom key mapping and dynamic controller remapping.
- **Conflict Resolution:** Automatically swaps duplicate bindings in memory to prevent input overlap.

### Core Architecture & Runtime
- **Direct Execution Backend:** Bypasses software interpretation entirely, executing guest x86-64 code directly on the host CPU for maximum hardware speed.
- **Advanced Executable Loading:** Detects and parses both standard 64-bit ELF binaries and Sony's compressed SELF (`SCE\0`) containers, automatically extracting entry points.
- **Dynamic Linker:** Parses `PT_DYNAMIC` program headers, recursively loads dependent `.sprx` modules, and resolves complex relocations (`R_X86_64_RELATIVE`, `R_X86_64_GLOB_DAT`, `R_X86_64_JUMP_SLOT`).

### Memory & HLE Virtualization
- **Virtual Memory Manager:** Allocates a massive guest virtual address space utilizing page-based allocation.
- **Native VEH Trampoline:** Features native x86-64 assembly page-fault handling to safely bridge Windows hardware exceptions.
- **`libkernel` HLE Stubs:** Houses active implementations for critical PlayStation OS services, including thread management, memory allocation, synchronization primitives, and sleep/timing functions.

### Graphics (Vulkan)
Graphics presentation is built exclusively on **Vulkan**, offering a high-performance rendering pathway. Implementations include swapchain creation, direct video output, and splash image presentation. *(Internal resolution scaling is planned for future updates).*

---

## Interface Preview

<div align="center">
  <p><em>Main Dashboard (Games Library Tab)</em></p>
  <img src="./assets/images/dashboard.png" width="85%" alt="CraziiEmu Dashboard">

  <br><br>

  <p><em>Advanced Controller Configuration</em></p>
  <img src="./assets/images/controls.png" width="85%" alt="CraziiEmu Controls">
</div>

---

## 🛠️ Build & Installation

### Prerequisites
* **.NET 10 SDK** (or newer)
* **Windows x64** (Windows 11 recommended)
* A Vulkan-compatible GPU

### Compilation Steps
1. **Clone the repository:**
   ```bash
   git clone https://github.com/craze1pirate/craziiEmu.git
   cd craziiEmu
   ```
2. **Build the solution:**
   ```bash
   dotnet build -c Release
   ```
3. **Run directly via CLI:**
   ```bash
   dotnet run --project src/CraziiEmu.UI
   ```
4. **Publish as a standalone native executable:**
   ```bash
   dotnet publish src/CraziiEmu.UI/CraziiEmu.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   ```

---

## 🚀 Roadmap
CraziiEmu is under active development. Current and planned priorities include:
- Expanding `libkernel` HLE coverage and boot compatibility.
- Enhancing runtime stability and module loading.
- Implementing internal graphics resolution scaling.
- Audio backend improvements.
- Expanding the integrated debugging toolset.

---

## Contributing
Bug reports, pull requests, and reverse engineering research are highly welcome. 
When opening an issue, please provide your **Build Version**, **OS/Hardware Specs**, the **Game/Executable** tested, and an absolute **Emulator Log Trace** or stack trace.

---

## Acknowledgements
Special thanks to the following projects for making this possible:
- **[SharpEmu](https://github.com/par274/sharpemu)** — The original architectural foundation and initial research.
- **[shadPS4](https://github.com/shadps4-emu/shadPS4)** — An invaluable reference for PlayStation 5 kernel and shared library behaviors.
- **[Ryujinx](https://github.com/Ryujinx/Ryujinx)** — Inspiration for high-performance C# runtime and systems programming techniques.

---

## License
CraziiEmu is licensed under the **GNU General Public License v2.0 (GPL-2.0)**. 
See the [LICENSE](LICENSE) file for more information.