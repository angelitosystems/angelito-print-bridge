---
title: Instalar los SDK desde GitHub Packages
description: Configuración de npm y autenticación para los paquetes @angelitosystems.
sidebar: { order: 4 }
---
Los paquetes se publican en **GitHub Packages** (`https://npm.pkg.github.com`), no en npmjs.com.
GitHub Packages **exige autenticación para instalar paquetes npm, incluso públicos**.

1. Cree un token *classic* con el permiso `read:packages`. En GitHub Actions use `GITHUB_TOKEN` con `permissions: packages: read`.
2. Configure el registro:
```ini
# .npmrc del proyecto (sin tokens)
@angelitosystems:registry=https://npm.pkg.github.com
```
El token va fuera del repositorio, en `~/.npmrc` del usuario o en la CI:
```ini
//npm.pkg.github.com/:_authToken=${GITHUB_PACKAGES_TOKEN}
```
3. Instale:
```bash
npm install @angelitosystems/print-bridge
```

| Paquete | Uso | Estado |
|---|---|---|
| `@angelitosystems/print-bridge` | Núcleo (cualquier framework) | Publicado 0.3.0 · 0.4.0 en el repositorio |
| `@angelitosystems/print-bridge-react` | React y Next.js | En el repositorio, publicación pendiente |
| `@angelitosystems/print-bridge-angular` | Angular | En el repositorio, publicación pendiente |
| `@angelitosystems/print-bridge-svelte` | Svelte / SvelteKit | En el repositorio, publicación pendiente |
| `@angelitosystems/print-bridge-vue` | Vue / Nuxt | En el repositorio, publicación pendiente |
