---
title: Svelte / SvelteKit
description: Contexto y stores de @angelitosystems/print-bridge-svelte (Svelte 5).
---
```bash
npm install @angelitosystems/print-bridge @angelitosystems/print-bridge-svelte
```
```svelte
<!-- +layout.svelte -->
<script lang="ts">
  import { providePrintBridge } from "@angelitosystems/print-bridge-svelte";
  let { children } = $props();
  providePrintBridge({ appId: "com.miempresa.app" });
</script>
{@render children()}
```
```svelte
<!-- +page.svelte -->
<script lang="ts">
  import { getPrintBridge } from "@angelitosystems/print-bridge-svelte";
  const { connectionState, printers, connect, pair, printPdf } = getPrintBridge();
</script>
<p>{$connectionState}</p>
{#each $printers as p (p.id)}<button onclick={() => printPdf({ printerId: p.id, document: pdf })}>{p.name}</button>{/each}
```
- Stores: `connectionState` (no se llama `state` porque `$state` es una rune), `pairingCode`, `error`, `printers`,
  `defaultPrinter`, `jobs`, `isPrinting`; fábricas `job(id)` y `printer(id)`.
- SSR: el store vive en el contexto del árbol (uno por petición) y solo conecta en `onMount`.

Ejemplo prerenderizado: `examples/sveltekit`.
