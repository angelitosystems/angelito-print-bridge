// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
//
// jsdom replaces FormData/Blob with its own classes, which Node's fetch cannot serialize. Real browsers do not need this.
import { Blob, File } from "node:buffer";

const nodeFormData = (await new Response(new URLSearchParams("a=1")).formData()).constructor as typeof FormData;
Object.assign(globalThis, { FormData: nodeFormData, Blob, File });
