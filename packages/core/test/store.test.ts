// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

import { test } from "node:test";
import assert from "node:assert/strict";
import { startMockAgent, fetchWithOrigin, type MockAgent } from "@angelitosystems/print-bridge-shared-testing";
import { createPrintBridgeStore, jobList, memoryStorage, type BridgeSnapshot, type PrintBridgeStore } from "../src/index.ts";

const ORIGIN = "https://store.example.com";

function makeStore(agent: MockAgent): PrintBridgeStore {
  return createPrintBridgeStore({ appId: "com.example.store", ports: [agent.port], fetch: fetchWithOrigin(ORIGIN), WebSocket: null, storage: memoryStorage() });
}

async function until(store: PrintBridgeStore, predicate: (s: BridgeSnapshot) => boolean, ms = 5000): Promise<BridgeSnapshot> {
  if (predicate(store.getSnapshot())) return store.getSnapshot();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { off(); reject(new Error("timeout waiting for snapshot")); }, ms);
    const off = store.subscribe(() => {
      if (predicate(store.getSnapshot())) { clearTimeout(timer); off(); resolve(store.getSnapshot()); }
    });
  });
}

test("creating a store opens no connection", async () => {
  const agent = await startMockAgent();
  const store = makeStore(agent);
  assert.equal(store.getSnapshot().state, "disconnected");
  assert.equal(agent.requests.length, 0);
  store.dispose();
  await agent.close();
});

test("connect → unpaired → pair (pending code until approved) → printers loaded", async () => {
  const agent = await startMockAgent({ autoApprove: false });
  const store = makeStore(agent);
  await store.connect();
  assert.equal(store.getSnapshot().state, "unpaired");

  const pairing = store.pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 20 });
  const withCode = await until(store, (s) => s.pairingCode !== null);
  agent.approve(withCode.pairingCode!);
  await pairing;
  const s = store.getSnapshot();
  assert.equal(s.state, "connected");
  assert.equal(s.pairingCode, null);
  assert.equal(s.printersStatus, "ready");
  assert.deepEqual(s.printers.map((p) => p.id), ["Mock Thermal", "Mock A4"]);
  assert.ok(agent.requests.every((r) => r.path === "/v1/hello" || r.origin === ORIGIN));
  store.dispose();
  await agent.close();
});

test("printPdf tracks the job to a final state; snapshots are immutable", async () => {
  const agent = await startMockAgent();
  const store = makeStore(agent);
  await store.connect();
  await store.pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 });
  const before = store.getSnapshot();
  const job = await store.printPdf({ printerId: "Mock A4", document: new Uint8Array([37, 80, 68, 70]) });
  assert.notEqual(store.getSnapshot(), before);
  assert.equal(before.jobs[job.jobId], undefined);
  const done = await until(store, (s) => s.jobs[job.jobId]?.state === "completed");
  assert.equal(done.submitting, 0);
  assert.equal(jobList(done)[0]!.jobId, job.jobId);
  store.dispose();
  await agent.close();
});

test("permission errors surface as typed errors in the snapshot", async () => {
  const agent = await startMockAgent();
  const store = makeStore(agent);
  await store.connect();
  await store.pair({ permissions: ["printers:read"], pollIntervalMs: 10 });
  await assert.rejects(store.printRaw({ data: "x" }), (e: { code: string }) => e.code === "permission_denied");
  assert.equal(store.getSnapshot().error?.code, "permission_denied");
  assert.equal(store.getSnapshot().submitting, 0);
  store.dispose();
  await agent.close();
});

test("cancel a queued job; sessions renew after an agent restart", async () => {
  const agent = await startMockAgent({ jobDelayMs: 5000 });
  const store = makeStore(agent);
  await store.connect();
  await store.pair({ permissions: ["print:document"], pollIntervalMs: 10 });
  const job = await store.printPdf({ document: new Uint8Array([1]) });
  agent.restart();
  const cancelled = await store.cancel(job.jobId);
  assert.equal(cancelled.state, "cancelled");
  assert.equal(store.getSnapshot().jobs[job.jobId]!.state, "cancelled");
  store.dispose();
  await agent.close();
});

test("missing agent: connect never throws and reports agent_not_found", async () => {
  const store = createPrintBridgeStore({ appId: "com.example.store", ports: [1], fetch: fetchWithOrigin(ORIGIN), WebSocket: null });
  await store.connect();
  assert.equal(store.getSnapshot().error?.code, "agent_not_found");
  assert.equal(store.getSnapshot().state, "error");
  store.dispose();
});

test("dispose stops notifications and listeners can unsubscribe", async () => {
  const agent = await startMockAgent();
  const store = makeStore(agent);
  let calls = 0;
  const off = store.subscribe(() => calls++);
  await store.connect();
  const afterConnect = calls;
  assert.ok(afterConnect > 0);
  off();
  await store.connect();
  assert.equal(calls, afterConnect);
  store.subscribe(() => calls++);
  store.dispose();
  await store.connect();
  assert.equal(calls, afterConnect);
  await agent.close();
});
