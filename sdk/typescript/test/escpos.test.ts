// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

import { test } from "node:test";
import assert from "node:assert/strict";
import { escpos } from "../src/escpos.ts";

test("init selects PC850 and text encodes Spanish characters", () => {
  const bytes = escpos().init().text("Ñandú ¿sí? ¡€!").build();
  assert.deepEqual([...bytes.slice(0, 5)], [0x1b, 0x40, 0x1b, 0x74, 2]);
  assert.deepEqual([...bytes.slice(5)], [0xa5, 0x61, 0x6e, 0x64, 0xa3, 0x20, 0xa8, 0x73, 0xa1, 0x3f, 0x20, 0xad, 0x3f, 0x21]);
});

test("line pads to the requested width", () => {
  const bytes = escpos().line("Café", "S/ 8.50", 20).build();
  assert.equal(bytes.length, 21);
  assert.equal(bytes[3], 0x82); // é
  assert.equal(bytes[20], 0x0a);
});

test("cut, drawer and size commands", () => {
  assert.deepEqual([...escpos().cut().build()], [0x1d, 0x56, 66, 0]);
  assert.deepEqual([...escpos().cut("full").build()], [0x1d, 0x56, 65, 0]);
  assert.deepEqual([...escpos().openDrawer().build()], [0x1b, 0x70, 0, 25, 250]);
  assert.deepEqual([...escpos().size(2, 3).build()], [0x1d, 0x21, 0x12]);
  assert.deepEqual([...escpos().size(99, 0).build()], [0x1d, 0x21, 0x70]);
});

test("qr store command carries the payload length (pL pH)", () => {
  const data = "20614770229|01|F001|123";
  const bytes = [...escpos().qr(data).build()];
  const store = bytes.findIndex((b, i) => b === 0x31 && bytes[i + 1] === 0x50 && bytes[i + 2] === 0x30);
  assert.ok(store > 0);
  const len = bytes[store - 2]! | (bytes[store - 1]! << 8);
  assert.equal(len, data.length + 3);
  assert.equal(new TextDecoder().decode(Uint8Array.from(bytes.slice(store + 3, store + 3 + data.length))), data);
});
