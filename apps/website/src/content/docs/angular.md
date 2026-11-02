---
title: Angular
description: providePrintBridge, servicios y signals de @angelitosystems/print-bridge-angular.
---
```bash
npm install @angelitosystems/print-bridge @angelitosystems/print-bridge-angular
```
Angular 20–22. Biblioteca en Angular Package Format, sin decoradores propios, compatible con zoneless y SSR.
```ts
bootstrapApplication(AppComponent, {
  providers: [provideZonelessChangeDetection(), providePrintBridge({ options: { appId: "com.miempresa.app" } })],
});
```
```ts
@Component({ selector: "app-print", template: `
  <p>{{ bridge.state() }}</p>
  @for (p of printers.printers(); track p.id) { <button (click)="print(p.id)">{{ p.name }}</button> }` })
export class PrintComponent {
  bridge = inject(PrintBridgeService);
  printers = inject(PrinterService);
  jobs = inject(PrintJobService);
  print(printerId: string) { /* this.jobs.printPdf({ printerId, document }) */ }
}
```
- `PrintBridgeService`: `state()`, `isConnected()`, `pairingCode()`, `error()`, `connect()`, `pair()`, `disconnect()`.
- `PrinterService`: `printers()`, `defaultPrinter()`, `isLoading()`, `printer(id)`, `refresh()`.
- `PrintJobService`: `jobs()`, `active()`, `isPrinting()`, `printPdf/printImage/printRaw`, `cancel`, `job(id)`.

En SSR, `connect()` no hace nada en el servidor. Ejemplo: `examples/angular`.
