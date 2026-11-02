<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script lang="ts">
  import type { PrintBridgeStore } from "@angelitosystems/print-bridge";
  import { providePrintBridge } from "../src/index.ts";
  import Panel from "./Panel.svelte";
  let { store, panels = 2 }: { store: PrintBridgeStore; panels?: number } = $props();
  providePrintBridge(store);
</script>

{#each Array(panels) as _, i (i)}
  <Panel />
{/each}
