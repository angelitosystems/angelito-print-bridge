// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

import { PrintBridgeError } from "./errors.ts";
import type {
  BinaryInput,
  BridgeEvents,
  CertificateIdentity,
  ConnectionState,
  DocumentJob,
  DocumentKind,
  GrantStorage,
  Job,
  JobState,
  PairOptions,
  Permission,
  PrintBridgeOptions,
  Printer,
  RawJob,
  SubmitBase,
  WaitOptions,
} from "./types.ts";

const DEFAULT_PORTS = [41730, 41731, 41732];
const PRODUCT = "Angelito Print Bridge";
const TERMINAL: ReadonlySet<JobState> = new Set(["completed", "failed", "cancelled", "unknown"]);

/** Grant kept only in memory (lost on reload). */
export function memoryStorage(): GrantStorage {
  const m = new Map<string, string>();
  return { get: (k) => m.get(k) ?? null, set: (k, v) => void m.set(k, v), remove: (k) => void m.delete(k) };
}

function webStorage(kind: "localStorage" | "sessionStorage"): GrantStorage | null {
  try {
    // Browsers only (Node 22+ exposes an experimental localStorage that warns on access); can throw in sandboxed iframes.
    const s = typeof document !== "undefined" ? globalThis[kind] : undefined;
    if (s) return { get: (k) => s.getItem(k), set: (k, v) => s.setItem(k, v), remove: (k) => s.removeItem(k) };
  } catch {
    // storage blocked by privacy settings
  }
  return null;
}

/** Default: per-tab `sessionStorage` (survives reloads, cleared when the tab closes). */
export function sessionGrantStorage(): GrantStorage {
  return webStorage("sessionStorage") ?? memoryStorage();
}

/**
 * Opt-in: persistent `localStorage`, so the user does not re-pair on every visit. Any script running on your origin
 * (e.g. an XSS) could read it; the grant only works from your origin and can be revoked in the agent.
 */
export function localGrantStorage(): GrantStorage {
  return webStorage("localStorage") ?? memoryStorage();
}

function defaultStorage(): GrantStorage {
  return sessionGrantStorage();
}

