"use client";
// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// The agent runs on the visitor's computer: the store lives only in the browser (one per tab), never on the server.
import type { ReactNode } from "react";
import { PrintBridgeProvider } from "@angelitosystems/print-bridge-react";

export function Providers({ children }: { children: ReactNode }) {
  return (
    <PrintBridgeProvider options={{ appId: "com.angelitosystems.example.next", appName: "Ejemplo Next.js" }}>
      {children}
    </PrintBridgeProvider>
  );
}
