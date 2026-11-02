// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// Vue 3 bindings over the core PrintBridgeStore. One store per app (plugin), shared by every composable:
// components never open their own connection. Nothing connects at import time or during SSR.

import {
  computed, getCurrentScope, inject, onScopeDispose, ref, shallowRef, watchEffect, type App, type ComputedRef, type InjectionKey,
  type Ref, type ShallowRef,
} from "vue";
import {
  createPrintBridgeStore, jobList,
  type BridgeSnapshot, type DocumentJob, type Job, type PairOptions, type PrintBridgeError, type PrintBridgeOptions,
  type PrintBridgeStore, type Printer, type RawJob,
} from "@angelitosystems/print-bridge";

export const PrintBridgeKey: InjectionKey<PrintBridgeStore> = Symbol("angelito-print-bridge");

export interface PrintBridgePluginOptions {
  /** Options to create the store, or an existing store. */
  options?: PrintBridgeOptions;
  store?: PrintBridgeStore;
  /** Connect when the plugin is installed (browser only). Default `false`: prefer a user action (Local Network Access). */
  autoConnect?: boolean;
}

/**
 * Vue plugin. In Nuxt register it from a `.client.ts` plugin so the store exists only in the browser
 * (never shared between server requests).
 */
export function createPrintBridgePlugin(config: PrintBridgePluginOptions) {
  return {
    install(app: App) {
      const store = config.store ?? createPrintBridgeStore(config.options!);
      app.provide(PrintBridgeKey, store);
      // Connect on client mount only: server rendering (renderToString/Nuxt SSR) never calls app.mount.
      const mount = app.mount.bind(app);
      app.mount = ((...args: Parameters<typeof mount>) => {
        const vm = mount(...args);
        if (config.autoConnect) void store.connect();
        return vm;
      }) as typeof app.mount;
      const unmount = app.unmount.bind(app);
      app.unmount = () => {
        store.disconnect();
        unmount();
      };
    },
  };
}

function useStore(): PrintBridgeStore {
  const store = inject(PrintBridgeKey, null);
  if (!store) throw new Error("Instale createPrintBridgePlugin() antes de usar los composables de Angelito Print Bridge.");
  return store;
}

/** Reactive snapshot of the shared store; the subscription ends with the calling component/effect scope. */
function useSnapshot(store: PrintBridgeStore): ShallowRef<BridgeSnapshot> {
  const snap = shallowRef(store.getSnapshot());
  const off = store.subscribe(() => (snap.value = store.getSnapshot()));
  if (getCurrentScope()) onScopeDispose(off);
  return snap;
}

export function usePrintBridge() {
  const store = useStore();
  const snap = useSnapshot(store);
  return {
    store,
    bridge: store.bridge,
    state: computed(() => snap.value.state),
    isConnected: computed(() => snap.value.state === "connected"),
    pairingCode: computed(() => snap.value.pairingCode),
    error: computed(() => snap.value.error),
    connect: () => store.connect(),
    pair: (options: PairOptions) => store.pair(options),
    disconnect: () => store.disconnect(),
    forget: () => store.forget(),
  };
}

export function usePrinters() {
  const store = useStore();
  const snap = useSnapshot(store);
  const printers = computed(() => snap.value.printers);
  return {
    printers,
    defaultPrinter: computed(() => printers.value.find((p) => p.isDefault)),
    isLoading: computed(() => snap.value.printersStatus === "loading"),
    error: computed(() => (snap.value.printersStatus === "error" ? snap.value.error : null)),
    refresh: () => store.refreshPrinters(),
  };
}

/** One printer (and its declared capabilities) by id; accepts a ref so it follows a selection. */
export function usePrinter(id: Ref<string | null | undefined> | string): ComputedRef<Printer | undefined> {
  const snap = useSnapshot(useStore());
  return computed(() => {
    const value = typeof id === "string" ? id : id.value;
    return value ? snap.value.printers.find((p) => p.id === value) : undefined;
  });
}

export function usePrintJob(jobId: Ref<string | null | undefined> | string): ComputedRef<Job | undefined> {
  const store = useStore();
  const snap = useSnapshot(store);
  const id = computed(() => (typeof jobId === "string" ? jobId : jobId.value));
  // Track jobs created elsewhere (idempotent).
  watchEffect(() => {
    if (id.value && !store.getSnapshot().jobs[id.value]) store.watchJob(id.value);
  });
  return computed(() => (id.value ? snap.value.jobs[id.value] : undefined));
}

export function usePrintQueue() {
  const snap = useSnapshot(useStore());
  const jobs = computed(() => jobList(snap.value));
  return { jobs, active: computed(() => jobs.value.filter((j) => j.state === "queued" || j.state === "rendering" || j.state === "submitted").length) };
}

export function usePrint() {
  const store = useStore();
  const snap = useSnapshot(store);
  const lastJobId = ref<string | null>(null);
  const error = shallowRef<PrintBridgeError | null>(null);
  const lastJob = usePrintJob(lastJobId);
  async function run(send: () => Promise<Job>): Promise<Job> {
    error.value = null;
    try {
      const job = await send();
      lastJobId.value = job.jobId;
      return job;
    } catch (e) {
      error.value = e as PrintBridgeError;
      throw e;
    }
  }
  return {
    printPdf: (job: DocumentJob) => run(() => store.printPdf(job)),
    printImage: (job: DocumentJob) => run(() => store.printImage(job)),
    printRaw: (job: RawJob) => run(() => store.printRaw(job)),
    cancel: (jobId: string) => store.cancel(jobId),
    isPrinting: computed(() => snap.value.submitting > 0),
    lastJob,
    error,
  };
}

export type { BridgeSnapshot, Job, Printer, PrintBridgeStore } from "@angelitosystems/print-bridge";
