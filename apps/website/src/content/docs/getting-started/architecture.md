---
title: Arquitectura
description: Cómo se comunican la web, el SDK, el agente local y el spooler de Windows.
sidebar: { order: 2 }
---
```
Navegador (https://su-app) ──HTTP/WebSocket 127.0.0.1:41730──▶ Agente (.NET 10, solo loopback)
   SDK + store compartido                                     ├─ Host/Origin/CORS + límites
                                                              ├─ Emparejamiento · sesiones · permisos
App de bandeja ──API admin (token DPAPI)──▶                   ├─ Cola SQLite persistente
                                                              └─ Spooler: RAW (WritePrinter) · PDF (Windows.Data.Pdf → GDI)
```
- El agente **nunca** escucha en la red: solo `127.0.0.1` y `::1`.
- Las sesiones son cortas (15 min) y están ligadas al origen; la autorización (grant) es revocable.
- La cola persiste en SQLite: lo no enviado se reanuda tras un reinicio; lo enviado a medias queda `unknown` y no se reimprime solo.
- En el navegador, el SDK mantiene **una conexión compartida** (`PrintBridgeStore`) que usan todas las integraciones.
