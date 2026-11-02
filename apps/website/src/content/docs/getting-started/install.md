---
title: Instalación y primera impresión
description: Descargar, instalar y probar el agente de Angelito Print Bridge.
sidebar: { order: 3 }
---
## Descargar
Descargue el instalador desde la [página de descargas](/angelito-print-bridge/download/) o GitHub Releases y verifique su SHA-256:
```powershell
Get-FileHash .\AngelitoPrintBridge-0.3.0-win-x64-Setup.exe -Algorithm SHA256
```
Los ejecutables aún no están firmados: SmartScreen puede advertir.

## Instalar
- Por usuario (sin administrador): ejecute el instalador.
- Todo el equipo: `Setup.exe /ALLUSERS`.
- Silenciosa: `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="autostart"`.

## Primer inicio y bandeja
La app aparece junto al reloj (en Windows 11 puede estar en «Mostrar iconos ocultos»).
- **Clic**: minipanel con estado, impresoras y trabajos. **Doble clic**: panel completo. **Clic derecho**: menú.
- «Iniciar con Windows» se activa en el menú o en Configuración.

## Primera impresión
En el panel completo › **Impresoras** › **Página de prueba** sobre la impresora elegida (imprime en papel).
Desde una web, use el [laboratorio](/angelito-print-bridge/playground/).
