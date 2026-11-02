// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineCollection } from "astro:content";
import { docsLoader } from "@astrojs/starlight/loaders";
import { docsSchema } from "@astrojs/starlight/schema";

export const collections = { docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }) };
