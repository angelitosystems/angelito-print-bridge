// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { createSSRApp, defineComponent, h, nextTick } from "vue";
import { renderToString } from "vue/server-renderer";
import { mount, flushPromises } from "@vue/test-utils";
import { startMockAgent, fetchWithOrigin, type MockAgent } from "@angelitosystems/print-bridge-shared-testing";
import { createPrintBridgeStore, memoryStorage, type PrintBridgeStore } from "@angelitosystems/print-bridge";
import { createPrintBridgePlugin, usePrint, usePrintBridge, usePrinter, usePrinters, usePrintQueue } from "../src/index.ts";

const ORIGIN = "https://vue.example.com";
let agent: MockAgent;
let store: PrintBridgeStore;
const listeners = (s: PrintBridgeStore) => (s as unknown as { listeners: Set<unknown> }).listeners.size;

beforeEach(async () => {
  agent = await startMockAgent();
  store = createPrintBridgeStore({ appId: "com.example.vue", ports: [agent.port], fetch: fetchWithOrigin(ORIGIN), WebSocket: null, storage: memoryStorage() });
});
afterEach(async () => {
  store.dispose();
  await agent.close();
});

const Panel = defineComponent({
  setup() {
    const { state, connect, pair } = usePrintBridge();
    const { printers, defaultPrinter } = usePrinters();
    const a4 = usePrinter("Mock A4");
    const { printPdf, lastJob, isPrinting } = usePrint();
    const { jobs } = usePrintQueue();
    return () => h("div", [
      h("p", { id: "state" }, state.value),
      h("p", { id: "printers" }, printers.value.map((p) => p.id).join(",")),
      h("p", { id: "default" }, defaultPrinter.value?.id ?? "-"),
      h("p", { id: "duplex" }, String(a4.value?.capabilities?.duplex)),
      h("p", { id: "job" }, lastJob.value?.state ?? "-"),
      h("p", { id: "queue" }, String(jobs.value.length)),
      h("p", { id: "printing" }, String(isPrinting.value)),
      h("button", { id: "connect", onClick: () => connect() }),
      h("button", { id: "pair", onClick: () => pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 }) }),
      h("button", { id: "print", onClick: () => printPdf({ printerId: "Mock A4", document: new Uint8Array([37, 80, 68, 70]) }) }),
    ]);
  },
});

async function until(check: () => boolean, ms = 8000) {
  const start = Date.now();
  while (!check()) {
    if (Date.now() - start > ms) throw new Error("timeout");
    await new Promise((r) => setTimeout(r, 25));
    await flushPromises();
  }
}

describe("print-bridge-vue", () => {
  it("plugin + composables: full flow over one shared connection", async () => {
    const wrapper = mount(defineComponent({ render: () => [h(Panel), h(Panel)] }), { global: { plugins: [createPrintBridgePlugin({ store })] } });
    expect(agent.requests.length).toBe(0);
    await wrapper.find("#connect").trigger("click");
    await until(() => wrapper.find("#state").text() === "unpaired");
    await wrapper.find("#pair").trigger("click");
    await until(() => wrapper.find("#printers").text() === "Mock Thermal,Mock A4");
    expect(wrapper.findAll("#state").map((w) => w.text())).toEqual(["connected", "connected"]);
    expect(wrapper.find("#default").text()).toBe("Mock Thermal");
    expect(wrapper.find("#duplex").text()).toBe("true");
    await wrapper.find("#print").trigger("click");
    await until(() => wrapper.find("#job").text() === "completed");
    expect(wrapper.findAll("#queue").map((w) => w.text())).toEqual(["1", "1"]);
    expect(agent.requests.filter((r) => r.path === "/v1/session").length).toBe(1);
    wrapper.unmount();
  });

  it("unmounting removes every subscription", async () => {
    const wrapper = mount(Panel, { global: { plugins: [createPrintBridgePlugin({ store })] } });
    await nextTick();
    expect(listeners(store)).toBeGreaterThan(0);
    wrapper.unmount();
    expect(listeners(store)).toBe(0);
  });

  it("SSR renders without touching the agent (Nuxt-safe)", async () => {
    const app = createSSRApp(Panel);
    app.use(createPrintBridgePlugin({ options: { appId: "com.example.ssr", ports: [agent.port] }, autoConnect: true }));
    const html = await renderToString(app);
    expect(html).toContain("disconnected");
    await new Promise((r) => setTimeout(r, 50));
    expect(agent.requests.length).toBe(0);
  });

  it("composables without the plugin fail with a clear message", () => {
    expect(() => mount(Panel)).toThrow(/createPrintBridgePlugin/);
  });
});
