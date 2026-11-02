---
title: What is Angelito Print Bridge?
description: A secure, open source local bridge between web apps and Windows printers.
sidebar: { order: 1 }
---
Angelito Print Bridge lets an **authorized** web application print silently —without the Windows dialog— to the
user's printers: PDF, images and RAW commands (ESC/POS, ZPL, EPL).

It is free software (Apache 2.0) created by **Angelito Systems S.A.C.** Printing needs no internet and no external service.

## Components
- **Agent** (`AngelitoPrintBridge.exe`): local service on `127.0.0.1:41730` that talks to the Windows spooler.
- **Tray app** (`AngelitoPrintBridge.Desktop.exe`): mini panel, app approval, printers, jobs and certificates.
- **SDK** `@angelitosystems/print-bridge` plus integrations for React, Angular, Svelte and Vue.

## Requirements
Windows 10 2004+ or Windows 11 (x64; ARM64 built but not hardware-validated) and a modern browser (Chrome, Edge, Firefox).

## Concepts
- **Pairing**: the user approves your app once by matching a 6-digit code.
- **Application certificate**: hands-free alternative; your server signs and the machine trusts the certificate.
- **Permissions**: `printers:read`, `print:document`, `print:raw`.
- **Job states**: `queued`, `rendering`, `submitted`, `completed` (left the Windows queue; not paper confirmation), `failed`, `cancelled`, `unknown`.
