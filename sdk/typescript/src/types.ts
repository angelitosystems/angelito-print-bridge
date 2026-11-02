// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

/** Permisos que una aplicación puede solicitar al emparejarse. */
export type Permission = "printers:read" | "print:document" | "print:raw";

export type ConnectionState = "disconnected" | "connecting" | "unpaired" | "connected" | "error";

/**
 * Estado de un trabajo.
 * - `submitted`: el spooler de Windows aceptó el trabajo.
 * - `completed`: el trabajo salió de la cola de Windows. **No** es confirmación física del papel.
 * - `unknown`: el agente no puede saber si se imprimió; nunca se reimprime automáticamente.
 */
export type JobState = "queued" | "rendering" | "submitted" | "completed" | "failed" | "cancelled" | "unknown";

export type DocumentKind = "pdf" | "image" | "raw";

export interface PrintOptions {
  /** Número de copias (validado contra el máximo que declara el driver). */
  copies?: number;
  orientation?: "portrait" | "landscape";
  /** Nombre exacto del papel tal como lo declara el driver (ver `printer.capabilities.paperSizes`). */
  paperSize?: string;
  duplex?: "simplex" | "longEdge" | "shortEdge";
  /** `fit` (por defecto) ajusta al área imprimible; `none` respeta el tamaño real si cabe. */
  scale?: "fit" | "none";
}

export interface PrinterCapabilities {
  duplex: boolean;
  color: boolean;
  maxCopies: number;
  paperSizes: string[];
}

export interface Printer {
  id: string;
  name: string;
  isDefault: boolean;
  isVirtual: boolean;
  status: string[];
  /** `null` cuando el driver no respondió a la consulta de capacidades. */
  capabilities: PrinterCapabilities | null;
}

export interface Job {
  jobId: string;
  state: JobState;
  printerId: string;
  kind: DocumentKind;
  attempts: number;
  /** `spooler` cuando Windows reportó el fin del trabajo; `none` en otro caso. */
  confirmation: "none" | "spooler";
  error?: { code: string; message: string };
  spoolerStatus?: string;
  createdAt: string;
  updatedAt: string;
}

export interface SubmitBase {
  /** Impresora destino; si se omite se usa la predeterminada de Windows. */
  printerId?: string;
  /** Prioridad entre -10 y 10. */
  priority?: number;
  /** Reenviar con la misma clave devuelve el trabajo original en lugar de imprimir de nuevo. */
  idempotencyKey?: string;
  signal?: AbortSignal;
}

export type BinaryInput = Blob | ArrayBuffer | Uint8Array;

export interface DocumentJob extends SubmitBase {
  document: BinaryInput;
  options?: PrintOptions;
}

export interface RawJob extends SubmitBase {
  /** Bytes del lenguaje de la impresora (ESC/POS, ZPL, EPL...). Un string se envía como UTF-8. */
  data: BinaryInput | string;
  options?: Pick<PrintOptions, "copies">;
}

/** Dónde guarda el SDK la autorización de esta aplicación (por origen). */
export interface GrantStorage {
  get(key: string): string | null | Promise<string | null>;
  set(key: string, value: string): void | Promise<void>;
  remove(key: string): void | Promise<void>;
}

export interface PrintBridgeOptions {
  /** Identificador de la aplicación, p. ej. `com.angelitosystems.dental`. */
  appId: string;
  /** Nombre visible para el usuario al aprobar el emparejamiento. */
  appName?: string;
  /** Puertos de loopback a probar. Por defecto `[41730, 41731, 41732]`. */
  ports?: number[];
  storage?: GrantStorage;
  requestTimeoutMs?: number;
  /** Reabre el canal de eventos si se cae. Por defecto `true`. */
  autoReconnect?: boolean;
  /** Inyección para entornos sin `fetch` global o para pruebas. */
  fetch?: typeof fetch;
  /** Implementación de WebSocket para eventos en tiempo real; `null` los desactiva (se usa sondeo). */
  WebSocket?: typeof WebSocket | null;
  /**
   * Identidad por certificado: si el administrador confió en este certificado para su origen, `connect()` se
   * empareja sin aprobación manual. La clave privada vive en SU servidor: `sign` debe pedirle la firma.
   */
  certificate?: CertificateIdentity;
}

export interface CertificateIdentity {
  /** Certificado PEM público (generado o importado en Angelito Print Bridge). */
  pem: string;
  /**
   * Firma `message` (UTF-8) con la clave privada en su backend: RSA PKCS#1 v1.5 o ECDSA P-256, SHA-256.
   * Devuelve la firma en Base64. Nunca incluya la clave privada en el JavaScript público.
   */
  sign: (message: string) => Promise<string>;
  /** Permisos a solicitar (deben estar autorizados para el certificado). Por defecto `printers:read` y `print:document`. */
  permissions?: Permission[];
  /** Origen de la página; por defecto `location.origin`. Solo necesario fuera del navegador. */
  origin?: string;
}

export interface PairOptions {
  permissions: Permission[];
  /** Recibe el código de 6 dígitos que el usuario debe confirmar en el agente. */
  onCode?: (code: string) => void;
  signal?: AbortSignal;
  pollIntervalMs?: number;
}

export interface WaitOptions {
  /** Estado objetivo. Por defecto `completed` (o cualquier estado final). */
  until?: "submitted" | "completed";
  timeoutMs?: number;
  signal?: AbortSignal;
}

export interface BridgeEvents {
  job: Job;
  state: ConnectionState;
}
