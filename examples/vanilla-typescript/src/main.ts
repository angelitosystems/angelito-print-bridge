// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { createPrintBridgeStore, jobList } from "@angelitosystems/print-bridge";

const store = createPrintBridgeStore({ appId: "com.angelitosystems.example.vanilla", appName: "Ejemplo TypeScript" });
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

// One render function subscribed to the shared store: no framework needed.
store.subscribe(() => {
  const s = store.getSnapshot();
  $("state").textContent = s.state;
  $("error").hidden = !s.error;
  $("error").textContent = s.error ? `${s.error.code}: ${s.error.message}` : "";
  $("connect").hidden = !(s.state === "disconnected" || s.state === "error");
  $("pair").hidden = s.state !== "unpaired";
  $("code").hidden = !s.pairingCode;
  $("code").textContent = s.pairingCode ? `Confirme el código ${s.pairingCode} en Angelito Print Bridge` : "";
  $("ready").hidden = s.state !== "connected";
  const select = $<HTMLSelectElement>("printers");
  if (select.options.length !== s.printers.length) {
    select.replaceChildren(...s.printers.map((p) => new Option(p.name, p.id, p.isDefault, p.isDefault)));
  }
  $<HTMLButtonElement>("print").disabled = s.submitting > 0 || !s.printers.length;
  $("jobs").replaceChildren(...jobList(s).map((j) => Object.assign(document.createElement("li"), { textContent: `${j.kind} → ${j.printerId}: ${j.state}` })));
});

$("connect").onclick = () => void store.connect();
$("pair").onclick = () => void store.pair({ permissions: ["printers:read", "print:document"] }).catch(() => {});
$("print").onclick = async () => {
  const pdf = await (await fetch(`${import.meta.env.BASE_URL}sample.pdf`)).blob();
  await store.printPdf({ printerId: $<HTMLSelectElement>("printers").value, document: pdf }).catch(() => {});
};
