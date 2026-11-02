// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig({ base: process.env.EXAMPLE_BASE ?? "/", plugins: [vue()] });
