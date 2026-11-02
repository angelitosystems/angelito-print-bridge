---
title: JavaScript / TypeScript SDK
description: Connect, pair, list printers and print with @angelitosystems/print-bridge.
---
```ts
import { createPrintBridge, escpos } from "@angelitosystems/print-bridge";

const bridge = createPrintBridge({ appId: "com.company.pos", appName: "My POS" });
await bridge.connect();
if (bridge.state === "unpaired") {
  await bridge.pair({ permissions: ["printers:read", "print:document", "print:raw"], onCode: showCode });
}
const printers = await bridge.printers.list();
const job = await bridge.print.pdf({ printerId: printers[0].id, document: pdfBlob, options: { copies: 1 } });
await bridge.jobs.waitFor(job.jobId);

const ticket = escpos().init().align("center").bold().text("MY STORE").newline().cut().build();
await bridge.print.raw({ printerId: "POS-80", data: ticket });
```
The authorization is kept in `sessionStorage` by default; opt into `localGrantStorage()` to persist it across visits.
See the Spanish pages for framework guides and the full API reference (translation in progress).
