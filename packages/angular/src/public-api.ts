// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// Angular bindings over the core PrintBridgeStore: one store per application injector, exposed as signals.
// No decorators (factory providers + inject()), zoneless-friendly, SSR-safe (connects only in the browser).

import {
  DestroyRef, InjectionToken, PLATFORM_ID, computed, inject, makeEnvironmentProviders, provideAppInitializer, signal,
  type EnvironmentProviders, type Signal,
} from "@angular/core";
import { isPlatformBrowser } from "@angular/common";
import {
  createPrintBridgeStore, jobList,
  type BridgeSnapshot, type ConnectionState, type DocumentJob, type Job, type PairOptions, type PrintBridgeError,
  type PrintBridgeOptions, type PrintBridgeStore, type Printer, type RawJob,
} from "@angelitosystems/print-bridge";

export interface PrintBridgeConfig {
  /** Options to create the store, or an existing store. */
  options?: PrintBridgeOptions;
  store?: PrintBridgeStore;
  /** Connect at application start (browser only). Default `false`: prefer a user action (Local Network Access). */
  autoConnect?: boolean;
}

export const PRINT_BRIDGE_CONFIG = new InjectionToken<PrintBridgeConfig>("PRINT_BRIDGE_CONFIG");

/** Shared connection and state. Provided once by {@link providePrintBridge}; inject it anywhere. */
export class PrintBridgeService {
  readonly store: PrintBridgeStore;
  private readonly snap: ReturnType<typeof signal<BridgeSnapshot>>;
  /** Full snapshot signal. */
  readonly snapshot: Signal<BridgeSnapshot>;
  readonly state: Signal<ConnectionState>;
  readonly isConnected: Signal<boolean>;
  readonly pairingCode: Signal<string | null>;
  readonly error: Signal<PrintBridgeError | null>;
  readonly isBrowser: boolean;

  constructor(config: PrintBridgeConfig, destroyRef: DestroyRef, isBrowser: boolean) {
    this.isBrowser = isBrowser;
    this.store = config.store ?? createPrintBridgeStore(config.options!);
    this.snap = signal(this.store.getSnapshot());
    const off = this.store.subscribe(() => this.snap.set(this.store.getSnapshot()));
    destroyRef.onDestroy(() => {
      off();
      this.store.disconnect();
    });
    this.snapshot = this.snap.asReadonly();
    this.state = computed(() => this.snap().state);
    this.isConnected = computed(() => this.snap().state === "connected");
    this.pairingCode = computed(() => this.snap().pairingCode);
    this.error = computed(() => this.snap().error);
  }

  /** No-op on the server: the agent runs on the visitor's computer, never on the SSR host. */
  connect(): Promise<void> {
    return this.isBrowser ? this.store.connect() : Promise.resolve();
  }

  pair(options: PairOptions): Promise<void> {
    return this.store.pair(options);
  }

  disconnect(): void {
    this.store.disconnect();
  }

  forget(): Promise<void> {
    return this.store.forget();
  }
}

/** Printers and their declared capabilities. */
export class PrinterService {
  private readonly bridge = inject(PrintBridgeService);
  readonly printers: Signal<readonly Printer[]> = computed(() => this.bridge.snapshot().printers);
  readonly defaultPrinter: Signal<Printer | undefined> = computed(() => this.printers().find((p) => p.isDefault));
  readonly isLoading: Signal<boolean> = computed(() => this.bridge.snapshot().printersStatus === "loading");

  refresh(): Promise<readonly Printer[]> {
    return this.bridge.store.refreshPrinters();
  }

  /** Signal with one printer by id. */
  printer(id: string): Signal<Printer | undefined> {
    return computed(() => this.printers().find((p) => p.id === id));
  }
}

/** Print actions and the session's job history. */
export class PrintJobService {
  private readonly bridge = inject(PrintBridgeService);
  readonly jobs: Signal<Job[]> = computed(() => jobList(this.bridge.snapshot()));
  readonly active: Signal<number> = computed(() => this.jobs().filter((j) => j.state === "queued" || j.state === "rendering" || j.state === "submitted").length);
  readonly isPrinting: Signal<boolean> = computed(() => this.bridge.snapshot().submitting > 0);

  printPdf(job: DocumentJob): Promise<Job> {
    return this.bridge.store.printPdf(job);
  }

  printImage(job: DocumentJob): Promise<Job> {
    return this.bridge.store.printImage(job);
  }

  printRaw(job: RawJob): Promise<Job> {
    return this.bridge.store.printRaw(job);
  }

  cancel(jobId: string): Promise<Job> {
    return this.bridge.store.cancel(jobId);
  }

  /** Signal with the live state of a job (tracks it if it was created elsewhere). */
  job(jobId: string): Signal<Job | undefined> {
    if (!this.bridge.store.getSnapshot().jobs[jobId]) this.bridge.store.watchJob(jobId);
    return computed(() => this.bridge.snapshot().jobs[jobId]);
  }
}

/**
 * Registers the shared Print Bridge services in the application (or a route) injector:
 * `bootstrapApplication(App, { providers: [providePrintBridge({ options: { appId: "com.empresa.app" } })] })`.
 */
export function providePrintBridge(config: PrintBridgeConfig): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: PRINT_BRIDGE_CONFIG, useValue: config },
    {
      provide: PrintBridgeService,
      useFactory: () => new PrintBridgeService(inject(PRINT_BRIDGE_CONFIG), inject(DestroyRef), isPlatformBrowser(inject(PLATFORM_ID))),
    },
    { provide: PrinterService, useFactory: () => new PrinterService() },
    { provide: PrintJobService, useFactory: () => new PrintJobService() },
    provideAppInitializer(() => {
      const service = inject(PrintBridgeService);
      if (config.autoConnect) void service.connect();
    }),
  ]);
}

export type { BridgeSnapshot, Job, Printer, PrintBridgeStore } from "@angelitosystems/print-bridge";
