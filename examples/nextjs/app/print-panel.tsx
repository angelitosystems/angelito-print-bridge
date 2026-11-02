"use client";
// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { useState } from "react";
import { usePrint, usePrintBridgeConnection, usePrinters } from "@angelitosystems/print-bridge-react";

export function PrintPanel() {
  const { state, pairingCode, error, connect, pair } = usePrintBridgeConnection();
  const { printers, defaultPrinter } = usePrinters();
  const { printPdf, isPrinting, lastJob } = usePrint();
  const [printerId, setPrinterId] = useState("");
  const selected = printerId || defaultPrinter?.id || "";

  return (
    <section>
      {/* Server HTML and first client render match ("disconnected"): no hydration mismatch. */}
      <p>Estado: <strong>{state}</strong></p>
      {error && <p role="alert">{error.code}: {error.message}</p>}
      {(state === "disconnected" || state === "error") && <button onClick={() => void connect()}>Conectar con el agente</button>}
      {state === "unpaired" && (
        <button onClick={() => void pair({ permissions: ["printers:read", "print:document"] }).catch(() => {})}>Autorizar</button>
      )}
      {pairingCode && <p>Código: <strong>{pairingCode}</strong></p>}
      {state === "connected" && (
        <>
          <select value={selected} onChange={(e) => setPrinterId(e.target.value)}>
            {printers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>{" "}
          <button
            disabled={!selected || isPrinting}
            onClick={async () => printPdf({ printerId: selected, document: await (await fetch(`${process.env.NEXT_PUBLIC_BASE_PATH ?? ""}/sample.pdf`)).blob() }).catch(() => {})}
          >
            Imprimir PDF de prueba
          </button>
          {lastJob && <p>Último trabajo: {lastJob.state}</p>}
        </>
      )}
    </section>
  );
}
