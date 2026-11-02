<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script lang="ts">
  import { getPrintBridge } from "@angelitosystems/print-bridge-svelte";

  const { connectionState, pairingCode, error, printers, defaultPrinter, jobs, isPrinting, connect, pair, printPdf } = getPrintBridge();
  let chosen = $state("");
  const selected = $derived(chosen || $defaultPrinter?.id || "");

  async function printSample() {
    await printPdf({ printerId: selected, document: await (await fetch("/sample.pdf")).blob() }).catch(() => {});
  }
</script>

<h1>Angelito Print Bridge · SvelteKit</h1>
<p>Estado: <strong>{$connectionState}</strong></p>
{#if $error}<p role="alert">{$error.code}: {$error.message}</p>{/if}
{#if $connectionState === "disconnected" || $connectionState === "error"}
  <button onclick={() => connect()}>Conectar con el agente</button>
{:else if $connectionState === "unpaired"}
  <button onclick={() => pair({ permissions: ["printers:read", "print:document"] }).catch(() => {})}>Autorizar</button>
{/if}
{#if $pairingCode}<p>Código: <strong>{$pairingCode}</strong></p>{/if}
{#if $connectionState === "connected"}
  <select bind:value={chosen}>
    {#each $printers as p (p.id)}<option value={p.id}>{p.name}</option>{/each}
  </select>
  <button disabled={!selected || $isPrinting} onclick={printSample}>Imprimir PDF de prueba</button>
  <ul>
    {#each $jobs as j (j.jobId)}<li>{j.kind} → {j.printerId}: {j.state}</li>{/each}
  </ul>
{/if}
