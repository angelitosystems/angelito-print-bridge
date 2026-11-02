// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

export interface MockPrinter {
  id: string;
  name: string;
  isDefault: boolean;
  isVirtual: boolean;
  status: string[];
  capabilities: null | { duplex: boolean; color: boolean; maxCopies: number; paperSizes: string[] };
}

export interface MockAgentOptions {
  /** Approve pairing requests automatically (default true). */
  autoApprove?: boolean;
  /** Milliseconds before a queued job reaches its final state (default 30). */
  jobDelayMs?: number;
  printers?: MockPrinter[];
  /** Final state for every job (default "completed"). */
  finalState?: "completed" | "failed";
}

export interface MockAgent {
  readonly port: number;
  readonly url: string;
  /** Every request received, for protocol assertions. */
  readonly requests: Array<{ method: string; path: string; origin: string | null; auth: string | null }>;
  /** Approves a pending pairing request by code (when autoApprove is false). */
  approve(code: string): void;
  /** Invalidates all sessions (simulates an agent restart). */
  restart(): void;
  close(): Promise<void>;
}

export function startMockAgent(options?: MockAgentOptions): Promise<MockAgent>;

/** fetch that adds an Origin header, as a browser page would. */
export function fetchWithOrigin(origin: string): typeof fetch;
