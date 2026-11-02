// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// Svelte 5 bindings over the core PrintBridgeStore. The store lives in component context (per component tree, so
// per SvelteKit SSR request: never shared between users) and connects only in onMount, i.e. in the browser.
// Exposed as Svelte readable stores: usable with `$state`-based runes components via the `$store` syntax.

import { getContext, onMount, setContext } from "svelte";
import { readable, type Readable } from "svelte/store";
import {
  createPrintBridgeStore, jobList,
  type BridgeSnapshot, type ConnectionState, type DocumentJob, type Job, type PairOptions, type PrintBridgeError,
  type PrintBridgeOptions, type PrintBridgeStore, type Printer, type RawJob,
} from "@angelitosystems/print-bridge";

const KEY = Symbol("angelito-print-bridge");

export interface PrintBridgeContext {
  store: PrintBridgeStore;
  /** Whole snapshot as a Svelte store (`$snapshot`). */
  snapshot: Readable<BridgeSnapshot>;
  /** Connection state store. Not named `state`: `$state` is a Svelte 5 rune. */
  connectionState: Readable<ConnectionState>;
  pairingCode: Readable<string | null>;
  error: Readable<PrintBridgeError | null>;
  printers: Readable<readonly Printer[]>;
  defaultPrinter: Readable<Printer | undefined>;
  printersLoading: Readable<boolean>;
  jobs: Readable<Job[]>;
  isPrinting: Readable<boolean>;
  connect: () => Promise<void>;
  pair: (options: PairOptions) => Promise<void>;
  disconnect: () => void;
  refreshPrinters: () => Promise<readonly Printer[]>;
  printPdf: (job: DocumentJob) => Promise<Job>;
  printImage: (job: DocumentJob) => Promise<Job>;
  printRaw: (job: RawJob) => Promise<Job>;
  cancel: (jobId: string) => Promise<Job>;
  /** Store with the live state of one job (tracks it if it was created elsewhere). */
  job: (jobId: string) => Readable<Job | undefined>;
  /** Store with one printer by id. */
  printer: (id: string) => Readable<Printer | undefined>;
}

/** A readable over a slice of the snapshot; emits only when the selected value changes (by reference). */
function select<T>(store: PrintBridgeStore, pick: (s: BridgeSnapshot) => T): Readable<T> {
  return readable(pick(store.getSnapshot()), (set) => {
    let last = pick(store.getSnapshot());
    set(last);
    return store.subscribe(() => {
      const next = pick(store.getSnapshot());
      if (next !== last) set((last = next));
    });
  });
}

export function wrapStore(store: PrintBridgeStore): PrintBridgeContext {
  const jobsObj = select(store, (s) => s.jobs);
  return {
    store,
    snapshot: select(store, (s) => s),
    connectionState: select(store, (s) => s.state),
    pairingCode: select(store, (s) => s.pairingCode),
    error: select(store, (s) => s.error),
    printers: select(store, (s) => s.printers),
    defaultPrinter: select(store, (s) => s.printers.find((p) => p.isDefault)),
    printersLoading: select(store, (s) => s.printersStatus === "loading"),
    jobs: readable<Job[]>([], (set) => jobsObj.subscribe(() => set(jobList(store.getSnapshot())))),
    isPrinting: select(store, (s) => s.submitting > 0),
    connect: () => store.connect(),
    pair: (options) => store.pair(options),
    disconnect: () => store.disconnect(),
    refreshPrinters: () => store.refreshPrinters(),
    printPdf: (job) => store.printPdf(job),
    printImage: (job) => store.printImage(job),
    printRaw: (job) => store.printRaw(job),
    cancel: (jobId) => store.cancel(jobId),
    job: (jobId) => {
      if (!store.getSnapshot().jobs[jobId]) store.watchJob(jobId);
      return select(store, (s) => s.jobs[jobId]);
    },
    printer: (id) => select(store, (s) => s.printers.find((p) => p.id === id)),
  };
}

export interface ProvideOptions {
  /** Connect after mounting (browser only). Default `false`: prefer a user action (Local Network Access). */
  autoConnect?: boolean;
}

/**
 * Creates the store for this component tree (call it in your root layout/component, during initialization).
 * Safe in SvelteKit SSR: each request renders its own tree, and nothing connects on the server.
 */
export function providePrintBridge(options: PrintBridgeOptions | PrintBridgeStore, provide: ProvideOptions = {}): PrintBridgeContext {
  const store = "getSnapshot" in options ? options : createPrintBridgeStore(options);
  const ctx = wrapStore(store);
  setContext(KEY, ctx);
  onMount(() => {
    if (provide.autoConnect) void store.connect();
    return () => store.disconnect();
  });
  return ctx;
}

/** The shared context created by {@link providePrintBridge} in an ancestor component. */
export function getPrintBridge(): PrintBridgeContext {
  const ctx = getContext<PrintBridgeContext | undefined>(KEY);
  if (!ctx) throw new Error("Llame a providePrintBridge() en un componente ancestro antes de usar getPrintBridge().");
  return ctx;
}

export type { BridgeSnapshot, Job, Printer, PrintBridgeStore } from "@angelitosystems/print-bridge";
