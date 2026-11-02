---
title: SDK JavaScript / TypeScript
description: Conectar, emparejar, listar impresoras e imprimir con @angelitosystems/print-bridge.
---
```bash
npm install @angelitosystems/print-bridge
```
## Conexión y emparejamiento
```ts
import { createPrintBridge, PrintBridgeError } from "@angelitosystems/print-bridge";

const bridge = createPrintBridge({ appId: "com.miempresa.pos", appName: "Mi POS" });
try {
  await bridge.connect();               // localiza el agente; no lanza si falta autorización
} catch (e) {
  if (e instanceof PrintBridgeError && e.code === "agent_not_found") mostrarDescarga();
}
if (bridge.state === "unpaired") {
  await bridge.pair({ permissions: ["printers:read", "print:document", "print:raw"], onCode: (c) => mostrarCodigo(c) });
}
```
La autorización se guarda en `sessionStorage` por defecto. Para no re-emparejar en cada visita, opte por
`storage: localGrantStorage()` (cualquier script de su origen podría leerla; es revocable).

## Impresoras e impresión
```ts
const printers = await bridge.printers.list();          // capacidades declaradas por cada driver
const job = await bridge.print.pdf({ printerId: printers[0].id, document: pdfBlob, options: { copies: 2, paperSize: "A4" } });
const final = await bridge.jobs.waitFor(job.jobId);      // completed | failed | cancelled | unknown
await bridge.jobs.cancel(job.jobId);
```
`print.image` acepta PNG/JPEG/BMP y `print.raw` bytes o texto. Use `idempotencyKey` para evitar duplicados en reintentos.

## Tickets ESC/POS
```ts
import { escpos } from "@angelitosystems/print-bridge";
const ticket = escpos().init().align("center").bold().text("MI TIENDA").newline().bold(false)
  .line("Café", "S/ 8.50", 48).qr("20614770229|03|B001|1").feed(3).cut().openDrawer().build();
await bridge.print.raw({ printerId: "POS-80", data: ticket });
```
## Eventos y store compartido
```ts
bridge.on("job", (j) => console.log(j.jobId, j.state));
bridge.on("state", (s) => console.log(s));

import { createPrintBridgeStore } from "@angelitosystems/print-bridge";
const store = createPrintBridgeStore({ appId: "com.miempresa.pos" });
store.subscribe(() => render(store.getSnapshot()));     // state, printers, jobs, pairingCode, error
```
## Certificado de aplicación
Con `certificate: { pem, sign }` el SDK se empareja sin intervención si el equipo confía en el certificado. La firma la
hace su servidor; nunca ponga la clave privada en JavaScript. Ver [Seguridad](/angelito-print-bridge/security/).
