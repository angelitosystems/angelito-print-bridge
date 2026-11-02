---
title: Vue / Nuxt
description: Plugin y composables de @angelitosystems/print-bridge-vue.
---
```bash
npm install @angelitosystems/print-bridge @angelitosystems/print-bridge-vue
```
```ts
createApp(App).use(createPrintBridgePlugin({ options: { appId: "com.miempresa.app" } })).mount("#app");
```
```vue
<script setup lang="ts">
import { usePrintBridge, usePrinters, usePrint } from "@angelitosystems/print-bridge-vue";
const { state, connect, pair } = usePrintBridge();
const { printers } = usePrinters();
const { printPdf, isPrinting } = usePrint();
</script>
```
Composables: `usePrintBridge`, `usePrinters`, `usePrinter(id)`, `usePrintJob(id)`, `usePrintQueue`, `usePrint`.
Todos comparten la conexión del plugin y liberan sus suscripciones con el scope del componente.

## Nuxt
Registre el plugin solo en el cliente y renderice el panel dentro de `<ClientOnly>`:
```ts
// plugins/print-bridge.client.ts
export default defineNuxtPlugin((nuxtApp) => {
  nuxtApp.vueApp.use(createPrintBridgePlugin({ options: { appId: "com.miempresa.app" } }));
});
```
Así el store nunca existe en el servidor ni se comparte entre usuarios. Ejemplos: `examples/vue-vite`, `examples/nuxt`.
