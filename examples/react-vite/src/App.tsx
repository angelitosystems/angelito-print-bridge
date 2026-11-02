// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { useState } from "react";
import { escpos } from "@angelitosystems/print-bridge";
import { usePrint, usePrintBridgeConnection, usePrinters, usePrintQueue } from "@angelitosystems/print-bridge-react";

export function App() {
  const { state, pairingCode, error, connect, pair } = usePrintBridgeConnection();
  const { printers, defaultPrinter } = usePrinters();
  const { printPdf, printRaw, cancel, isPrinting, lastJob, error: printError } = usePrint();
  const { jobs } = usePrintQueue();
  const [printerId, setPrinterId] = useState<string>("");
  const selected = printerId || defaultPrinter?.id || "";

  async function printSample() {
    const pdf = await (await fetch(`${import.meta.env.BASE_URL}sample.pdf`)).blob();
    await printPdf({ printerId: selected, document: pdf });
  }

  function printTicket() {
    const ticket = escpos().init().align("center").bold().text("ANGELITO PRINT BRIDGE").newline().bold(false)
      .text("Ticket de prueba").feed(3).cut().build();
    return printRaw({ printerId: selected, data: ticket });
  }

  return (
    <main style={{ fontFamily: "system-ui", maxWidth: 720, margin: "2rem auto", padding: "0 1rem" }}>
      <h1>Angelito Print Bridge · React + Vite</h1>
      <p>Estado: <strong>{state}</strong></p>
      {error && <p role="alert" style={{ color: "crimson" }}>{error.code}: {error.message}</p>}

      {(state === "disconnected" || state === "error") && <button onClick={() => void connect()}>Conectar con el agente</button>}
      {state === "unpaired" && (
        <button onClick={() => void pair({ permissions: ["printers:read", "print:document", "print:raw"] }).catch(() => {})}>
          Autorizar esta aplicación
        </button>
      )}
      {pairingCode && <p>Confirme el código <strong style={{ fontSize: 24, letterSpacing: 4 }}>{pairingCode}</strong> en Angelito Print Bridge.</p>}

      {state === "connected" && (
        <section>
          <label>
            Impresora{" "}
            <select value={selected} onChange={(e) => setPrinterId(e.target.value)}>
              {printers.map((p) => <option key={p.id} value={p.id}>{p.name}{p.isDefault ? " (predeterminada)" : ""}</option>)}
            </select>
          </label>{" "}
          <button disabled={!selected || isPrinting} onClick={() => void printSample().catch(() => {})}>Imprimir PDF de prueba</button>{" "}
          <button disabled={!selected || isPrinting} onClick={() => void printTicket().catch(() => {})}>Ticket ESC/POS</button>
          {printError && <p role="alert" style={{ color: "crimson" }}>{printError.code}: {printError.message}</p>}
          {lastJob && <p>Último trabajo: {lastJob.state}{lastJob.confirmation === "spooler" ? " (salió de la cola de Windows)" : ""}</p>}
          <h2>Trabajos</h2>
          <ul>
            {jobs.map((j) => (
              <li key={j.jobId}>
                {j.kind} → {j.printerId}: {j.state}{" "}
                {j.state === "queued" && <button onClick={() => void cancel(j.jobId)}>Cancelar</button>}
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  );
}
