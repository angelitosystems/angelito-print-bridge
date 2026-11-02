// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// @vitest-environment jsdom

import { Component, StrictMode, useRef, type ReactNode } from "react";
import { act, render, screen, waitFor, cleanup } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { renderToString } from "react-dom/server";
import { startMockAgent, fetchWithOrigin, type MockAgent } from "@angelitosystems/print-bridge-shared-testing";
import { createPrintBridgeStore, memoryStorage, type PrintBridgeStore } from "@angelitosystems/print-bridge";
import {
  PrintBridgeProvider, usePrint, usePrintBridgeConnection, usePrinter, usePrinters, usePrintQueue,
} from "../src/index.tsx";

const ORIGIN = "https://react.example.com";
let agent: MockAgent;
let store: PrintBridgeStore;

beforeEach(async () => {
  agent = await startMockAgent();
  store = createPrintBridgeStore({ appId: "com.example.react", ports: [agent.port], fetch: fetchWithOrigin(ORIGIN), WebSocket: null, storage: memoryStorage() });
});
afterEach(async () => {
  cleanup();
  store.dispose();
  await agent.close();
});

function App() {
  const { state, pairingCode, connect, pair } = usePrintBridgeConnection();
  const { printers, defaultPrinter } = usePrinters();
  const a4 = usePrinter("Mock A4");
  const { printPdf, isPrinting, lastJob } = usePrint();
  const { jobs } = usePrintQueue();
  const renders = useRef(0);
  renders.current++;
  return (
    <div>
      <p data-testid="state">{state}</p>
      <p data-testid="code">{pairingCode ?? "-"}</p>
      <p data-testid="printers">{printers.map((p) => p.id).join(",")}</p>
      <p data-testid="default">{defaultPrinter?.id ?? "-"}</p>
      <p data-testid="duplex">{String(a4?.capabilities?.duplex)}</p>
      <p data-testid="job">{lastJob?.state ?? "-"}</p>
      <p data-testid="queue">{jobs.length}</p>
      <p data-testid="printing">{String(isPrinting)}</p>
      <button onClick={() => void connect()}>connect</button>
      <button onClick={() => void pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 })}>pair</button>
      <button onClick={() => void printPdf({ printerId: "Mock A4", document: new Uint8Array([37, 80, 68, 70]) })}>print</button>
    </div>
  );
}

describe("print-bridge-react", () => {
  it("full flow under StrictMode: connect, pair, printers, print, job tracked; one connection only", async () => {
    render(<StrictMode><PrintBridgeProvider store={store}><App /></PrintBridgeProvider></StrictMode>);
    expect(screen.getByTestId("state").textContent).toBe("disconnected");
    expect(agent.requests.length).toBe(0); // no connection on mount by default

    await act(async () => screen.getByText("connect").click());
    await waitFor(() => expect(screen.getByTestId("state").textContent).toBe("unpaired"));
    await act(async () => screen.getByText("pair").click());
    await waitFor(() => expect(screen.getByTestId("state").textContent).toBe("connected"));
    await waitFor(() => expect(screen.getByTestId("printers").textContent).toBe("Mock Thermal,Mock A4"));
    expect(screen.getByTestId("default").textContent).toBe("Mock Thermal");
    expect(screen.getByTestId("duplex").textContent).toBe("true");

    await act(async () => screen.getByText("print").click());
    await waitFor(() => expect(screen.getByTestId("job").textContent).toBe("completed"), { timeout: 6000 }); // polling fallback (mock has no WebSocket)
    expect(screen.getByTestId("queue").textContent).toBe("1");
    expect(screen.getByTestId("printing").textContent).toBe("false");
    expect(agent.requests.filter((r) => r.path === "/v1/pair/requests").length).toBe(1);
    expect(agent.requests.filter((r) => r.path === "/v1/session").length).toBe(1);
  });

  it("autoConnect connects once even with StrictMode double effects", async () => {
    render(<StrictMode><PrintBridgeProvider store={store} autoConnect><App /></PrintBridgeProvider></StrictMode>);
    await waitFor(() => expect(screen.getByTestId("state").textContent).toBe("unpaired"));
    expect(screen.getByTestId("code").textContent).toBe("-");
  });

  it("components selecting unrelated slices do not re-render on job updates", async () => {
    let printerRenders = 0;
    function PrinterOnly() {
      usePrinters();
      printerRenders++;
      return null;
    }
    render(<PrintBridgeProvider store={store}><PrinterOnly /><App /></PrintBridgeProvider>);
    await act(async () => { await store.connect(); await store.pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 }); });
    const before = printerRenders;
    await act(async () => { await store.printPdf({ document: new Uint8Array([1]) }); });
    await waitFor(() => expect(Object.values(store.getSnapshot().jobs)[0]?.state).toBe("completed"), { timeout: 6000 });
    expect(printerRenders).toBe(before);
  });

  it("server rendering produces markup without contacting the agent", () => {
    const html = renderToString(<PrintBridgeProvider options={{ appId: "com.example.ssr", ports: [agent.port] }}><App /></PrintBridgeProvider>);
    expect(html).toContain("disconnected");
    expect(agent.requests.length).toBe(0);
  });

  it("hooks outside the provider fail with a clear message", () => {
    class Boundary extends Component<{ children: ReactNode }, { message: string | null }> {
      override state = { message: null as string | null };
      static getDerivedStateFromError(e: Error) { return { message: e.message }; }
      override render() { return this.state.message ? <p data-testid="err">{this.state.message}</p> : this.props.children; }
    }
    const original = console.error;
    console.error = () => {};
    try {
      render(<Boundary><App /></Boundary>);
    } finally {
      console.error = original;
    }
    expect(screen.getByTestId("err").textContent).toMatch(/PrintBridgeProvider/);
  });
});
