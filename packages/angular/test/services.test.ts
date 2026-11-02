// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { TestBed } from "@angular/core/testing";
import { PLATFORM_ID, provideZonelessChangeDetection } from "@angular/core";
import { startMockAgent, fetchWithOrigin, type MockAgent } from "@angelitosystems/print-bridge-shared-testing";
import { createPrintBridgeStore, memoryStorage, type PrintBridgeStore } from "@angelitosystems/print-bridge";
import { PrintBridgeService, PrinterService, PrintJobService, providePrintBridge } from "../src/public-api.ts";

const ORIGIN = "https://angular.example.com";
let agent: MockAgent;
let store: PrintBridgeStore;
const listeners = (s: PrintBridgeStore) => (s as unknown as { listeners: Set<unknown> }).listeners.size;

async function until(check: () => boolean, ms = 8000) {
  const start = Date.now();
  while (!check()) {
    if (Date.now() - start > ms) throw new Error("timeout");
    await new Promise((r) => setTimeout(r, 25));
  }
}

beforeEach(async () => {
  agent = await startMockAgent();
  store = createPrintBridgeStore({ appId: "com.example.angular", ports: [agent.port], fetch: fetchWithOrigin(ORIGIN), WebSocket: null, storage: memoryStorage() });
});
afterEach(async () => {
  TestBed.resetTestingModule();
  store.dispose();
  await agent.close();
});

describe("print-bridge-angular", () => {
  it("DI shares one service; signals follow connect, pair, printers and jobs", async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), providePrintBridge({ store })] });
    const bridge = TestBed.inject(PrintBridgeService);
    const printers = TestBed.inject(PrinterService);
    const jobs = TestBed.inject(PrintJobService);
    expect(TestBed.inject(PrintBridgeService)).toBe(bridge);
    expect(bridge.state()).toBe("disconnected");
    expect(agent.requests.length).toBe(0);

    await bridge.connect();
    expect(bridge.state()).toBe("unpaired");
    await bridge.pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 });
    expect(bridge.isConnected()).toBe(true);
    expect(printers.printers().map((p) => p.id)).toEqual(["Mock Thermal", "Mock A4"]);
    expect(printers.defaultPrinter()?.id).toBe("Mock Thermal");
    expect(printers.printer("Mock A4")()?.capabilities?.duplex).toBe(true);

    const job = await jobs.printPdf({ printerId: "Mock A4", document: new Uint8Array([37, 80, 68, 70]) });
    const live = jobs.job(job.jobId);
    await until(() => live()?.state === "completed");
    expect(jobs.jobs().length).toBe(1);
    expect(jobs.active()).toBe(0);
    expect(agent.requests.filter((r) => r.path === "/v1/session").length).toBe(1);
  });

  it("destroying the injector unsubscribes and disconnects (DestroyRef)", () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), providePrintBridge({ store })] });
    TestBed.inject(PrintBridgeService);
    expect(listeners(store)).toBe(1);
    TestBed.resetTestingModule();
    expect(listeners(store)).toBe(0);
  });

  it("on the server platform connect() is a no-op and autoConnect never contacts the agent", async () => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), { provide: PLATFORM_ID, useValue: "server" }, providePrintBridge({ store, autoConnect: true })],
    });
    const bridge = TestBed.inject(PrintBridgeService);
    await bridge.connect();
    await new Promise((r) => setTimeout(r, 50));
    expect(bridge.state()).toBe("disconnected");
    expect(agent.requests.length).toBe(0);
  });
});
