<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script lang="ts">
  import type { Snippet } from "svelte";
  import { providePrintBridge } from "@angelitosystems/print-bridge-svelte";

  let { children }: { children: Snippet } = $props();
  providePrintBridge({ appId: "com.angelitosystems.example.svelte", appName: "Ejemplo SvelteKit" });
</script>

<main style="font-family: system-ui; max-width: 720px; margin: 2rem auto; padding: 0 1rem">{@render children()}</main>
