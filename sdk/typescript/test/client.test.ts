// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

import { test } from "node:test";
import assert from "node:assert/strict";
import { createPrintBridge, PrintBridgeError } from "../src/index.ts";

const hello = () => new Response(JSON.stringify({ product: "Angelito Print Bridge", apiVersion: 1 }), { status: 200 });
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

test("rejects malformed appId", () => {
  assert.throws(() => createPrintBridge({ appId: "Mi App" }), PrintBridgeError);
});

test("connect reports agent_not_found when nothing listens", async () => {
  const bridge = createPrintBridge({ appId: "com.example.pos", fetch: async () => { throw new TypeError("refused"); } });
  await assert.rejects(bridge.connect(), (e: PrintBridgeError) => e.code === "agent_not_found");
  assert.equal(bridge.state, "error");
});

test("connect without stored grant ends in unpaired, calls without grant fail with not_paired", async () => {
  const bridge = createPrintBridge({ appId: "com.example.pos", fetch: async () => hello() });
  await bridge.connect();
  assert.equal(bridge.state, "unpaired");
  await assert.rejects(bridge.printers.list(), (e: PrintBridgeError) => e.code === "not_paired");
});

test("expired session is renewed once from the stored grant", async () => {
  const calls: string[] = [];
  let sessions = 0;
  const fetchImpl = (async (url: string, init?: RequestInit) => {
    const path = new URL(url).pathname;
    calls.push(`${init?.method ?? "GET"} ${path}`);
    if (path === "/v1/hello") return hello();
    if (path === "/v1/session") return json({ sessionToken: `s${++sessions}` });
    if (path === "/v1/printers") {
      const auth = (init?.headers as Record<string, string>).Authorization;
      return auth === "Bearer s2" ? json([{ id: "P" }]) : json({ error: { code: "unauthorized", message: "x" } }, 401);
    }
    return json({}, 404);
  }) as typeof fetch;
  const store = new Map([["angelito-print-bridge:grant:com.example.pos", "g"]]);
  const bridge = createPrintBridge({
    appId: "com.example.pos",
    fetch: fetchImpl,
    WebSocket: null,
    storage: { get: (k) => store.get(k) ?? null, set: (k, v) => void store.set(k, v), remove: (k) => void store.delete(k) },
  });
  await bridge.connect();
  const printers = await bridge.printers.list();
  assert.deepEqual(printers, [{ id: "P" }]);
  assert.equal(calls.filter((c) => c === "POST /v1/session").length, 2);
});

test("revoked grant is cleared and surfaces as not_paired", async () => {
  const store = new Map([["angelito-print-bridge:grant:com.example.pos", "revoked"]]);
  const bridge = createPrintBridge({
    appId: "com.example.pos",
    fetch: (async (url: string) =>
      new URL(url).pathname === "/v1/hello" ? hello() : json({ error: { code: "invalid_grant", message: "x" } }, 401)) as typeof fetch,
    storage: { get: (k) => store.get(k) ?? null, set: (k, v) => void store.set(k, v), remove: (k) => void store.delete(k) },
  });
  await bridge.connect();
  assert.equal(bridge.state, "unpaired");
  assert.equal(store.size, 0);
});
