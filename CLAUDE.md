# Angelito Print Bridge — instrucciones del repositorio

Proyecto de código abierto (Apache-2.0) creado por Angelito Systems S.A.C. Puente local entre apps web y
impresoras/periféricos de Windows. Sin licencias comerciales, activaciones ni funciones bloqueadas: no añadirlas. Independiente de QZ Tray: no copiar su código ni depender de sus servicios.

## Estado
Ver `docs/IMPLEMENTATION_STATUS.md` (fuente de verdad de lo verificado). Decisiones en `docs/ARCHITECTURE.md`.

## Estructura
- `src/Angelito.PrintBridge.Core` — dominio, SQLite (`BridgeStore`), emparejamiento/sesiones (`Security`), cola (`JobService`). net10.0, sin dependencias de Windows.
- `src/Angelito.PrintBridge.Windows` — `AdminToken` (DPAPI), winspool P/Invoke (`WinSpool`), motor PDF de Windows (`Windows.Data.Pdf`), GDI, catálogo de impresoras.
- `src/Angelito.PrintBridge.Agent` — ASP.NET Core/Kestrel en loopback (`AngelitoPrintBridge.exe`), API `/v1`, WebSocket, CLI admin.
- `src/Angelito.PrintBridge.Desktop` — Avalonia 12 (MVVM, CommunityToolkit.Mvvm): bandeja + UI. Habla con el agente por la API admin (`/v1/admin`, token DPAPI). Inicia el agente si no corre.
- Certificados de aplicación: `Core/Security/AppCertificates.cs` + `PairingService.PairWithCertificate`; guía `docs/CERTIFICATES.md`.
- `installer/` — `publish.ps1` (autocontenido) + `AngelitoPrintBridge.iss` (Inno Setup 6). Ver `docs/RELEASE.md`.
- `tests/Angelito.PrintBridge.Tests` — xUnit (unit + integración HTTP/WS + componentes Windows reales).
- `packages/core` — `@angelitosystems/print-bridge` (ESM, strict). Tests con `node --test` (Node ≥ 22.6 con type stripping).

## Comandos
```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
./installer/publish.ps1                     # artifacts/win-x64/app
cd packages/core && npm ci && npm run typecheck && npm test && npm run test:e2e   # e2e requiere dotnet build previo
```

## Reglas
- **Nunca** auto-seleccionar impresoras instaladas en tests/smoke (OneNote abre la app). Usar `virtual:file`
  (`Bridge__VirtualPrinterEnabled=true`) o `APB_TEST_PRINTER` explícito. No imprimir en papel sin permiso.
- No registrar contenido de documentos, tokens ni datos personales (logs, auditoría, nombres de documentos del spooler: `APB-{jobId}`).
- No reintentar un trabajo cuyo envío al spooler ya empezó (`spool_started=1`) → `Unknown`, nunca reimpresión automática.
- `Completed` = salió de la cola de Windows, no confirmación física. No declarar soporte de opciones que el driver no declara.
- Ninguna clave privada, certificado ni credencial en el repo. No publicar paquetes/releases sin autorización explícita.
- Cambios en seguridad (Origin/Host/CORS/emparejamiento) requieren test en `ApiTests`/`PairingTests`.
- UI: verificar con UI Automation (nombres accesibles en botones con `AutomationProperties.Name`) y capturas; nunca pulsar "Página de prueba" en impresoras reales.
- No cambiar configuración del sistema (impresora predeterminada, registro) salvo acción explícita del usuario.
- Archivos nuevos con cabecera `// Copyright 2026 Angelito Systems S.A.C.` + `// SPDX-License-Identifier: Apache-2.0`.
- Dependencias: solo licencias permisivas (`docs/DEPENDENCIES.md`) y registrarlas en `THIRD-PARTY-NOTICES.md`.
- Bandeja: probar con entrada real solo cuando el usuario libere el escritorio; nunca clics en coordenadas guardadas.
- Documentación en español.
