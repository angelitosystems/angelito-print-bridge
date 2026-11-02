// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { render, screen, waitFor, cleanup, fireEvent } from "@testing-library/svelte";
import { startMockAgent, fetchWithOrigin, type MockAgent } from "@angelitosystems/print-bridge-shared-testing";
import { createPrintBridgeStore, memoryStorage, type PrintBridgeStore } from "@angelitosystems/print-bridge";
import Root from "./Root.svelte";
import Panel from "./Panel.svelte";

const ORIGIN = "https://svelte.example.com";
let agent: MockAgent;
let store: PrintBridgeStore;
const listeners = (s: PrintBridgeStore) => (s as unknown as { listeners: Set<unknown> }).listeners.size;

beforeEach(async () => {
  agent = await startMockAgent();
  store = createPrintBridgeStore({ appId: "com.example.svelte", ports: [agent.port], fetch: fetchWithOrigin(ORIGIN), WebSocket: null, storage: memoryStorage() });
});
afterEach(async () => {
  cleanup();
  store.dispose();
  await agent.close();
});

describe("print-bridge-svelte", () => {
  it("context + stores: full flow, two components share one connection", async () => {
    render(Root, { store });
    expect(agent.requests.length).toBe(0);
    await fireEvent.click(screen.getAllByText("connect")[0]!);
    await waitFor(() => expect(screen.getAllByTestId("state").map((e) => e.textContent)).toEqual(["unpaired", "unpaired"]));
    await fireEvent.click(screen.getAllByText("pair")[0]!);
    await waitFor(() => expect(screen.getAllByTestId("printers")[1]!.textContent).toBe("Mock Thermal,Mock A4"));
    expect(screen.getAllByTestId("default")[0]!.textContent).toBe("Mock Thermal");
    expect(screen.getAllByTestId("duplex")[0]!.textContent).toBe("true");
    await fireEvent.click(screen.getAllByText("print")[0]!);
    await waitFor(() => expect(screen.getAllByTestId("job")[1]!.textContent).toBe("completed"), { timeout: 6000 });
    expect(agent.requests.filter((r) => r.path === "/v1/session").length).toBe(1);
  });

  it("destroying the tree removes every store subscription", async () => {
    const { unmount } = render(Root, { store, panels: 1 });
    expect(listeners(store)).toBeGreaterThan(0);
    unmount();
    expect(listeners(store)).toBe(0);
  });

  it("getPrintBridge without providePrintBridge fails clearly", () => {
    expect(() => render(Panel)).toThrow(/providePrintBridge/);
  });
});
