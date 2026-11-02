# Estado del ecosistema SDK

Fecha: 2026-11-02. Fuente de verdad de lo **verificado**; "publicado" solo cuando el workflow `publish-sdk` lo confirme.

## Arquitectura
- Un único protocolo en `@angelitosystems/print-bridge` (`packages/core`). Las integraciones no hablan con el agente:
  se suscriben a `PrintBridgeStore` (core), que mantiene **una conexión compartida** y emite instantáneas inmutables
  (`subscribe` / `getSnapshot`, compatibles con `useSyncExternalStore`, `shallowRef`, stores de Svelte y signals de Angular).
- Crear un store **no conecta**: `connect()` se llama por acción del usuario (o `autoConnect`), respetando Local Network Access.
- Autorización guardada por defecto en `sessionStorage` (por pestaña); `localGrantStorage()` es opcional y explícito.
- `packages/shared-testing`: agente simulado con el contrato HTTP real (Origin, Bearer, permisos, multipart) para CI.
  No sustituye el E2E contra el agente real (`packages/core/test/e2e`).

## Paquetes
| Paquete | Versión | Compila | Tests | Agente real | Listo para publicar |
|---|---|---|---|---|---|
| `@angelitosystems/print-bridge` (core) | 0.4.0 | ✔ | ✔ 16 unit/integración (mock) + E2E real | ✔ E2E (local y CI Windows) | ✔ (0.3.0 ya publicado) |
| `@angelitosystems/print-bridge-react` | 0.1.0 | ✔ | ✔ 5 (Strict Mode, SSR, renders, flujo completo con mock) | ✔ vía ejemplo Next.js (detección del agente local) | ✔ |
| `print-bridge-next` | — | — | — | — | **No se crea**: `print-bridge-react` ya incluye `"use client"`; un paquete Next solo reexportaría. Ver ejemplo. |
| SvelteKit / Nuxt | — | — | — | — | Sin paquete propio: `providePrintBridge` (contexto por petición) y el plugin Vue en `.client.ts` cubren SSR. |
| `@angelitosystems/print-bridge-vue` | 0.1.0 | ✔ | ✔ 4 (plugin, composables, 2 componentes = 1 conexión, limpieza al desmontar, SSR sin contactar al agente) | — (mock) | ✔ |
| `@angelitosystems/print-bridge-svelte` | 0.1.0 | ✔ | ✔ 3 (contexto, stores con runes, 1 conexión, limpieza) | — (mock) | ✔ |
| `@angelitosystems/print-bridge-angular` | 0.1.0 | ✔ ng-packagr (APF, `fesm2022`) | ✔ 3 (DI compartida, signals, DestroyRef, plataforma servidor sin conexión) | — (mock) | ✔ (se publica `dist/`) |

## Ejemplos
| Ejemplo | Compila | Probado en navegador |
|---|---|---|
| `examples/react-vite` | ✔ `vite build` | pendiente |
| `examples/nextjs` (App Router) | ✔ `next build` (rutas estáticas, sin acceso a APIs del navegador en servidor) | ✔ `next start`: sin errores de hidratación; conectó con el agente real → `unpaired` |
| `examples/vue-vite` | ✔ `vue-tsc` + `vite build` | pendiente |
| `examples/nuxt` | ✔ `nuxt generate` (plugin `.client.ts`, `<ClientOnly>`) | pendiente |
| `examples/sveltekit` | ✔ prerender SSR (HTML con `disconnected`, sin conexión en servidor) + `svelte-check` 0 errores | pendiente |
| `examples/vanilla-typescript` | ✔ `vite build` | pendiente |
| `examples/angular` | ✔ `ng build` (standalone, zoneless, signals) | pendiente |

## Sitio web (`apps/website`)
- Astro 7 + Starlight 0.42 + Tailwind v4; español en `/`, inglés en `/en/` (páginas clave traducidas; el resto muestra el
  aviso de traducción pendiente de Starlight). Búsqueda Pagefind incluida en Starlight.
- Landing, descargas (datos reales de GitHub Releases en cada build, con SHA-256), frameworks, ejemplos, laboratorio
  (SDK real; conecta solo al pulsar), documentación por framework, API, seguridad y solución de problemas.
- SEO: títulos y descripciones únicos, canonical, hreflang, Open Graph/Twitter, sitemap, JSON-LD SoftwareApplication.
- Verificado localmente: `astro check` 0 errores, build de 37 páginas, verificador de enlaces internos sin rotos,
  revisión visual (escritorio, móvil y modo oscuro).
- Despliegue: `.github/workflows/pages.yml` (configure-pages, upload-pages-artifact, deploy-pages; entorno `github-pages`).
- Dominio futuro `printbridge.angelitosystems.net`: definir `SITE_URL` y `BASE_PATH=/` en el workflow y configurar DNS (no hecho).
- **Desplegado y verificado**: https://angelitosystems.github.io/angelito-print-bridge/ (24 rutas y recursos con 200,
  canonical y hreflang correctos). El laboratorio público no pudo comprobarse contra el agente real desde el navegador
  integrado de pruebas: bloquea las peticiones de sitios públicos a localhost (`ERR_BLOCKED_BY_CLIENT`). Probar en Chrome/Edge
  aceptando el permiso de red local.
- **Ejemplos publicados** en `/examples/<nombre>/` (los 7, compilados con su ruta base por `scripts/build-examples.mjs`).
  Verificado con el build local: React, Angular, Next.js, Nuxt y Vue cargan sin errores; la demo Vue detectó el agente real.
- Pendiente: TypeDoc integrado, Lighthouse automatizado, traducción completa al inglés.

## Publicación
- `publish-sdk.yml`: release `sdk-<etiqueta>` publicado por un mantenedor → publica cada paquete cuya versión no exista
  en GitHub Packages (core primero) tras build, tipos, lint, tests y E2E; luego instala y comprueba en un proyecto aparte.
- Release `sdk-2026.11.02`: publicados core 0.4.0, react/vue/svelte/angular 0.1.0 (log del workflow `publish-sdk`).
  Verificación de instalación: core ✔; angular 0.1.0 ✘ (faltaba la peer `@angular/common`) → corregido en 0.1.1, pendiente de publicar.
  `verify-packages.yml` permite volver a verificar paquetes publicados (solo lectura).

## Continuar
```bash
npm ci && npm run build && npm run typecheck && npm run lint && npm test
cd packages/core && npm run test:e2e   # requiere dotnet build del agente
```
Pendiente, en orden: web (Astro/Starlight) + GitHub Pages → pruebas en navegador de cada ejemplo con el agente real.
Nota Svelte: el store de conexión se llama `connectionState` (no `state`, que choca con la rune `$state`).
