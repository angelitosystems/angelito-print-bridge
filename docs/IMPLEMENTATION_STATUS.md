# Estado de implementación (verificable)

Fecha: 2026-11-02 · Versión: 0.3.0 · **Prototipo open source (Apache-2.0) — no apto aún para producción.**

## Evidencia
| Suite | Comando | Resultado |
|---|---|---|
| .NET (unit + integración HTTP/WS + Windows reales + UI/VM) | `dotnet test` | 70/70 ✔ |
| SDK unit | `cd sdk/typescript && npm test` | 9/9 ✔ |
| E2E (agente real + SQLite + motor PDF + CLI) | `npm run test:e2e` | 8/8 ✔ (re-ejecutado en 0.3.0) |
| Formato | `dotnet format --verify-no-changes`, `tsc --noEmit` strict | ✔ |

## Criterios del MVP
| # | Criterio | Estado | Evidencia |
|---|---|---|---|
| 1 | Instalación en Windows | 🟡 Publicación autocontenida verificada (la UI arranca su agente); script Inno Setup escrito pero **no compilado** | `installer/publish.ps1`, smoke manual |
| 2 | Inicio y cierre correcto | ✔ inicio automático desde la UI, `shutdown` y `shutdown --if-idle` (rechaza con trabajos activos) | `AdminApiTests`, smoke manual |
| 3 | Detección de impresoras | ✔ winspool `EnumPrinters` + capacidades del driver | `Lists_installed_printers…`, smoke manual (4 impresoras reales) |
| 4 | Emparejamiento seguro desde web | ✔ aprobación visual en la UI con código de verificación (y CLI) | `PairingTests`, `ApiTests`, E2E, UI Automation |
| 5 | Impresión silenciosa de PDF | 🟡 Render real verificado (Windows.Data.Pdf, impresora virtual). **GDI → driver físico no verificado** | `Windows_pdf_engine_renders…`, E2E |
| 6 | Ticket ESC/POS | 🟡 Bytes exactos verificados en impresora virtual; **no probado en térmica física** | E2E ticket |
| 7 | Estado de trabajos | ✔ REST + WebSocket | `Websocket_*`, E2E |
| 8 | Cola persistente | ✔ | `StoreTests`, `JobServiceTests` |
| 9 | Recuperación tras reinicio | ✔ crash duro simulado | `Recovery_*`, `Crash_while_spooling…`, E2E |
| 10 | SDK TypeScript | ✔ | unit + E2E |
| 11 | Auditoría sin filtraciones | ✔ | E2E `audit trail…` |
| 12 | Pruebas automatizadas | ✔ | arriba |
| 13 | Docs instalación/integración | 🟡 README + SDK README + seguridad | — |

## Hardware: lo que NO se ha probado físicamente
- Impresión en papel de cualquier tipo (no se imprimió en la EPSON L4360 instalada, a propósito).
- Impresoras térmicas ESC/POS, ZPL/EPL, cajón de dinero, corte.
- Para probar ruta GDI con un driver real sin papel: `APB_TEST_PRINTER="<nombre>" dotnet test` (imprime a archivo). No usar OneNote (abre la aplicación).
- Hallazgo local: "Microsoft Print to PDF" reporta `IsValid=false` en este equipo.

## Fase 2 — App de escritorio (Avalonia 12, MVVM)
Verificado en este equipo, con datos temporales e impresora virtual, controlando la app por **Windows UI Automation**
(el mismo árbol que usan los lectores de pantalla) y con capturas:
- Bandeja del sistema, instancia única, cerrar la ventana la oculta (la impresión sigue en el agente).
- Inicio: tarjeta de aprobación con código de 6 dígitos, estado del agente, métricas y últimos trabajos.
- Impresoras (capacidades del driver, página de prueba), Trabajos (detalles técnicos, cancelar, filtro de errores),
  Aplicaciones (revocar), Configuración (inicio con Windows, tema
  sistema/claro/oscuro, exportar diagnóstico, carpeta de registros).
