<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<script setup lang="ts">
import { computed, ref } from "vue";
import { useRuntimeConfig } from "#app";
import { usePrint, usePrintBridge, usePrinters } from "@angelitosystems/print-bridge-vue";

const { state, pairingCode, error, connect, pair } = usePrintBridge();
const { printers, defaultPrinter } = usePrinters();
const { printPdf, isPrinting, lastJob } = usePrint();
const chosen = ref("");
const selected = computed(() => chosen.value || defaultPrinter.value?.id || "");
async function printSample() {
  await printPdf({ printerId: selected.value, document: await (await fetch(`${useRuntimeConfig().app.baseURL}sample.pdf`)).blob() }).catch(() => {});
}
</script>

<template>
  <section>
    <p>Estado: <strong>{{ state }}</strong></p>
    <p v-if="error" role="alert">{{ error.code }}: {{ error.message }}</p>
    <button v-if="state === 'disconnected' || state === 'error'" @click="connect()">Conectar con el agente</button>
    <button v-if="state === 'unpaired'" @click="pair({ permissions: ['printers:read', 'print:document'] }).catch(() => {})">Autorizar</button>
    <p v-if="pairingCode">Código: <strong>{{ pairingCode }}</strong></p>
    <template v-if="state === 'connected'">
      <select v-model="chosen"><option v-for="p in printers" :key="p.id" :value="p.id">{{ p.name }}</option></select>
      <button :disabled="!selected || isPrinting" @click="printSample">Imprimir PDF de prueba</button>
      <p v-if="lastJob">Último trabajo: {{ lastJob.state }}</p>
    </template>
  </section>
</template>
