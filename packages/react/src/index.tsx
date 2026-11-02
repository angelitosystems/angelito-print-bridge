"use client";
// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// React bindings over the core PrintBridgeStore: one shared agent connection per <PrintBridgeProvider>.
// Client-only ("use client"): safe to import from Next.js Server Components, never connects during SSR.

import {
  createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode,
} from "react";
import {
  createPrintBridgeStore, jobList,
  type BridgeSnapshot, type ConnectionState, type DocumentJob, type Job, type PairOptions, type PrintBridge,
  type PrintBridgeError, type PrintBridgeOptions, type PrintBridgeStore, type Printer, type RawJob,
} from "@angelitosystems/print-bridge";

const StoreContext = createContext<PrintBridgeStore | null>(null);

export type PrintBridgeProviderProps = {
  children?: ReactNode;
  /** Connect on mount. Default `false`: browsers may show a Local Network Access prompt, so prefer a user action. */
  autoConnect?: boolean;
} & ({ options: PrintBridgeOptions; store?: never } | { store: PrintBridgeStore; options?: never });

/**
 * Provides one shared {@link PrintBridgeStore} to the tree. Pass `options` (the provider creates and owns the store)
 * or an existing `store`. Unmounting disconnects; the connection is never opened during server rendering.
 */
export function PrintBridgeProvider(props: PrintBridgeProviderProps) {
  const [store] = useState(() => props.store ?? createPrintBridgeStore(props.options!));
  const { autoConnect = false } = props;
  useEffect(() => {
    if (autoConnect) void store.connect();
    // Disconnect (reversible) instead of dispose so React Strict Mode's mount→unmount→mount keeps working.
    return () => store.disconnect();
  }, [store, autoConnect]);
  return <StoreContext.Provider value={store}>{props.children}</StoreContext.Provider>;
}

/** The shared store and client. Throws outside a {@link PrintBridgeProvider}. */
export function usePrintBridge(): { store: PrintBridgeStore; bridge: PrintBridge } {
  const store = useContext(StoreContext);
  if (!store) throw new Error("usePrintBridge debe usarse dentro de <PrintBridgeProvider>.");
  return useMemo(() => ({ store, bridge: store.bridge }), [store]);
}

/** Subscribes to a slice of the snapshot; re-renders only when the selected value changes (by reference). */
export function usePrintBridgeSelector<T>(selector: (s: BridgeSnapshot) => T): T {
  const { store } = usePrintBridge();
  const get = () => selector(store.getSnapshot());
  return useSyncExternalStore(store.subscribe, get, get);
}

export interface PrintBridgeConnection {
  state: ConnectionState;
  isConnected: boolean;
  /** 6-digit code to show while waiting for approval in the agent (`null` otherwise). */
  pairingCode: string | null;
  error: PrintBridgeError | null;
  connect: () => Promise<void>;
  pair: (options: PairOptions) => Promise<void>;
  disconnect: () => void;
  forget: () => Promise<void>;
}

export function usePrintBridgeConnection(): PrintBridgeConnection {
  const { store } = usePrintBridge();
  const state = usePrintBridgeSelector((s) => s.state);
  const pairingCode = usePrintBridgeSelector((s) => s.pairingCode);
  const error = usePrintBridgeSelector((s) => s.error);
  return useMemo(() => ({
    state, isConnected: state === "connected", pairingCode, error,
    connect: () => store.connect(),
    pair: (options: PairOptions) => store.pair(options),
    disconnect: () => store.disconnect(),
    forget: () => store.forget(),
  }), [store, state, pairingCode, error]);
}

export interface PrintersResult {
  printers: readonly Printer[];
  defaultPrinter: Printer | undefined;
  isLoading: boolean;
  error: PrintBridgeError | null;
  refresh: () => Promise<readonly Printer[]>;
}

/** Printers installed in Windows (requires `printers:read`). Loaded automatically after connecting/pairing. */
export function usePrinters(): PrintersResult {
  const { store } = usePrintBridge();
  const printers = usePrintBridgeSelector((s) => s.printers);
  const status = usePrintBridgeSelector((s) => s.printersStatus);
  const error = usePrintBridgeSelector((s) => (s.printersStatus === "error" ? s.error : null));
  const refresh = useCallback(() => store.refreshPrinters(), [store]);
  return useMemo(() => ({
    printers, defaultPrinter: printers.find((p) => p.isDefault), isLoading: status === "loading", error, refresh,
  }), [printers, status, error, refresh]);
}

/** One printer and its declared capabilities, by id. */
export function usePrinter(id: string | null | undefined): Printer | undefined {
  return usePrintBridgeSelector((s) => (id ? s.printers.find((p) => p.id === id) : undefined));
}

/** Live state of a job; starts tracking it if it was created elsewhere. */
export function usePrintJob(jobId: string | null | undefined): Job | undefined {
  const { store } = usePrintBridge();
  const job = usePrintBridgeSelector((s) => (jobId ? s.jobs[jobId] : undefined));
  useEffect(() => {
    if (jobId && !store.getSnapshot().jobs[jobId]) store.watchJob(jobId);
  }, [store, jobId]);
  return job;
}

/** Jobs of this session, newest first, plus how many are still in progress. */
export function usePrintQueue(): { jobs: Job[]; active: number } {
  const jobs = usePrintBridgeSelector((s) => s.jobs);
  return useMemo(() => {
    const list = jobList({ jobs } as BridgeSnapshot);
    return { jobs: list, active: list.filter((j) => j.state === "queued" || j.state === "rendering" || j.state === "submitted").length };
  }, [jobs]);
}

export interface PrintActions {
  printPdf: (job: DocumentJob) => Promise<Job>;
  printImage: (job: DocumentJob) => Promise<Job>;
  printRaw: (job: RawJob) => Promise<Job>;
  cancel: (jobId: string) => Promise<Job>;
  /** True while a document is being uploaded to the agent. */
  isPrinting: boolean;
  /** The last job submitted from this component (live state). */
  lastJob: Job | undefined;
  error: PrintBridgeError | null;
}

export function usePrint(): PrintActions {
  const { store } = usePrintBridge();
  const [lastJobId, setLastJobId] = useState<string | null>(null);
  const [error, setError] = useState<PrintBridgeError | null>(null);
  const isPrinting = usePrintBridgeSelector((s) => s.submitting > 0);
  const lastJob = usePrintJob(lastJobId);
  const run = useCallback(async (send: () => Promise<Job>) => {
    setError(null);
    try {
      const job = await send();
      setLastJobId(job.jobId);
      return job;
    } catch (e) {
      setError(e as PrintBridgeError);
      throw e;
    }
  }, []);
  return useMemo(() => ({
    printPdf: (job: DocumentJob) => run(() => store.printPdf(job)),
    printImage: (job: DocumentJob) => run(() => store.printImage(job)),
    printRaw: (job: RawJob) => run(() => store.printRaw(job)),
    cancel: (jobId: string) => store.cancel(jobId),
    isPrinting, lastJob, error,
  }), [store, run, isPrinting, lastJob, error]);
}

export type { BridgeSnapshot, Job, Printer, PrintBridgeStore } from "@angelitosystems/print-bridge";
