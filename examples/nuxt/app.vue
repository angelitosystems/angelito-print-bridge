<!-- Copyright 2026 Angelito Systems S.A.C. · SPDX-License-Identifier: Apache-2.0 -->
<template>
  <main style="font-family: system-ui; max-width: 720px; margin: 2rem auto; padding: 0 1rem">
    <h1>Angelito Print Bridge · Nuxt</h1>
    <p>El HTML se genera en el servidor; el panel de impresión solo existe en el navegador.</p>
    <ClientOnly>
      <PrintPanel />
      <template #fallback><p>Cargando…</p></template>
    </ClientOnly>
  </main>
</template>
