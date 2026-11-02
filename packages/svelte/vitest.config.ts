// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vitest/config";
import { svelte } from "@sveltejs/vite-plugin-svelte";

// Components are compiled for the browser in tests; SSR is verified by building the SvelteKit example.
export default defineConfig({
  plugins: [svelte()],
  resolve: { conditions: ["browser"] },
  test: { environment: "jsdom", setupFiles: ["./test/setup.ts"], testTimeout: 15000 },
});
