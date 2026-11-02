// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vite";

// EXAMPLE_BASE lets GitHub Pages serve the demo under /angelito-print-bridge/examples/<name>/.
export default defineConfig({ base: process.env.EXAMPLE_BASE ?? "/" });
