---
title: Install and first print
description: Download, install and test the Angelito Print Bridge agent.
sidebar: { order: 3 }
---
Download the installer from the [downloads page](/angelito-print-bridge/en/download/) and verify its SHA-256:
```powershell
Get-FileHash .\AngelitoPrintBridge-0.3.0-win-x64-Setup.exe -Algorithm SHA256
```
Executables are not code-signed yet, so SmartScreen may warn.

- Per user (no admin): run the installer. Whole machine: `Setup.exe /ALLUSERS`.
- Silent: `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="autostart"`.

The tray icon appears next to the clock (on Windows 11 it may be under “Show hidden icons”):
**click** for the mini panel, **double-click** for the full window, **right-click** for the menu.
First print: full window › **Printers** › **Test page** (prints on paper), or use the [playground](/angelito-print-bridge/en/playground/).
