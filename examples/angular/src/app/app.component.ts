// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { Component, computed, inject, signal } from "@angular/core";
import { PrintBridgeService, PrinterService, PrintJobService } from "@angelitosystems/print-bridge-angular";

@Component({
  selector: "app-root",
  template: `
    <h1>Angelito Print Bridge · Angular</h1>
    <p>Estado: <strong>{{ bridge.state() }}</strong></p>
    @if (bridge.error(); as e) { <p role="alert">{{ e.code }}: {{ e.message }}</p> }
    @switch (bridge.state()) {
      @case ("unpaired") { <button (click)="authorize()">Autorizar esta aplicación</button> }
      @case ("connected") {}
      @default { <button (click)="bridge.connect()">Conectar con el agente</button> }
    }
    @if (bridge.pairingCode(); as code) { <p>Confirme el código <strong>{{ code }}</strong> en Angelito Print Bridge.</p> }

    @if (bridge.isConnected()) {
      <label>Impresora
        <select [value]="selected()" (change)="chosen.set($any($event.target).value)">
          @for (p of printers.printers(); track p.id) { <option [value]="p.id">{{ p.name }}</option> }
        </select>
      </label>
      <button [disabled]="!selected() || jobs.isPrinting()" (click)="printSample()">Imprimir PDF de prueba</button>
      <h2>Historial</h2>
      <ul>
        @for (j of jobs.jobs(); track j.jobId) {
          <li>{{ j.kind }} → {{ j.printerId }}: {{ j.state }}
            @if (j.state === "queued") { <button (click)="jobs.cancel(j.jobId)">Cancelar</button> }
          </li>
        } @empty { <li>Sin trabajos en esta sesión.</li> }
      </ul>
    }
  `,
})
export class AppComponent {
  readonly bridge = inject(PrintBridgeService);
  readonly printers = inject(PrinterService);
  readonly jobs = inject(PrintJobService);
  readonly chosen = signal("");
  readonly selected = computed(() => this.chosen() || this.printers.defaultPrinter()?.id || "");

  authorize() {
    void this.bridge.pair({ permissions: ["printers:read", "print:document"] }).catch(() => {});
  }

  async printSample() {
    const pdf = await (await fetch(new URL("sample.pdf", document.baseURI))).blob();
    await this.jobs.printPdf({ printerId: this.selected(), document: pdf }).catch(() => {});
  }
}
