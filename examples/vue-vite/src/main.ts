// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { createApp } from "vue";
import { createPrintBridgePlugin } from "@angelitosystems/print-bridge-vue";
import App from "./App.vue";

createApp(App)
  .use(createPrintBridgePlugin({ options: { appId: "com.angelitosystems.example.vue", appName: "Ejemplo Vue" } }))
  .mount("#app");
