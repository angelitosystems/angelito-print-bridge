<p align="center"><img src="docs/assets/logo.png" alt="Angelito Print Bridge" width="220"></p>

# Angelito Print Bridge

Puente local, libre y gratuito, que permite a aplicaciones web **autorizadas** imprimir en silencio
(PDF, imágenes, ESC/POS/ZPL RAW) en las impresoras de Windows, sin diálogos y sin depender de internet.

Creado por **[Angelito Systems S.A.C.](https://angelitosystems.net)** · Licencia **Apache 2.0** · uso personal, empresarial y comercial permitido.

> Estado: **prototipo funcional** (v0.3.0): agente, SDK, app de bandeja y script de instalador. Ver [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md).

## Requisitos
Windows 10 2004+ / 11 (x64), .NET SDK 10 para compilar, Node.js ≥ 22.6 para el SDK.

## App de bandeja
`AngelitoPrintBridge.Desktop.exe` vive junto al reloj de Windows e inicia (y reinicia si falla) el agente.
- **Clic izquierdo**: minipanel con estado, impresoras, trabajos pendientes y accesos rápidos (clic de nuevo o fuera para cerrarlo).
- **Doble clic**: panel completo (aprobar emparejamientos, impresoras, trabajos, aplicaciones, configuración).
- **Clic derecho**: menú con estado del agente, inicio con Windows, acerca de y salir.
Desde el panel puede **elegir la impresora predeterminada de Windows** y **generar o importar certificados de aplicación**
para que un sitio de confianza imprima sin aprobación manual ([docs/CERTIFICATES.md](docs/CERTIFICATES.md)).
Cerrar o minimizar ventanas no detiene la impresión; «Salir» detiene app y agente sin cortar trabajos en curso.
Instalador y distribución: [docs/RELEASE.md](docs/RELEASE.md).

## Ejecutar el agente (desarrollo)
```bash
dotnet build
src/Angelito.PrintBridge.Agent/bin/Debug/net10.0-windows10.0.19041.0/AngelitoPrintBridge.exe
```
Escucha solo en `127.0.0.1:41730` / `[::1]:41730`. Datos, logs y base SQLite en `%LOCALAPPDATA%\AngelitoSystems\PrintBridge`.
Configuración por `appsettings.json` o variables `Bridge__*` (p. ej. `Bridge__VirtualPrinterEnabled=true` para la impresora de pruebas `virtual:file`).

### Administración local por consola
```bash
AngelitoPrintBridge.exe requests          # solicitudes de emparejamiento pendientes (con su código)
AngelitoPrintBridge.exe approve 123456    # aprobar (deny para rechazar)
AngelitoPrintBridge.exe grants            # aplicaciones autorizadas
AngelitoPrintBridge.exe revoke g_xxxxx    # revocar
AngelitoPrintBridge.exe jobs | audit
AngelitoPrintBridge.exe shutdown [--if-idle]   # detener (con --if-idle no interrumpe trabajos)
```

## Integración web
```ts
import { createPrintBridge, escpos } from "@angelitosystems/print-bridge";

const bridge = createPrintBridge({ appId: "com.angelitosystems.dental", appName: "Dental" });
await bridge.connect();
if (bridge.state === "unpaired")
  await bridge.pair({ permissions: ["printers:read", "print:document", "print:raw"], onCode: (c) => alert(`Código: ${c}`) });

const printers = await bridge.printers.list();
const job = await bridge.print.pdf({ printerId: printers[0].id, document: pdfBlob, options: { copies: 1 } });
await bridge.jobs.waitFor(job.jobId);
```
Instalación desde GitHub Packages (requiere autenticarse, ver la guía): `npm install @angelitosystems/print-bridge`.
Guía completa: [packages/core/README.md](packages/core/README.md).

## Documentación
- [Arquitectura y decisiones](docs/ARCHITECTURE.md)
- [Modelo de seguridad y emparejamiento](docs/SECURITY-MODEL.md)
- [Reportar vulnerabilidades](SECURITY.md) · [Contribuir](CONTRIBUTING.md) · [Dependencias](docs/DEPENDENCIES.md) · [Cambios](CHANGELOG.md)
- [Estado verificable](docs/IMPLEMENTATION_STATUS.md)
- [Distribución y firma](docs/RELEASE.md)

## Pruebas
```bash
dotnet test
cd packages/core && npm ci && npm test && npm run test:e2e
```
Las pruebas nunca imprimen en papel: usan la impresora `virtual:file` o salida a archivo con `APB_TEST_PRINTER` explícito.

## Licencia y créditos
Angelito Print Bridge — Copyright © 2026 Angelito Systems S.A.C. — https://angelitosystems.net

Publicado bajo [Apache License 2.0](LICENSE): puede usarlo, modificarlo y distribuirlo, también comercialmente,
conservando [LICENSE](LICENSE) y [NOTICE](NOTICE). No hay activaciones, pagos ni funciones bloqueadas; la impresión
silenciosa solo requiere que el usuario autorice cada aplicación (seguridad, no licencia).

**Atribución**: al redistribuir, conserve LICENSE y los avisos de copyright, e incluya el texto de [NOTICE](NOTICE) en
al menos uno de estos lugares: un archivo NOTICE, su documentación o la pantalla donde su producto muestre avisos de
terceros (Apache 2.0, §4). No se exige nada más: puede usar el software en proyectos comerciales sin mostrar nuestro
nombre en sus pantallas.

### Créditos
Angelito Print Bridge fue creado por **Angelito Systems S.A.C.** (Perú) · https://angelitosystems.net.
La distribución oficial muestra esa autoría en el minipanel, en «Acerca de» y en el instalador.

La marca y el logotipo se rigen por [TRADEMARKS.md](TRADEMARKS.md). Componentes de terceros: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
