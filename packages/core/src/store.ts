// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

import { createPrintBridge, type PrintBridge } from "./client.ts";
import { PrintBridgeError } from "./errors.ts";
import type { ConnectionState, DocumentJob, Job, JobState, PairOptions, PrintBridgeOptions, Printer, RawJob } from "./types.ts";

export type LoadStatus = "idle" | "loading" | "ready" | "error";

/** Immutable view of the shared bridge state. A new object is produced on every change (safe for `useSyncExternalStore`). */
export interface BridgeSnapshot {
  readonly state: ConnectionState;
  /** Code to show the user while `pair()` waits for approval in the agent. */
  readonly pairingCode: string | null;
  readonly printers: readonly Printer[];
  readonly printersStatus: LoadStatus;
  /** Jobs submitted or observed during this session, by id. */
  readonly jobs: Readonly<Record<string, Job>>;
  /** Number of submissions in flight (upload until the agent accepts the job). */
  readonly submitting: number;
  readonly error: PrintBridgeError | null;
}

const TERMINAL: ReadonlySet<JobState> = new Set(["completed", "failed", "cancelled", "unknown"]);

function toError(e: unknown): PrintBridgeError {
  return e instanceof PrintBridgeError ? e : new PrintBridgeError("unexpected_error", e instanceof Error ? e.message : String(e));
}

/**
 * Shared, framework-agnostic state over one {@link PrintBridge}. Framework integrations (React, Vue, Svelte, Angular)
 * subscribe to it instead of re-implementing the protocol, so many components share a single agent connection.
 * Creating a store never connects: call {@link connect} (e.g. after a user gesture).
 */
export class PrintBridgeStore {
  readonly bridge: PrintBridge;
  private snapshot: BridgeSnapshot;
  private readonly listeners = new Set<() => void>();
  private readonly offs: Array<() => void>;
  private readonly watching = new Set<string>();
  private disposed = false;

  constructor(bridge: PrintBridge) {
    this.bridge = bridge;
    this.snapshot = { state: bridge.state, pairingCode: null, printers: [], printersStatus: "idle", jobs: {}, submitting: 0, error: null };
    this.offs = [
      bridge.on("state", (state) => this.set({ state })),
      bridge.on("job", (job) => this.putJob(job)),
    ];
  }

  /** Current snapshot. Bound so it can be passed directly as `getSnapshot`. */
  readonly getSnapshot = (): BridgeSnapshot => this.snapshot;

  /** Registers a change listener; returns the unsubscribe function. Bound for `useSyncExternalStore`. */
  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Locates the agent and restores a stored authorization. Loads printers when connected. Never throws. */
  async connect(): Promise<void> {
    try {
      this.set({ error: null });
      await this.bridge.connect();
      if (this.bridge.state === "connected") await this.refreshPrinters();
    } catch (e) {
      this.set({ error: toError(e) });
    }
  }

  /** Starts pairing; `pairingCode` is set until the user approves (or denies) in the agent. */
  async pair(options: PairOptions): Promise<void> {
    try {
      this.set({ error: null });
      await this.bridge.pair({
        ...options,
        onCode: (code) => {
          this.set({ pairingCode: code });
          options.onCode?.(code);
        },
      });
      this.set({ pairingCode: null });
      await this.refreshPrinters();
    } catch (e) {
      this.set({ pairingCode: null, error: toError(e) });
      throw toError(e);
    }
  }

  async refreshPrinters(signal?: AbortSignal): Promise<readonly Printer[]> {
    this.set({ printersStatus: "loading" });
    try {
      const printers = await this.bridge.printers.list(signal);
      this.set({ printers, printersStatus: "ready" });
      return printers;
    } catch (e) {
      // An app paired without `printers:read` simply has no printer list.
      if (e instanceof PrintBridgeError && e.code === "permission_denied") {
        this.set({ printers: [], printersStatus: "ready" });
        return [];
      }
      this.set({ printersStatus: "error", error: toError(e) });
      throw toError(e);
    }
  }

  /** Submits a document and follows it to a final state in the background. Resolves when the agent accepts it. */
  printPdf(job: DocumentJob): Promise<Job> {
    return this.submit(() => this.bridge.print.pdf(job));
  }

  printImage(job: DocumentJob): Promise<Job> {
    return this.submit(() => this.bridge.print.image(job));
  }

  printRaw(job: RawJob): Promise<Job> {
    return this.submit(() => this.bridge.print.raw(job));
  }

  async cancel(jobId: string): Promise<Job> {
    const job = await this.bridge.jobs.cancel(jobId);
    this.putJob(job);
    return job;
  }

  /** Starts tracking a job created elsewhere (e.g. restored from your backend). */
  watchJob(jobId: string): void {
    if (this.watching.has(jobId) || this.disposed) return;
    this.watching.add(jobId);
    void this.bridge.jobs
      .waitFor(jobId, { timeoutMs: 10 * 60_000 })
      .then((job) => this.putJob(job))
      .catch((e) => {
        if (!(e instanceof PrintBridgeError && e.code === "aborted")) this.set({ error: toError(e) });
      })
      .finally(() => this.watching.delete(jobId));
  }

  disconnect(): void {
    this.bridge.disconnect();
  }

  /** Forgets the stored authorization (the agent can also revoke it). */
  async forget(): Promise<void> {
    await this.bridge.forget();
    this.set({ printers: [], printersStatus: "idle" });
  }

  /** Stops listening and closes the connection. The store is unusable afterwards. */
  dispose(): void {
    this.disposed = true;
    for (const off of this.offs) off();
    this.listeners.clear();
    this.bridge.disconnect();
  }

  private async submit(send: () => Promise<Job>): Promise<Job> {
    this.set({ submitting: this.snapshot.submitting + 1, error: null });
    try {
      const job = await send();
      this.putJob(job);
      if (!TERMINAL.has(job.state)) this.watchJob(job.jobId);
      return job;
    } catch (e) {
      this.set({ error: toError(e) });
      throw toError(e);
    } finally {
      this.set({ submitting: this.snapshot.submitting - 1 });
    }
  }

  private putJob(job: Job): void {
    const prev = this.snapshot.jobs[job.jobId];
    if (prev && prev.updatedAt === job.updatedAt && prev.state === job.state) return;
    this.set({ jobs: { ...this.snapshot.jobs, [job.jobId]: job } });
  }

  private set(patch: Partial<BridgeSnapshot>): void {
    if (this.disposed) return;
    const next = { ...this.snapshot, ...patch };
    if ((Object.keys(patch) as Array<keyof BridgeSnapshot>).every((k) => next[k] === this.snapshot[k])) return;
    this.snapshot = next;
    for (const l of [...this.listeners]) l();
  }
}

/** Creates a store with its own client. One store per app (or per SSR request) — never a module-level singleton on servers. */
export function createPrintBridgeStore(options: PrintBridgeOptions | PrintBridge): PrintBridgeStore {
  return new PrintBridgeStore("connect" in options ? options : createPrintBridge(options));
}

/** Jobs of a snapshot, newest first. */
export function jobList(snapshot: BridgeSnapshot): Job[] {
  return Object.values(snapshot.jobs).sort((a, b) => b.createdAt.localeCompare(a.createdAt));
}
