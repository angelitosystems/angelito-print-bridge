// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import adapter from "@sveltejs/adapter-static";
import { sveltekit } from "@sveltejs/kit/vite";
import { defineConfig } from "vite";

// SvelteKit 3: configuration is passed to the Vite plugin (svelte.config.js is no longer read).
export default defineConfig({ plugins: [sveltekit({ adapter: adapter(), paths: { base: (process.env.EXAMPLE_BASE ?? "/").replace(/\/$/, "") as "" | `/${string}` } })] });