- Página de prueba generada sin dependencias, validada con el motor PDF de Windows.
No verificado: lector de pantalla real (NVDA/Narrador), escalados 150–200 %, alto contraste, multi-monitor.

## 0.3.0 — Bandeja, minipanel y código abierto
| # | Prueba obligatoria | Estado | Evidencia |
|---|---|---|---|
| 1 | Se ejecuta | ✔ | build Debug y publicación Release |
| 2 | Un único icono en la bandeja | ✔ 1 icono (en el desbordamiento «iconos ocultos» de Windows 11), tooltip «activo» | UI Automation sobre la barra de tareas |
| 3 | Clic izquierdo → minipanel | ✔ clic real sobre el icono; segundo clic lo cierra; clic de nuevo lo reabre; Escape lo cierra | SendInput + UI Automation + captura |
| 4 | Clic derecho → menú | ⏳ pendiente | — |
| 5 | Cerrar minipanel mantiene el servicio | ✔ agente respondiendo tras cerrar el panel | `/v1/hello` |
| 6 | Cerrar ventana principal no detiene impresiones | ✔ (0.2.0, UI Automation) | — |
| 7 | Inicio automático | 🟡 entrada `HKCU\...\Run` probada en 0.2.0; inicio de sesión real no probado | — |
| 8 | Sin procesos duplicados | ✔ 1 UI + 1 agente; mutex por sesión | conteo de procesos |
| 9 | Posición multi-monitor / escalado | 🟡 lógica pura probada (4 bordes, monitor con coordenadas negativas, 150 %); solo 1 monitor físico disponible | `PanelPlacementTests` |
| 10 | Botones con acciones reales | 🟡 panel completo ✔ (0.2.0); botones del minipanel pendientes | — |
| 11 | Inicio/cierre de sesión | ❌ no probado | — |
| 12 | Imprimir sin internet | ✔ por diseño: solo loopback, sin llamadas salientes; E2E con impresora virtual | E2E |
| 13 | Silenciosa respeta autorizaciones | ✔ | `ApiTests`, E2E |
| 14 | Sin bloqueos por licencia | ✔ no existe código de licencias; textos comerciales eliminados | grep del repositorio |
| 15 | LICENSE y NOTICE | ✔ Apache-2.0 íntegro (reproducido; conviene comparar con apache.org), NOTICE con atribución | archivos raíz |

Notas: «Imprimir página de prueba» del minipanel abre Impresoras para elegir impresora a propósito (evita imprimir en
papel o en OneNote por accidente). Un proceso terminado a la fuerza deja un icono fantasma hasta pasar el ratón (comportamiento
de Windows); «Salir» elimina el icono explícitamente.

## Certificados e impresora predeterminada
- Certificados: generar/importar/revocar verificado en la UI real (UI Automation) y por CLI; emparejamiento firmado
  verificado con firmas reales RSA y ECDSA (P1363 y DER) y ataques rechazados (`CertificateTests`, E2E del SDK).
- Impresora predeterminada: implementada con `SetDefaultPrinterW`; **no se pulsó en esta máquina** para no cambiar tu configuración.
- Pendiente de entrada real: clic derecho (menú) y doble clic, porque el usuario estaba usando la app durante la sesión.

## Pendiente (siguientes fases, en orden)
1. Compilar y probar el instalador (requiere Inno Setup 6) + firma Authenticode.
2. Notificaciones nativas de Windows para solicitudes de emparejamiento.
3. Pruebas en hardware físico (térmica USB/red, EPSON A4) y matriz de drivers.
4. Cifrado DPAPI de payloads, renderizador aislado, permisos por impresora.
5. Actualizaciones firmadas con canales y rollback (sin licencias: el proyecto es Apache-2.0).
6. Adaptadores de periféricos (serial, HID, TCP), SDK .NET, plataforma cloud (NestJS).
7. CI con SBOM y firma (workflow base incluido en `.github/workflows/ci.yml`).
