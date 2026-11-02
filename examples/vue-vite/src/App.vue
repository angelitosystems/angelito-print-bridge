<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script setup lang="ts">
import { computed, ref } from "vue";
import { usePrint, usePrintBridge, usePrinters, usePrintQueue } from "@angelitosystems/print-bridge-vue";

const { state, pairingCode, error, connect, pair } = usePrintBridge();
const { printers, defaultPrinter } = usePrinters();
const { printPdf, cancel, isPrinting, lastJob } = usePrint();
const { jobs } = usePrintQueue();
const chosen = ref("");
const selected = computed(() => chosen.value || defaultPrinter.value?.id || "");

async function printSample() {
  const pdf = await (await fetch("/sample.pdf")).blob();
  await printPdf({ printerId: selected.value, document: pdf }).catch(() => {});
}
</script>

<template>
  <main style="font-family: system-ui; max-width: 720px; margin: 2rem auto; padding: 0 1rem">
    <h1>Angelito Print Bridge · Vue 3</h1>
    <p>Estado: <strong>{{ state }}</strong></p>
    <p v-if="error" role="alert">{{ error.code }}: {{ error.message }}</p>
    <button v-if="state === 'disconnected' || state === 'error'" @click="connect()">Conectar con el agente</button>
    <button v-if="state === 'unpaired'" @click="pair({ permissions: ['printers:read', 'print:document'] }).catch(() => {})">Autorizar</button>
    <p v-if="pairingCode">Código: <strong>{{ pairingCode }}</strong></p>
    <section v-if="state === 'connected'">
      <select v-model="chosen">
        <option v-for="p in printers" :key="p.id" :value="p.id">{{ p.name }}</option>
      </select>
      <button :disabled="!selected || isPrinting" @click="printSample">Imprimir PDF de prueba</button>
      <p v-if="lastJob">Último trabajo: {{ lastJob.state }}</p>
      <ul>
        <li v-for="j in jobs" :key="j.jobId">
          {{ j.kind }} → {{ j.printerId }}: {{ j.state }}
          <button v-if="j.state === 'queued'" @click="cancel(j.jobId)">Cancelar</button>
        </li>
      </ul>
    </section>
  </main>
</template>
