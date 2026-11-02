<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script lang="ts">
  import { getPrintBridge } from "../src/index.ts";
  const { connectionState, printers, defaultPrinter, jobs, isPrinting, connect, pair, printPdf, printer } = getPrintBridge();
  const a4 = printer("Mock A4");
  let lastState = $state("-");
  // Runes interop: Svelte stores are read with the $ prefix inside runes-mode components.
  const ids = $derived($printers.map((p) => p.id).join(","));
  async function doPrint() {
    const job = await printPdf({ printerId: "Mock A4", document: new Uint8Array([37, 80, 68, 70]) });
    lastState = job.state;
  }
</script>

<p data-testid="state">{$connectionState}</p>
<p data-testid="printers">{ids}</p>
<p data-testid="default">{$defaultPrinter?.id ?? "-"}</p>
<p data-testid="duplex">{String($a4?.capabilities?.duplex)}</p>
<p data-testid="job">{$jobs[0]?.state ?? lastState}</p>
<p data-testid="printing">{String($isPrinting)}</p>
<button onclick={() => connect()}>connect</button>
<button onclick={() => pair({ permissions: ["printers:read", "print:document"], pollIntervalMs: 10 })}>pair</button>
<button onclick={doPrint}>print</button>
