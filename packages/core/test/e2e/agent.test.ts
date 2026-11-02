// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

/**
 * End-to-end: real agent process + real SQLite + Windows PDF engine, using the "virtual:file" printer
 * so no paper is used. Requires a prior `dotnet build` of the solution.
 *   npm run test:e2e
 */
import { test, before, after } from "node:test";
import assert from "node:assert/strict";
import { spawn, execFile, type ChildProcess } from "node:child_process";
import { createSign } from "node:crypto";
import { mkdtempSync, readFileSync, readdirSync, existsSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { promisify } from "node:util";
import { createPrintBridge, escpos, PrintBridgeError, type PrintBridge } from "../../src/index.ts";

const ROOT = resolve(import.meta.dirname, "../../../..");
const EXE = process.env.APB_AGENT_EXE ?? join(ROOT, "src/Angelito.PrintBridge.Agent/bin/Debug/net10.0-windows10.0.19041.0/AngelitoPrintBridge.exe");
const PORT = 41790 + Math.floor(Math.random() * 100);
const ORIGIN = "https://e2e.angelitosystems.net";
const DATA = mkdtempSync(join(tmpdir(), "apb-e2e-"));
const env = { ...process.env, Bridge__DataDirectory: DATA, Bridge__Port: String(PORT), Bridge__VirtualPrinterEnabled: "true" };
const run = promisify(execFile);
let agent: ChildProcess;

// Node has no browser Origin; emulate what a page at ORIGIN sends.
const fetchWithOrigin: typeof fetch = (input, init) =>
  fetch(input, { ...init, headers: { ...(init?.headers as Record<string, string>), Origin: ORIGIN } });
class OriginWebSocket extends WebSocket {
  constructor(url: string | URL) {
    super(url, { headers: { Origin: ORIGIN } } as unknown as string[]);
  }
}

async function startAgent(): Promise<void> {
  agent = spawn(EXE, [], { env, stdio: "ignore" });
  for (let i = 0; i < 60; i++) {
    try {
      if ((await fetch(`http://127.0.0.1:${PORT}/v1/hello`)).ok) return;
    } catch {}
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error("agent did not start");
}

async function hardKill(): Promise<void> {
  const exited = new Promise((r) => agent.once("exit", r));
  agent.kill("SIGKILL"); // TerminateProcess: simulates a crash, no graceful shutdown
  await exited;
}

const cli = (...args: string[]) => run(EXE, args, { env });

function newBridge(): PrintBridge {
  return createPrintBridge({ appId: "com.angelitosystems.e2e", appName: "E2E", ports: [PORT], fetch: fetchWithOrigin, WebSocket: OriginWebSocket, storage });
}
const grants = new Map<string, string>();
const storage = { get: (k: string) => grants.get(k) ?? null, set: (k: string, v: string) => void grants.set(k, v), remove: (k: string) => void grants.delete(k) };
let bridge: PrintBridge;

before(async () => {
  assert.ok(existsSync(EXE), `build the agent first: ${EXE}`);
  await startAgent();
  bridge = newBridge();
});

after(async () => {
  bridge?.disconnect();
  if (agent && agent.exitCode === null) await hardKill();
  rmSync(DATA, { recursive: true, force: true });
});

test("unpaired app cannot print; pairing requires local approval with matching code", async () => {
  await bridge.connect();
  assert.equal(bridge.state, "unpaired");
  await assert.rejects(bridge.printers.list(), (e: PrintBridgeError) => e.code === "not_paired");

  await bridge.pair({
    permissions: ["printers:read", "print:document", "print:raw"],
    pollIntervalMs: 200,
    onCode: async (code) => {
      const { stdout } = await cli("requests");
      assert.match(stdout, new RegExp(`"code":"${code}"`)); // the user sees the same code in the agent
      assert.match(stdout, /"origin":"https:\/\/e2e\.angelitosystems\.net"/);
      await cli("approve", code);
    },
  });
  assert.equal(bridge.state, "connected");
});

test("lists printers including the virtual printer", async () => {
  const printers = await bridge.printers.list();
  assert.ok(printers.some((p) => p.id === "virtual:file" && p.isVirtual));
});

test("silent PDF print is rendered by the Windows engine and completes", async () => {
  const pdf = readFileSync(join(ROOT, "tests/fixtures/sample.pdf"));
  const job = await bridge.print.pdf({ printerId: "virtual:file", document: new Blob([pdf]) });
  const done = await bridge.jobs.waitFor(job.jobId, { timeoutMs: 20_000 });
  assert.equal(done.state, "completed");
  assert.equal(done.confirmation, "spooler");
  assert.ok(existsSync(join(DATA, "virtual-output", `${job.jobId.replace(/-/g, "")}-p1.png`)));
});

test("ESC/POS ticket bytes reach the printer unchanged and events stream in real time", async () => {
  const ticket = escpos().init().align("center").bold().text("ANGELITO SYSTEMS").newline().bold(false)
    .line("Café", "S/ 8.50", 32).qr("20614770229|03|B001|1").feed(3).cut().openDrawer().build();
  const seen: string[] = [];
  const off = bridge.on("job", (j) => seen.push(j.state));
  const job = await bridge.print.raw({ printerId: "virtual:file", data: ticket, idempotencyKey: "ticket-0001" });
  const done = await bridge.jobs.waitFor(job.jobId, { timeoutMs: 20_000 });
  off();
  assert.equal(done.state, "completed");
  const written = readFileSync(join(DATA, "virtual-output", `${job.jobId.replace(/-/g, "")}.bin`));
  assert.deepEqual(new Uint8Array(written), ticket);
  assert.ok(seen.includes("completed"), `events received: ${seen.join(",")}`);

  const again = await bridge.print.raw({ printerId: "virtual:file", data: ticket, idempotencyKey: "ticket-0001" });
  assert.equal(again.jobId, job.jobId, "same idempotency key must not print twice");
});

test("corrupt PDF fails with invalid_document, unknown printer is rejected", async () => {
  const bad = await bridge.print.pdf({ printerId: "virtual:file", document: readFileSync(join(ROOT, "tests/fixtures/corrupt.pdf")) });
  const done = await bridge.jobs.waitFor(bad.jobId, { timeoutMs: 20_000 });
  assert.equal(done.state, "failed");
  assert.equal(done.error?.code, "invalid_document");
  await assert.rejects(bridge.print.pdf({ printerId: "No existe", document: new Uint8Array([1]) }), (e: PrintBridgeError) => e.code === "printer_not_found");
});

test("after an agent crash and restart the app keeps working without re-pairing; history persists", async () => {
  const before = await bridge.jobs.list();
  await hardKill();
  await startAgent();
  const after = await bridge.jobs.list(); // session is renewed transparently from the stored grant
  assert.deepEqual(after.map((j) => j.jobId).sort(), before.map((j) => j.jobId).sort());
});

test("revocation in the agent cuts access immediately", async () => {
  const { stdout } = await cli("grants");
  const id = (JSON.parse(stdout) as { id: string }[])[0]!.id;
  await cli("revoke", id);
  await assert.rejects(bridge.printers.list(), (e: PrintBridgeError) => e.code === "not_paired");
});

test("audit trail records operations without document content or tokens", async () => {
  const { stdout } = await cli("audit");
  const events = (JSON.parse(stdout) as { event: string; detail?: string }[]).map((a) => a.event);
  for (const e of ["pair.requested", "pair.approved", "job.created", "job.completed", "job.failed", "grant.revoked"]) assert.ok(events.includes(e), e);
  assert.doesNotMatch(stdout, /ANGELITO SYSTEMS|grantToken|sessionToken/);
  const logs = readdirSync(join(DATA, "logs")).map((f) => readFileSync(join(DATA, "logs", f), "utf8")).join("\n");
  assert.ok(logs.length > 0);
  for (const secret of grants.values()) assert.ok(!logs.includes(secret));
  assert.doesNotMatch(logs, /ANGELITO SYSTEMS/);
});

test("certificate identity: trusted cert pairs without approval, backend signs, revocation blocks it", async () => {
  const dir = join(DATA, "certs");
  await cli("cert-generate", ORIGIN, "E2E Cert", dir, "printers:read,print:document");
  const pem = readFileSync(join(dir, "certificate.pem"), "utf8");
  const privateKey = readFileSync(join(dir, "private-key.pem"), "utf8");
  // What the application's backend does: sign with the private key it keeps on the server.
  const sign = async (message: string) => createSign("SHA256").update(message).sign(privateKey, "base64");
  const store = new Map<string, string>();
  const certBridge = createPrintBridge({
    appId: "com.angelitosystems.cert",
    appName: "Cert App",
    ports: [PORT],
    fetch: fetchWithOrigin,
    WebSocket: null,
    storage: { get: (k) => store.get(k) ?? null, set: (k, v) => void store.set(k, v), remove: (k) => void store.delete(k) },
    certificate: { pem, sign, origin: ORIGIN },
  });
  await certBridge.connect();
  assert.equal(certBridge.state, "connected");
  const job = await certBridge.print.pdf({ printerId: "virtual:file", document: readFileSync(join(ROOT, "tests/fixtures/sample.pdf")) });
  assert.equal((await certBridge.jobs.waitFor(job.jobId, { timeoutMs: 20_000 })).state, "completed");
  await assert.rejects(certBridge.print.raw({ printerId: "virtual:file", data: "x" }), (e: PrintBridgeError) => e.code === "permission_denied");

  const { stdout } = await cli("certs");
  const fp = (JSON.parse(stdout) as { fingerprint: string }[])[0]!.fingerprint;
  await cli("cert-revoke", fp);
  await assert.rejects(certBridge.printers.list(), (e: PrintBridgeError) => e.code === "untrusted_certificate" || e.code === "not_paired");
  certBridge.disconnect();
});
