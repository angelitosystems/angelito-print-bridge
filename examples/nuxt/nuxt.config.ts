// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
export default defineNuxtConfig({
  compatibilityDate: "2026-01-01",
  devtools: { enabled: false },
  app: {
    baseURL: process.env.EXAMPLE_BASE ?? "/",
    head: { title: "Angelito Print Bridge · Nuxt", htmlAttrs: { lang: "es" } },
  },
});
