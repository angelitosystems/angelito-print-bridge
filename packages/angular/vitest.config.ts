// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "vitest/config";

export default defineConfig({ test: { environment: "jsdom", setupFiles: ["./test/setup.ts"], testTimeout: 15000 } });
