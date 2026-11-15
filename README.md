# ⚡ RAM Booster Core (Community Edition)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6.svg)](https://microsoft.com/windows)
[![Runtime](https://img.shields.io/badge/.NET-Framework%204.8%20Native-512BD4.svg)](https://dotnet.microsoft.com/)
[![Kernel](https://img.shields.io/badge/API-Win32%20%26%20NT%20Native-00E5FF.svg)](#native-architecture)
[![Dependencies](https://img.shields.io/badge/Dependencies-Zero%20(Single%20Binary)-brightgreen.svg)](#)

> **High-Performance Windows Kernel NT Memory Optimizer & AI Process Sentinel.**  
> Pure C# / Native x64 utility with zero external dependencies, designed to prevent Windows freezing and memory thrashing in heavy AI, local LLM, and developer workloads.

---

## 🎯 The Problem

Modern development environments running local AI agents, coding assistants, and worker processes (**Claude Desktop**, **Cursor**, **Node.js / OpenCode**, **Python**, **Language Servers**, and **DuckDB**) frequently saturate physical RAM.

When RAM fills up:
1. The Windows Memory Manager accumulates gigabytes of cached files in the **Kernel Standby List**.
2. Background workers and IDEs hold massive **Working Sets** of inactive pages.
3. The system begins **Memory Thrashing** (severe Page Fault spikes and disk I/O saturation), leading to mouse stutter, unresponsiveness, and complete OS freezes.

### Why Traditional "PC Cleaners" Fail
Most cleaners simply delete `%TEMP%` files, recycle bin, or prefetch caches. **Deleting files from your SSD does not free a single byte of physical RAM.** 

**RAM Booster Core** operates at the **Windows Kernel NT level**, directly communicating with low-level subsystem APIs to reclaim unmapped physical memory without terminating your running applications.

---

## 🧠 Native Architecture & How It Works

```
┌────────────────────────────────────────────────────────────────────────┐
│                        RAM BOOSTER CORE                                │
└──────┬────────────────────┬────────────────────┬───────────────────────┘
       │                    │                    │
       ▼                    ▼                    ▼
[psapi.dll]           [ntdll.dll]          [kernel32.dll]
EmptyWorkingSet()     NtSetSystemInfo()    SetProcessInformation()
                      (SystemMemoryList)   (EcoQoS / Efficiency)
       │                    │                    │
       ▼                    ▼                    ▼
• Trims inactive      • Purges Kernel      • Throttles background
  pages of target       Standby List         workers to yield CPU
  processes to free   • Flushes System       instantly to interactive
  physical memory.      File Cache.          windows.
```

### Key Subsystems:

1. **Kernel NT Standby List Purge (`ntdll.dll` -> `NtSetSystemInformation`):**
   * Elevates `SeProfileSingleProcessPrivilege` and `SeIncreaseQuotaPrivilege`.
   * Invokes `MemoryPurgeStandbyList` (Class 80, Command 4) to flush phantom standby memory back to the Windows Zeroed/Free Page List.
2. **Safe Working Set Trimming (`psapi.dll` -> `EmptyWorkingSet`):**
   * Discharges dead/idle memory allocations without closing the software.
   * **Active Window Immunity:** Uses `user32.dll -> GetForegroundWindow()` to identify and shield whatever window you are actively using, ensuring zero interruptions.
3. **AI & Dev Process Sentinel (`AISentinel.cs`):**
   * Real-time monitoring of AI agents (`claude.exe`, `cursor.exe`, `node.exe`, `python.exe`, `antigravity.exe`, `language_server.exe`).
   * **CPU-Aware Smart Trim:** If a process is under active CPU load (`>= 12%`), trimming is withheld to prevent Page Fault storms, applying **EcoQoS** instead.
4. **Windows EcoQoS / Efficiency Mode (`kernel32.dll` -> `SetProcessInformation`):**
   * Forces background extractors and AI scripts into `PROCESS_POWER_THROTTLING_EXECUTION_SPEED`, ensuring the mouse, graphics driver (`dwm.exe`), and IDE remain fluid.

---

## 🚀 Features

- **Zero Bloat & Zero Telemetry:** Pure native C# code. No tracking, no background services, no ads, no web requests.
- **Single Portable Binary (~80 KB):** Compiles directly with Windows native `csc.exe` 64-bit compiler. No need to install massive IDEs or runtimes.
- **Dual Interface:** Full CLI argument support for scripts/automations + interactive ANSI terminal menu.
- **Automated Sentinel Watch Mode:** Background daemon that monitors RAM usage and automatically performs soft trims if memory pressure exceeds safe thresholds.

---

## 📦 Getting Started

### 1. Download or Clone
```bash
git clone https://github.com/YOUR_USERNAME/rambooster-core.git
cd rambooster-core
```

### 2. Compile from Source (1-Click)
No Visual Studio required! Windows has the native 64-bit C# compiler pre-installed:

* Double-click `build.bat` or run:
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
The optimized executable will be generated at `bin\rambooster.exe`.

---

## 💻 CLI Usage & Commands

Run in any elevated terminal (Command Prompt, PowerShell, Windows Terminal):

```bash
# Full Turbo Boost: Kernel Standby List purge + Working Sets trim
rambooster.exe --boost

# Target AI & Developer processes only (Claude, Node, Python, Cursor, etc.)
rambooster.exe --trim-ai

# Purge only Kernel Standby List and System File Cache
rambooster.exe --standby

# Start Continuous Sentinel Watch Mode (Auto-manages memory)
rambooster.exe --watch

# Display current system RAM and Kernel cache metrics
rambooster.exe --status

# Display help and arguments
rambooster.exe --help
```

If launched without parameters, **RAM Booster Core** opens an interactive terminal menu:

```
  _____            __  __   ____                  _             
 |  __ \   /\     |  \/  | |  _ \                | |            
 | |__) | /  \    | \  / | | |_) | ___   ___  ___| |_ ___ _ __ 
 |  _  / / /\ \   | |\/| | |  _ < / _ \ / _ \/ __| __/ _ \ '__|
 | | \ \/ ____ \  | |  | | | |_) | (_) | (_) \__ \ ||  __/ |   
 |_|  \_\_/    \_\_|  |_| |____/ \___/ \___/|___/\__\___|_|   
 Kernel NT & AI Process Sentinel • Open Source Community Edition v1.0.0

 [+] RAM Física: 15.39 GB Total | 9.42 GB Em Uso (61.2%) | 5.97 GB Livre
 [+] Cache do Kernel: 153 MB (Standby List & File Cache)
 [+] Commit: 18.3/27.9 GB (66%)

 MENU DE OPERAÇÕES:
 [1] ⚡ Turbo Boost Completo (Kernel Standby + Working Sets)
 [2] 🤖 Otimizar Apenas IAs e Workers (Node, Python, Claude, Cursor)
 [3] 🧹 Purgar Kernel Standby List (Sem tocar em processos)
 [4] 🛡️ Iniciar Modo Sentinela Contínuo (Watch Mode)
 [5] 🔄 Atualizar Telemetria
 [0] Sair
```

---

## 🛡️ Security, Privacy & Transparency

| Aspect | Implementation |
| :--- | :--- |
| **Data Collection** | **Zero.** The software makes no HTTP/HTTPS calls and collects no telemetry. |
| **File System Integrity** | Does **NOT** delete user files, system logs, prefetch, or registry keys. |
| **Application Safety** | Applications are **not terminated**. Memory pages are simply paged out to Standby. |
| **Foreground Shield** | The active window with user focus is **never touched**. |
| **Permissions Required** | `Administrator` privileges are required solely for calling `NtSetSystemInformation` and adjusting process working set quotas via Win32. |

---

## 📜 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details. You are free to use, modify, distribute, and integrate this software into your own tools.