function randomVerifier(): string {
  const bytes = new Uint8Array(32);
  globalThis.crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function toBlob(input: BinaryInput | string): Blob {
  if (input instanceof Blob) return input;
  if (typeof input === "string") return new Blob([new TextEncoder().encode(input)]);
  return new Blob([input instanceof Uint8Array ? new Uint8Array(input) : input]);
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(new PrintBridgeError("aborted", "Operación cancelada."));
    const t = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    const onAbort = () => {
      clearTimeout(t);
      reject(new PrintBridgeError("aborted", "Operación cancelada."));
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}

type Listener<K extends keyof BridgeEvents> = (payload: BridgeEvents[K]) => void;

/**
 * Cliente de Angelito Print Bridge. Créelo con {@link createPrintBridge}.
 *
 * Flujo: `connect()` → si `state === "unpaired"`, `pair({ permissions })` → imprimir.
 */
export class PrintBridge {
  private readonly appId: string;
  private readonly appName: string;
  private readonly ports: number[];
  private readonly storage: GrantStorage;
  private readonly timeoutMs: number;
  private readonly autoReconnect: boolean;
  private readonly fetchImpl: typeof fetch;
  private readonly WS: typeof WebSocket | undefined;
  private readonly grantKey: string;
  private readonly certificate: CertificateIdentity | undefined;

  private baseUrl: string | null = null;
  private session: string | null = null;
  private ws: WebSocket | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectAttempt = 0;
  private closing = false;
  private _state: ConnectionState = "disconnected";
  private readonly listeners: { [K in keyof BridgeEvents]: Set<Listener<K>> } = { job: new Set(), state: new Set() };

  constructor(options: PrintBridgeOptions) {
    if (!/^[a-z0-9]+(\.[a-z0-9-]+){1,9}$/.test(options.appId))
      throw new PrintBridgeError("invalid_app_id", "appId debe tener formato de dominio inverso, p. ej. com.empresa.app");
    this.appId = options.appId;
    this.appName = options.appName ?? options.appId;
    this.ports = options.ports ?? DEFAULT_PORTS;
    this.storage = options.storage ?? defaultStorage();
    this.timeoutMs = options.requestTimeoutMs ?? 15_000;
    this.autoReconnect = options.autoReconnect ?? true;
    this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.WS = options.WebSocket === null ? undefined : (options.WebSocket ?? globalThis.WebSocket);
    this.grantKey = `angelito-print-bridge:grant:${this.appId}`;
    this.certificate = options.certificate;
  }

  /** Estado actual de la conexión con el agente. */
  get state(): ConnectionState {
    return this._state;
  }

  /** Suscribe a eventos (`job`, `state`). Devuelve la función para desuscribir. */
  on<K extends keyof BridgeEvents>(event: K, listener: Listener<K>): () => void {
    this.listeners[event].add(listener);
    return () => this.listeners[event].delete(listener);
  }

  /**
   * Localiza el agente y restaura la autorización guardada. No lanza si la app aún no está emparejada:
   * en ese caso `state` queda en `"unpaired"`.
   * @throws {PrintBridgeError} `agent_not_found` si el agente no está instalado o en ejecución.
   */
  async connect(): Promise<void> {
    this.closing = false;
    this.setState("connecting");
    try {
      this.baseUrl ??= await this.discover();
      let grant = await this.storage.get(this.grantKey);
      if (!grant && this.certificate) grant = await this.pairWithCertificate();
      if (!grant) return this.setState("unpaired");
      try {
        await this.openSession(grant);
      } catch (e) {
        // A revoked grant can be replaced silently while the certificate is still trusted.
        if (!(e instanceof PrintBridgeError && e.code === "not_paired" && this.certificate)) throw e;
        await this.openSession(await this.pairWithCertificate());
      }
      this.setState("connected");
      this.openEvents();
    } catch (e) {
      if (e instanceof PrintBridgeError && e.code === "not_paired") return this.setState("unpaired");
      this.setState("error");
      throw e;
    }
  }

  /**
   * Solicita autorización al usuario. El agente muestra la solicitud con un código de 6 dígitos que
   * también recibe `onCode`; el usuario confirma que coinciden y aprueba.
   */
  async pair(options: PairOptions): Promise<void> {
    this.baseUrl ??= await this.discover();
    const verifier = randomVerifier();
    const req = await this.request<{ requestId: string; code: string; expiresAt: string }>("POST", "/v1/pair/requests", {
      json: { appId: this.appId, appName: this.appName, permissions: options.permissions, verifier },
      auth: false,
      signal: options.signal,
    });
    options.onCode?.(req.code);
    const deadline = Date.parse(req.expiresAt);
    for (;;) {
      if (Date.now() > deadline) throw new PrintBridgeError("pair_expired", "La solicitud de emparejamiento venció.");
      await sleep(options.pollIntervalMs ?? 1500, options.signal);
      let r: { status: string; grantToken?: string };
      try {
        r = await this.request("POST", `/v1/pair/requests/${req.requestId}/redeem`, { json: { verifier }, auth: false, signal: options.signal });
      } catch (e) {
        if (e instanceof PrintBridgeError && e.code === "pair_denied") throw e;
        if (e instanceof PrintBridgeError && e.code === "pair_request_not_found")
          throw new PrintBridgeError("pair_expired", "La solicitud de emparejamiento venció o fue descartada.");
        throw e;
      }
      if (r.status === "approved" && r.grantToken) {
        await this.storage.set(this.grantKey, r.grantToken);
        await this.openSession(r.grantToken);
        this.setState("connected");
        this.openEvents();
        return;
      }
    }
  }

  /**
   * Emparejamiento firmado (sin intervención del usuario): requiere que el administrador haya confiado en el
   * certificado para este origen. Lo llama `connect()` automáticamente cuando se configura `certificate`.
   */
  async pairWithCertificate(signal?: AbortSignal): Promise<string> {
    const cert = this.certificate;
    if (!cert) throw new PrintBridgeError("no_certificate", "No se configuró `certificate` en createPrintBridge.");
    this.baseUrl ??= await this.discover();
    const origin = cert.origin ?? globalThis.location?.origin;
    if (!origin) throw new PrintBridgeError("no_origin", "No se pudo determinar el origen; indique certificate.origin.");
    const permissions = cert.permissions ?? ["printers:read", "print:document"];
    const timestamp = Date.now();
    const nonce = randomVerifier();
    const message = ["angelito-print-bridge/v1", origin, this.appId, [...permissions].sort().join(","), timestamp, nonce].join("\n");
    const signature = await cert.sign(message);
    const r = await this.request<{ grantToken: string }>("POST", "/v1/pair/certificate", {
      json: { appId: this.appId, appName: this.appName, permissions, certificate: cert.pem, timestamp, nonce, signature },
      auth: false,
      signal,
    });
    await this.storage.set(this.grantKey, r.grantToken);
    return r.grantToken;
  }

  /** Olvida la autorización local (el administrador puede además revocarla en el agente). */
  async forget(): Promise<void> {
    await this.storage.remove(this.grantKey);
    this.session = null;
    this.closeEvents();
    this.setState("unpaired");
  }

  /** Cierra el canal de eventos. La autorización se conserva. */
  disconnect(): void {
    this.closing = true;
    this.closeEvents();
    this.session = null;
    this.setState("disconnected");
  }

  readonly printers = {
    /** Impresoras instaladas en Windows con sus capacidades declaradas. Requiere `printers:read`. */
    list: (signal?: AbortSignal): Promise<Printer[]> => this.request("GET", "/v1/printers", { signal }),
  };

  readonly print = {
    /** Imprime un PDF de forma silenciosa. Requiere `print:document`. */
    pdf: (job: DocumentJob): Promise<Job> => this.submit("pdf", job.document, job, job.options),
    /** Imprime una imagen PNG, JPEG o BMP. Requiere `print:document`. */
    image: (job: DocumentJob): Promise<Job> => this.submit("image", job.document, job, job.options),
    /** Envía bytes sin procesar (ESC/POS, ZPL, EPL). Requiere `print:raw`. */
    raw: (job: RawJob): Promise<Job> => this.submit("raw", job.data, job, job.options),
  };

  readonly jobs = {
    get: (jobId: string, signal?: AbortSignal): Promise<Job> => this.request("GET", `/v1/jobs/${encodeURIComponent(jobId)}`, { signal }),
    list: (limit = 50, signal?: AbortSignal): Promise<Job[]> => this.request("GET", `/v1/jobs?limit=${limit}`, { signal }),
    cancel: async (jobId: string): Promise<Job> =>
      (await this.request<{ job: Job }>("POST", `/v1/jobs/${encodeURIComponent(jobId)}/cancel`)).job,
    /**
     * Espera a que el trabajo alcance `until` (por defecto `completed`) o un estado final.
     * Usa eventos en tiempo real con sondeo de respaldo.
     */
    waitFor: (jobId: string, options: WaitOptions = {}): Promise<Job> => this.waitFor(jobId, options),
  };

  private async submit(kind: DocumentKind, data: BinaryInput | string, base: SubmitBase, options: object | undefined): Promise<Job> {
    const form = new FormData();
    form.append("job", JSON.stringify({ kind, printerId: base.printerId, options, priority: base.priority ?? 0, idempotencyKey: base.idempotencyKey }));
    form.append("document", toBlob(data), "document");
    return this.request("POST", "/v1/jobs", { body: form, signal: base.signal });
  }

  private async waitFor(jobId: string, { until = "completed", timeoutMs = 120_000, signal }: WaitOptions): Promise<Job> {
    const done = (j: Job) => TERMINAL.has(j.state) || (until === "submitted" && j.state === "submitted");
    const controller = new AbortController();
    const onAbort = () => controller.abort();
    signal?.addEventListener("abort", onAbort, { once: true });
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    let resolveEvent!: (j: Job) => void;
    const fromEvent = new Promise<Job>((r) => (resolveEvent = r));
    const off = this.on("job", (j) => j.jobId === jobId && done(j) && resolveEvent(j));
    try {
      const poll = (async () => {
        for (;;) {
          const j = await this.jobs.get(jobId, controller.signal);
          if (done(j)) return j;
          await sleep(2000, controller.signal);
        }
      })();
      return await Promise.race([fromEvent, poll]);
    } catch (e) {
      if (e instanceof PrintBridgeError && e.code === "aborted" && !signal?.aborted)
        throw new PrintBridgeError("timeout", `El trabajo ${jobId} no terminó en ${timeoutMs} ms.`);
      throw e;
    } finally {
      off();
      clearTimeout(timer);
      controller.abort();
      signal?.removeEventListener("abort", onAbort);
    }
  }

  // ---------- transport ----------

  private async discover(): Promise<string> {
    for (const port of this.ports) {
      for (const host of ["127.0.0.1", "[::1]"]) {
        const base = `http://${host}:${port}`;
        try {
          const res = await this.fetchImpl(`${base}/v1/hello`, { signal: AbortSignal.timeout(1500) });
          if (res.ok && ((await res.json()) as { product?: string }).product === PRODUCT) return base;
        } catch {
          // try next candidate
        }
      }
    }
    throw new PrintBridgeError("agent_not_found", "No se encontró Angelito Print Bridge en este equipo. ¿Está instalado y en ejecución?");
  }

  private async openSession(grantToken: string): Promise<void> {
    try {
      const r = await this.request<{ sessionToken: string }>("POST", "/v1/session", { json: { grantToken }, auth: false });
      this.session = r.sessionToken;
    } catch (e) {
      if (e instanceof PrintBridgeError && e.code === "invalid_grant") {
        await this.storage.remove(this.grantKey);
        throw new PrintBridgeError("not_paired", "La autorización fue revocada o no es válida. Vuelva a emparejar.", e.status);
      }
      throw e;
    }
  }

  private async request<T>(
    method: string,
    path: string,
    opts: { json?: unknown; body?: BodyInit; auth?: boolean; signal?: AbortSignal | undefined; retried?: boolean } = {},
  ): Promise<T> {
    if (!this.baseUrl) this.baseUrl = await this.discover();
    const auth = opts.auth ?? true;
    if (auth && !this.session) {
      const grant = await this.storage.get(this.grantKey);
      if (!grant) throw new PrintBridgeError("not_paired", "La aplicación no está emparejada. Llame a pair().");
      await this.openSession(grant);
    }
    const headers: Record<string, string> = {};
    if (auth) headers.Authorization = `Bearer ${this.session}`;
    if (opts.json !== undefined) headers["Content-Type"] = "application/json";
    const timeout = AbortSignal.timeout(this.timeoutMs);
    const signal = opts.signal ? AbortSignal.any([opts.signal, timeout]) : timeout;

    let res: Response;
    try {
      res = await this.fetchImpl(this.baseUrl + path, {
        method,
        headers,
        body: opts.json !== undefined ? JSON.stringify(opts.json) : (opts.body ?? null),
        signal,
      });
    } catch (e) {
      if (opts.signal?.aborted) throw new PrintBridgeError("aborted", "Operación cancelada.");
      if (timeout.aborted) throw new PrintBridgeError("timeout", `Sin respuesta del agente en ${this.timeoutMs} ms.`);
      throw new PrintBridgeError("network_error", `No se pudo contactar al agente: ${(e as Error).message}`);
    }

    if (res.ok) return (await res.json()) as T;
    const body = (await res.json().catch(() => null)) as { error?: { code: string; message: string } } | null;
    const code = body?.error?.code ?? `http_${res.status}`;
    // Sessions are in-memory on the agent (lost on restart / expired): renew once from the stored grant.
    if (res.status === 401 && auth && code === "unauthorized" && !opts.retried) {
      this.session = null;
      const r = await this.request<T>(method, path, { ...opts, retried: true });
      this.openEvents();
      return r;
    }
    throw new PrintBridgeError(code, body?.error?.message ?? res.statusText, res.status);
  }

  // ---------- events ----------

  private openEvents(): void {
    if (!this.WS || !this.session || !this.baseUrl) return;
    this.closeEvents();
    const ws = new this.WS(this.baseUrl.replace(/^http/, "ws") + "/v1/events");
    this.ws = ws;
    const token = this.session;
    ws.onopen = () => ws.send(JSON.stringify({ type: "auth", sessionToken: token }));
    ws.onmessage = (ev: MessageEvent) => {
      const msg = JSON.parse(String(ev.data)) as { type: string; job?: Job };
      if (msg.type === "ready") this.reconnectAttempt = 0;
      if (msg.type === "job" && msg.job) this.emit("job", msg.job);
    };
    ws.onclose = (ev: CloseEvent) => {
      if (this.ws !== ws) return;
      this.ws = null;
      if (this.closing || !this.autoReconnect) return;
      if (ev.code === 4401) this.session = null; // session expired: next attempt renews it from the grant
      const delay = Math.min(30_000, 1000 * 2 ** this.reconnectAttempt++);
      this.reconnectTimer = setTimeout(() => void this.reconnect(), delay);
    };
  }

  private async reconnect(): Promise<void> {
    try {
      if (!this.session) {
        const grant = await this.storage.get(this.grantKey);
        if (!grant) return this.setState("unpaired");
        await this.openSession(grant);
      }
      this.openEvents();
      this.setState("connected");
    } catch (e) {
      if (e instanceof PrintBridgeError && e.code === "not_paired") return this.setState("unpaired");
      this.setState("error");
      const delay = Math.min(30_000, 1000 * 2 ** this.reconnectAttempt++);
      this.reconnectTimer = setTimeout(() => void this.reconnect(), delay);
    }
  }

  private closeEvents(): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    const ws = this.ws;
    this.ws = null;
    ws?.close();
  }

  private setState(s: ConnectionState): void {
    if (s === this._state) return;
    this._state = s;
    this.emit("state", s);
  }

  private emit<K extends keyof BridgeEvents>(event: K, payload: BridgeEvents[K]): void {
    for (const l of this.listeners[event]) {
      try {
        l(payload);
      } catch (e) {
        console.error("[print-bridge] listener error", e);
      }
    }
  }
}

/** Crea un cliente. No abre conexiones hasta llamar a `connect()` o `pair()`. */
export function createPrintBridge(options: PrintBridgeOptions): PrintBridge {
  return new PrintBridge(options);
}

export type { Permission };
