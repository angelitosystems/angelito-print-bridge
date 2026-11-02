---
title: Referencia API
description: Métodos, tipos, errores y eventos del SDK @angelitosystems/print-bridge.
---
## `createPrintBridge(options)`
| Opción | Tipo | Descripción |
|---|---|---|
| `appId` | `string` | Dominio inverso, p. ej. `com.empresa.app` (obligatorio) |
| `appName` | `string` | Nombre que ve el usuario al aprobar |
| `ports` | `number[]` | Puertos de loopback (por defecto `[41730, 41731, 41732]`) |
| `storage` | `GrantStorage` | Por defecto `sessionGrantStorage()`; también `localGrantStorage()` y `memoryStorage()` |
| `requestTimeoutMs` | `number` | 15000 por defecto |
| `autoReconnect` | `boolean` | Reabre el WebSocket de eventos (por defecto `true`) |
| `WebSocket` | `typeof WebSocket \| null` | `null` desactiva eventos (sondeo) |
| `certificate` | `{ pem, sign, permissions?, origin? }` | Emparejamiento por certificado |

## `PrintBridge`
| Miembro | Descripción |
|---|---|
| `state` | `disconnected · connecting · unpaired · connected · error` |
| `connect()` | Localiza el agente y restaura la autorización |
| `pair({ permissions, onCode?, signal?, pollIntervalMs? })` | Solicita aprobación con código |
| `pairWithCertificate(signal?)` | Emparejamiento firmado |
| `printers.list()` | `Printer[]` con `capabilities` declaradas por el driver |
| `print.pdf / print.image / print.raw` | Devuelven `Job` cuando el agente acepta el trabajo |
| `jobs.get / list / cancel / waitFor` | Consulta y seguimiento |
| `on("job" \| "state", listener)` | Devuelve la función para desuscribir |
| `disconnect()` · `forget()` | Cerrar eventos · olvidar la autorización |

## `PrintBridgeStore`
`getSnapshot()` → `{ state, pairingCode, printers, printersStatus, jobs, submitting, error }` (inmutable) ·
`subscribe(listener)` · `connect()` · `pair()` · `refreshPrinters()` · `printPdf/printImage/printRaw` · `cancel` · `watchJob` · `dispose()`.

## Errores (`PrintBridgeError.code`)
`agent_not_found`, `not_paired`, `pair_denied`, `pair_expired`, `timeout`, `aborted`, `network_error`, `permission_denied`,
`printer_not_found`, `document_too_large`, `unsupported_option`, `invalid_document`, `untrusted_certificate`,
`origin_mismatch`, `invalid_signature`, `stale_signature`, `replayed_request`.

## Compatibilidad
| Paquete | Requiere |
|---|---|
| core 0.4 | Navegadores modernos, Node 20+ (pruebas/SSR) · agente 0.3+ |
| react 0.1 | React 18.2+ / 19, core ^0.4 |
| angular 0.1 | Angular 20–22, core ^0.4 |
| svelte 0.1 | Svelte 5, core ^0.4 |
| vue 0.1 | Vue 3.4+, core ^0.4 |
