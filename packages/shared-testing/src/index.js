// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// Minimal in-process agent implementing the real /v1 HTTP contract (src/Angelito.PrintBridge.Agent/Endpoints.cs):
// Origin and Bearer checks, pairing with verifier, sessions, permissions, multipart jobs, idempotency and cancellation.
// It has no WebSocket endpoint, so clients exercise their polling fallback. It never touches printers.
import { createServer } from "node:http";
import { randomUUID, randomInt } from "node:crypto";

const DEFAULT_PRINTERS = [
  { id: "Mock Thermal", name: "Mock Thermal", isDefault: true, isVirtual: false, status: ["ready"], capabilities: { duplex: false, color: false, maxCopies: 1, paperSizes: ["80mm"] } },
  { id: "Mock A4", name: "Mock A4", isDefault: false, isVirtual: false, status: ["ready"], capabilities: { duplex: true, color: true, maxCopies: 99, paperSizes: ["A4", "Letter"] } },
];

export async function startMockAgent(options = {}) {
  const { autoApprove = true, jobDelayMs = 30, printers = DEFAULT_PRINTERS, finalState = "completed" } = options;
  const requests = [];
  const pairs = new Map();
  const grants = new Map();
  let sessions = new Map();
  const jobs = new Map();

  const send = (res, status, body, origin) => {
    res.writeHead(status, { "Content-Type": "application/json", ...(origin ? { "Access-Control-Allow-Origin": origin, Vary: "Origin" } : {}) });
    res.end(body === undefined ? "" : JSON.stringify(body));
  };
  const error = (res, status, code, origin) => send(res, status, { error: { code, message: code } }, origin);
  const readBody = (req) => new Promise((resolve) => {
    const chunks = [];
    req.on("data", (d) => chunks.push(d));
    req.on("end", () => resolve(Buffer.concat(chunks)));
  });
  const dto = ({ grantId, idempotencyKey, ...job }) => job;

  const server = createServer(async (req, res) => {
    const url = new URL(req.url, "http://127.0.0.1");
    const origin = req.headers.origin ?? null;
    const auth = req.headers.authorization ?? null;
    requests.push({ method: req.method, path: url.pathname, origin, auth });

    if (req.method === "OPTIONS") {
      res.writeHead(204, { "Access-Control-Allow-Origin": origin ?? "*", "Access-Control-Allow-Methods": "GET, POST, DELETE", "Access-Control-Allow-Headers": "Authorization, Content-Type" });
      return res.end();
    }
    if (url.pathname === "/v1/hello") return send(res, 200, { product: "Angelito Print Bridge", vendor: "Angelito Systems S.A.C.", version: "mock", apiVersion: 1 }, origin);
    if (!origin) return error(res, 403, "origin_required");
    const json = async () => JSON.parse((await readBody(req)).toString() || "{}");

    if (req.method === "POST" && url.pathname === "/v1/pair/requests") {
      const b = await json();
      const id = randomUUID();
      const code = String(randomInt(0, 1e6)).padStart(6, "0");
      pairs.set(id, { code, verifier: b.verifier, origin, permissions: b.permissions, approved: autoApprove });
      return send(res, 201, { requestId: id, code, expiresAt: new Date(Date.now() + 300_000).toISOString(), origin }, origin);
    }
    const redeem = url.pathname.match(/^\/v1\/pair\/requests\/([^/]+)\/redeem$/);
    if (req.method === "POST" && redeem) {
      const p = pairs.get(redeem[1]);
      const b = await json();
      if (!p || p.origin !== origin || p.verifier !== b.verifier) return error(res, 404, "pair_request_not_found", origin);
      if (!p.approved) return send(res, 200, { status: "pending" }, origin);
      pairs.delete(redeem[1]);
      const token = randomUUID();
      const grant = { origin, permissions: p.permissions, id: "g_" + token.slice(0, 8) };
      grants.set(token, grant);
      return send(res, 200, { status: "approved", grantToken: token, grantId: grant.id, permissions: p.permissions }, origin);
    }
    if (req.method === "POST" && url.pathname === "/v1/session") {
      const grant = grants.get((await json()).grantToken);
      if (!grant || grant.origin !== origin) return error(res, 401, "invalid_grant", origin);
      const token = randomUUID();
      sessions.set(token, grant);
      return send(res, 200, { sessionToken: token, expiresAt: new Date(Date.now() + 900_000).toISOString(), grantId: grant.id, permissions: grant.permissions }, origin);
    }

    const session = sessions.get((auth ?? "").replace(/^Bearer /, ""));
    if (!session || session.origin !== origin) return error(res, 401, "unauthorized", origin);
    const can = (permission) => session.permissions.includes(permission);

    if (req.method === "GET" && url.pathname === "/v1/printers") {
      return can("printers:read") ? send(res, 200, printers, origin) : error(res, 403, "permission_denied", origin);
    }
    if (req.method === "POST" && url.pathname === "/v1/jobs") {
      const form = await new Response(await readBody(req), { headers: { "content-type": req.headers["content-type"] } }).formData();
      const meta = JSON.parse(String(form.get("job")));
      const doc = form.get("document");
      if (!can(meta.kind === "raw" ? "print:raw" : "print:document")) return error(res, 403, "permission_denied", origin);
      const printerId = meta.printerId ?? printers.find((p) => p.isDefault)?.id;
      if (!printers.some((p) => p.id === printerId)) return error(res, 404, "printer_not_found", origin);
      if (!doc || doc.size === 0) return error(res, 413, "document_too_large", origin);
      const existing = meta.idempotencyKey && [...jobs.values()].find((j) => j.grantId === session.id && j.idempotencyKey === meta.idempotencyKey);
      if (existing) return send(res, 200, dto(existing), origin);
      const now = new Date().toISOString();
      const job = { jobId: randomUUID(), state: "queued", printerId, kind: meta.kind, attempts: 0, confirmation: "none", createdAt: now, updatedAt: now, grantId: session.id, idempotencyKey: meta.idempotencyKey };
      jobs.set(job.jobId, job);
      setTimeout(() => {
        if (job.state !== "queued") return;
        Object.assign(job, finalState === "completed"
          ? { state: "completed", confirmation: "spooler", attempts: 1 }
          : { state: "failed", attempts: 1, error: { code: "spooler_error", message: "mock failure" } }, { updatedAt: new Date().toISOString() });
      }, jobDelayMs);
      return send(res, 202, dto(job), origin);
    }
    if (req.method === "GET" && url.pathname === "/v1/jobs") {
      return send(res, 200, [...jobs.values()].filter((j) => j.grantId === session.id).map(dto), origin);
    }
    const jobMatch = url.pathname.match(/^\/v1\/jobs\/([^/]+)(\/cancel)?$/);
    if (jobMatch) {
      const job = jobs.get(jobMatch[1]);
      if (!job || job.grantId !== session.id) return error(res, 404, "job_not_found", origin);
      if (req.method === "GET" && !jobMatch[2]) return send(res, 200, dto(job), origin);
      if (req.method === "POST" && jobMatch[2]) {
        if (job.state !== "queued") return error(res, 409, "job_finished", origin);
        Object.assign(job, { state: "cancelled", updatedAt: new Date().toISOString(), error: { code: "cancelled", message: "Cancelado" } });
        return send(res, 200, { result: "cancelled", job: dto(job) }, origin);
      }
    }
    return error(res, 404, "not_found", origin);
  });

  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const port = server.address().port;
  return {
    port,
    url: `http://127.0.0.1:${port}`,
    requests,
    approve(code) {
      for (const p of pairs.values()) if (p.code === code) p.approved = true;
    },
    restart() {
      sessions = new Map();
    },
    close: () => new Promise((resolve) => {
      server.closeAllConnections?.();
      server.close(() => resolve());
    }),
  };
}

export function fetchWithOrigin(origin) {
  return (input, init) => fetch(input, { ...init, headers: { ...(init?.headers ?? {}), Origin: origin } });
}
