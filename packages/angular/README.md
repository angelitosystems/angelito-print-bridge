# @angelitosystems/print-bridge-angular

Integración Angular de **Angelito Print Bridge** (creado por [Angelito Systems S.A.C.](https://angelitosystems.net)) · Apache-2.0.
Servicios con signals sobre el SDK `@angelitosystems/print-bridge`: una sola conexión compartida por DI, compatible con
aplicaciones zoneless y con Angular SSR (en el servidor nunca se contacta al agente).

## Instalación (GitHub Packages)
Requiere autenticación con un token `read:packages` (ver la guía del SDK). En `.npmrc`:
```ini
@angelitosystems:registry=https://npm.pkg.github.com
```
```bash
npm install @angelitosystems/print-bridge @angelitosystems/print-bridge-angular
```

## Uso
```ts
// app.config.ts
import { providePrintBridge } from "@angelitosystems/print-bridge-angular";
export const appConfig = { providers: [providePrintBridge({ options: { appId: "com.empresa.app", appName: "Mi App" } })] };
```
```ts
// componente standalone
import { Component, inject } from "@angular/core";
import { PrintBridgeService, PrinterService, PrintJobService } from "@angelitosystems/print-bridge-angular";

@Component({
  selector: "app-print",
  template: `
    <p>Estado: {{ bridge.state() }}</p>
    @if (bridge.state() === "unpaired") { <button (click)="authorize()">Autorizar</button> }
    @for (p of printers.printers(); track p.id) { <button (click)="print(p.id)">{{ p.name }}</button> }
  `,
})
export class PrintComponent {
  readonly bridge = inject(PrintBridgeService);
  readonly printers = inject(PrinterService);
  readonly jobs = inject(PrintJobService);
  authorize() { return this.bridge.pair({ permissions: ["printers:read", "print:document"] }); }
  async print(printerId: string) {
    const pdf = await (await fetch("/sample.pdf")).blob();
    await this.jobs.printPdf({ printerId, document: pdf });
  }
}
```
Llame a `bridge.connect()` tras una acción del usuario (o `autoConnect: true`). `PrinterService` y `PrintJobService`
exponen `printers()`, `defaultPrinter()`, `jobs()`, `active()`, `isPrinting()` y acciones `printPdf/printImage/printRaw/cancel`.
