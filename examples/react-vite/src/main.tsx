// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PrintBridgeProvider } from "@angelitosystems/print-bridge-react";
import { App } from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <PrintBridgeProvider options={{ appId: "com.angelitosystems.example.react", appName: "Ejemplo React" }}>
      <App />
    </PrintBridgeProvider>
  </StrictMode>,
);
