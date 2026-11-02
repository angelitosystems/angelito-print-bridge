// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import "@angular/compiler";
import { Blob, File } from "node:buffer";
import { getTestBed } from "@angular/core/testing";
import { BrowserTestingModule, platformBrowserTesting } from "@angular/platform-browser/testing";

getTestBed().initTestEnvironment(BrowserTestingModule, platformBrowserTesting());
// jsdom replaces FormData/Blob/File with classes Node fetch cannot serialize (not needed in real browsers).
const nodeFormData = (await new Response(new URLSearchParams("a=1")).formData()).constructor as typeof FormData;
Object.assign(globalThis, { FormData: nodeFormData, Blob, File });
