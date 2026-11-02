# Arquitectura

```
 Navegador (https://app.cliente.com)                         Equipo Windows del usuario
 ┌──────────────────────────────┐   HTTP/WS 127.0.0.1:41730  ┌───────────────────────────────────────────┐
 │ @angelitosystems/print-bridge│ ─────────────────────────▶ │ AngelitoPrintBridge.exe (Kestrel loopback)│
 │  connect · pair · print · on │ ◀───── eventos de trabajo ─ │  HttpSecurity: Host · Origin · CORS · LNA │
 └──────────────────────────────┘                            │  Endpoints /v1  ── RateLimiter            │
                                                             │  PairingService · SessionService          │
 Usuario / admin local ── CLI (token DPAPI) ───────────────▶ │  JobService ── QueueWorker / SpoolMonitor │
                                                             │  BridgeStore (SQLite WAL) · spool/*.bin   │
                                                             │  IPrintDriver ─▶ WindowsPrintDriver       │
                                                             │     RAW: winspool WritePrinter            │
                                                             │     PDF: Windows.Data.Pdf → GDI → spooler │
                                                             └───────────────────────────────────────────┘
```

## Módulos (mapa al prompt maestro)
| Módulo | Implementación actual |
|---|---|
| Core Domain | `Core/Domain/Model.cs` |
| Local API Gateway | `Agent/Endpoints.cs`, `Agent/Program.cs` |
| Connection Manager | WebSocket `/v1/events` + reconexión en SDK |
| Pairing & Authentication | `Core/Security/Security.cs` |
| Authorization Policy | permisos por grant (`printers:read`, `print:document`, `print:raw`) |
| Print Job Manager / Persistent Queue | `Core/Jobs/JobService.cs`, `Core/Persistence/BridgeStore.cs` |
| Rendering / Discovery / Adapters | `Windows/*` |
| Logging & Diagnostics | Serilog JSON en `%LOCALAPPDATA%\AngelitoSystems\PrintBridge\logs`, tabla `audit` |
| Desktop UI | `Desktop/` (Avalonia 12, proceso separado del agente) |
| Update, Cloud | **pendiente** (fases siguientes). No hay gestor de licencias: proyecto Apache-2.0 |

## Decisiones (ADR resumidos)
1. **.NET 10 + Kestrel solo en loopback** (`ListenLocalhost`). Nunca se enlaza a interfaces de red.
2. **Sin TLS local**: `http://127.0.0.1` es *potentially trustworthy* para Chrome/Edge/Firefox, por lo que una página
   https puede llamarlo sin contenido mixto. Evita instalar una CA raíz local (riesgo alto y frágil). Safari no está soportado (Windows primero).
3. **Local Network Access (Chrome/Edge ≥ 142)**: el navegador pide permiso al usuario la primera vez que un sitio
   público accede a loopback. Se respeta (no se intenta eludir); el agente además responde al preflight antiguo de PNA
   (`Access-Control-Allow-Private-Network`). La UI de la app debe explicar ese permiso antes de `connect()`.
4. **Autorización en dos niveles**: *grant* largo (por app+origen, revocable, guardado como hash SHA-256 en SQLite)
   → *sesión* corta en memoria (15 min, ligada al origen). Reiniciar el agente invalida sesiones; el SDK las renueva.
5. **Emparejamiento con código + verificador**: la página genera un verificador aleatorio; el usuario aprueba
   localmente comparando un código de 6 dígitos; el token se entrega una sola vez, al mismo origen con el mismo verificador.
6. **Cola SQLite con CAS de estados**. `spool_started` se persiste antes de tocar el spooler: tras un crash,
   lo no enviado vuelve a la cola y lo enviado a medias pasa a `Unknown` (sin reimpresión automática).
7. **PDF con el motor de Windows** (`Windows.Data.Pdf`, sin dependencias de terceros, sin GPU obligatoria) rasterizado
   a ≤300 dpi y enviado por GDI con `StandardPrintController` (sin diálogos). Alternativa evaluada: PDFium (BSD) si
   aparecen PDFs incompatibles.
8. **RAW por winspool** (`WritePrinter`, datatype RAW) para ESC/POS/ZPL/EPL. Requiere drivers que acepten RAW
   (los drivers v4/IPP "class" pueden no hacerlo).
9. **Impresora virtual `virtual:file`** (desactivada por defecto) para pruebas E2E y demostraciones sin papel.
10. **UI en proceso separado del agente**: si la UI falla o se cierra, la impresión continúa. La UI usa solo la API admin.
11. **Instalador Inno Setup** (uso comercial gratuito) en lugar de WiX v6 (tarifa de mantenimiento para productos con ingresos) o MSIX (exige certificado de confianza incluso para pruebas).
12. **Estados**: `Completed` significa que el trabajo salió de la cola de Windows (`confirmation: "spooler"`), nunca confirmación física.

## Flujo de un trabajo
`POST /v1/jobs` → payload a `spool/{id}.bin` + fila `Queued` → `QueueWorker` reclama (`Rendering`) → driver
(`spool_started=1`) → `Submitted` (payload borrado) → `SpoolMonitor` consulta `EnumJobs` por `APB-{id}` →
`Completed` / `Cancelled` (eliminado en cola). Fallos transitorios antes del spool: reintento exponencial (máx. 3).
