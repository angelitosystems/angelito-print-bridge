# @angelitosystems/print-bridge

SDK oficial de **Angelito Print Bridge**, creado por [Angelito Systems S.A.C.](https://angelitosystems.net). Impresión silenciosa autorizada desde el navegador hacia impresoras de Windows.
Sin dependencias, ESM, TypeScript estricto. Funciona con React, Angular, Vue, Svelte o JavaScript puro.

## Instalación (GitHub Packages)
El paquete se publica en **GitHub Packages** (`https://npm.pkg.github.com`), no en npmjs.com.
GitHub Packages **exige autenticación para instalar paquetes npm, incluso si son públicos**: no hay instalación anónima.

1. Cree un token *classic* de GitHub con el permiso `read:packages` (solo lectura) en
   *Settings › Developer settings › Personal access tokens*. En CI de GitHub Actions use `GITHUB_TOKEN` con
   `permissions: packages: read`.
2. Configure npm **fuera del repositorio** (no suba tokens a git). En `~/.npmrc` del usuario o como variable de entorno:
   ```ini
   @angelitosystems:registry=https://npm.pkg.github.com
   //npm.pkg.github.com/:_authToken=${GITHUB_PACKAGES_TOKEN}
   ```
   En el `.npmrc` del proyecto basta con la primera línea; el token se toma de la variable `GITHUB_PACKAGES_TOKEN`.
3. Instale:
   ```bash
   npm install @angelitosystems/print-bridge
   ```
El paquete es solo ESM (no se ofrece CommonJS) e incluye declaraciones TypeScript. Compatible con Vite, webpack,
Angular, Next.js (en el cliente) y navegadores modernos. Requiere el agente Angelito Print Bridge instalado en el equipo
del usuario (descargas en GitHub Releases).

## Conceptos
1. `connect()` localiza el agente en `127.0.0.1:41730-41732` y restaura la autorización guardada.
2. Si `state === "unpaired"`, `pair()` pide permisos: el agente muestra un código de 6 dígitos que también recibe `onCode`; el usuario confirma y aprueba.
3. La autorización (por origen) se guarda en `localStorage`; las sesiones de 15 min se renuevan solas.
4. Chrome/Edge pedirán el permiso *Acceso a la red local* la primera vez: explique al usuario que debe permitirlo.

## Uso
```ts
import { createPrintBridge, escpos, PrintBridgeError } from "@angelitosystems/print-bridge";

const bridge = createPrintBridge({ appId: "com.miempresa.pos", appName: "Mi POS" });
bridge.on("state", (s) => console.log("estado", s));
bridge.on("job", (j) => console.log(j.jobId, j.state));

try {
  await bridge.connect();
} catch (e) {
  if (e instanceof PrintBridgeError && e.code === "agent_not_found") mostrarDescarga();
}
if (bridge.state === "unpaired") {
  await bridge.pair({ permissions: ["printers:read", "print:document", "print:raw"], onCode: mostrarCodigo });
}

// PDF
const job = await bridge.print.pdf({ document: pdfBlob, options: { copies: 2, paperSize: "A4" } });
const final = await bridge.jobs.waitFor(job.jobId); // completed | failed | cancelled | unknown

// Ticket ESC/POS
const ticket = escpos().init().align("center").bold().text("MI TIENDA").newline().bold(false)
  .align("left").line("Café", "S/ 8.50", 48).qr("20614770229|03|B001|1").feed(3).cut().openDrawer().build();
await bridge.print.raw({ printerId: "POS-80", data: ticket, idempotencyKey: `venta-${ventaId}` });
```

### Estados y garantías
- `submitted`: Windows aceptó el trabajo. `completed`: salió de la cola de Windows — **no** confirma que el papel salió.
- `unknown`: el agente se interrumpió mientras enviaba; no se reimprime solo. Decida usted si reimprimir.
- Use `idempotencyKey` para que un reintento de red no imprima dos veces.
- Opciones no declaradas por el driver (p. ej. dúplex) fallan con `unsupported_option`, nunca se ignoran.

### Errores (`PrintBridgeError.code`)
`agent_not_found`, `not_paired`, `pair_denied`, `pair_expired`, `timeout`, `aborted`, `network_error`,
`permission_denied`, `printer_not_found`, `document_too_large`, `unsupported_option`, `invalid_document`.

## React
```tsx
const bridge = createPrintBridge({ appId: "com.miempresa.app" }); // fuera del componente: una instancia

export function usePrintBridge() {
  const [state, setState] = useState(bridge.state);
  useEffect(() => {
    const off = bridge.on("state", setState);
    bridge.connect().catch(() => {});
    return off;
  }, []);
  return { bridge, state };
}
```

## Angular
```ts
@Injectable({ providedIn: "root" })
export class PrintService {
  readonly bridge = createPrintBridge({ appId: "com.miempresa.app" });
  readonly state = signal(this.bridge.state);
  constructor() { this.bridge.on("state", (s) => this.state.set(s)); }
  connect() { return this.bridge.connect(); }
}
```

## Laravel / backends
El SDK corre en el navegador: genere el PDF en el servidor (p. ej. dompdf), descárguelo con `fetch` como `Blob` y
páselo a `bridge.print.pdf`. Nunca envíe tokens del agente al servidor.

## Seguridad
No guarde secretos privilegiados en el frontend. El grant guardado solo funciona desde su origen y el usuario puede
revocarlo en el agente. Ver `docs/SECURITY-MODEL.md` del repositorio.

## Licencia
Apache-2.0 — Copyright © 2026 Angelito Systems S.A.C. (https://angelitosystems.net). Ver LICENSE y NOTICE.
