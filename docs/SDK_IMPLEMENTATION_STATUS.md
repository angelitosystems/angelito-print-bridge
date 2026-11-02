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
| `@angelitosystems/print-bridge-vue` | — | pendiente | — | — | — |
| `@angelitosystems/print-bridge-svelte` | — | pendiente | — | — | — |
| `@angelitosystems/print-bridge-angular` | — | pendiente | — | — | — |

## Ejemplos
| Ejemplo | Compila | Probado en navegador |
|---|---|---|
| `examples/react-vite` | ✔ `vite build` | pendiente |
| `examples/nextjs` (App Router) | ✔ `next build` (rutas estáticas, sin acceso a APIs del navegador en servidor) | ✔ `next start`: sin errores de hidratación; conectó con el agente real → `unpaired` |
| vanilla-typescript, angular, sveltekit, vue-vite, nuxt | pendiente | — |

## Publicación
- `publish-sdk.yml`: release `sdk-<etiqueta>` publicado por un mantenedor → publica cada paquete cuya versión no exista
  en GitHub Packages (core primero) tras build, tipos, lint, tests y E2E; luego instala y comprueba en un proyecto aparte.
- No se ha publicado ninguna versión nueva del ecosistema: requiere autorización explícita.

## Continuar
```bash
npm ci && npm run build && npm run typecheck && npm run lint && npm test
cd packages/core && npm run test:e2e   # requiere dotnet build del agente
```
Pendiente, en orden: Vue + ejemplos Vue/Nuxt → Svelte + SvelteKit → Angular (APF) → ejemplo vanilla → web (Astro/Starlight) + GitHub Pages.
