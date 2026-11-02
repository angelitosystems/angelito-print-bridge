---
title: React + Vite
description: Provider y hooks de @angelitosystems/print-bridge-react.
---
```bash
npm install @angelitosystems/print-bridge @angelitosystems/print-bridge-react
```
Requiere React 18.2+ o 19.
```tsx
import { PrintBridgeProvider, usePrintBridgeConnection, usePrinters, usePrint } from "@angelitosystems/print-bridge-react";

createRoot(root).render(
  <PrintBridgeProvider options={{ appId: "com.miempresa.app", appName: "Mi App" }}>
    <App />
  </PrintBridgeProvider>,
);

function PrintButton({ pdf }: { pdf: Blob }) {
  const { state, connect, pair, pairingCode } = usePrintBridgeConnection();
  const { printers, defaultPrinter } = usePrinters();
  const { printPdf, isPrinting, lastJob } = usePrint();
  if (state !== "connected") return state === "unpaired"
    ? <button onClick={() => pair({ permissions: ["printers:read", "print:document"] })}>Autorizar {pairingCode}</button>
    : <button onClick={connect}>Conectar</button>;
  return <button disabled={isPrinting || !printers.length} onClick={() => printPdf({ printerId: defaultPrinter?.id, document: pdf })}>
    Imprimir {lastJob?.state}
  </button>;
}
```
| Hook | Devuelve |
|---|---|
| `usePrintBridgeConnection()` | `state`, `isConnected`, `pairingCode`, `error`, `connect`, `pair`, `disconnect`, `forget` |
| `usePrinters()` | `printers`, `defaultPrinter`, `isLoading`, `error`, `refresh` |
| `usePrinter(id)` | impresora y capacidades |
| `usePrintJob(id)` | estado en vivo del trabajo |
| `usePrintQueue()` | `jobs`, `active` |
| `usePrint()` | `printPdf`, `printImage`, `printRaw`, `cancel`, `isPrinting`, `lastJob`, `error` |

Una sola conexión por `PrintBridgeProvider`; compatible con Strict Mode. `autoConnect` es `false` por defecto para que el
permiso de red local del navegador aparezca tras una acción del usuario. Ejemplo: `examples/react-vite`.
