// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({ base: process.env.EXAMPLE_BASE ?? "/", plugins: [react()], server: { port: 5173 } });
