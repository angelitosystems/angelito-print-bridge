// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

/** Códigos que produce el propio SDK (además de los que devuelve el agente). */
export type SdkErrorCode =
  | "agent_not_found"
  | "not_paired"
  | "timeout"
  | "aborted"
  | "network_error"
  | "pair_expired"
  | "pair_denied"
  | (string & {});

/** Error tipado del SDK. `code` es estable y apto para lógica; `message` es para humanos. */
export class PrintBridgeError extends Error {
  readonly code: SdkErrorCode;
  readonly status: number | undefined;

  constructor(code: SdkErrorCode, message: string, status?: number) {
    super(message);
    this.name = "PrintBridgeError";
    this.code = code;
    this.status = status;
  }
}
