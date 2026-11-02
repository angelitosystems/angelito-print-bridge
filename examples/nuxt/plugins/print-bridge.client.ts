// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// `.client.ts`: runs only in the browser. The agent is on the visitor's computer, so the store never exists on the
// Nuxt server (and is never shared between server requests).
import { createPrintBridgePlugin } from "@angelitosystems/print-bridge-vue";

export default defineNuxtPlugin((nuxtApp) => {
  nuxtApp.vueApp.use(createPrintBridgePlugin({ options: { appId: "com.angelitosystems.example.nuxt", appName: "Ejemplo Nuxt" } }));
});
