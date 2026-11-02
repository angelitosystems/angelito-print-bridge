# Cómo contribuir

¡Gracias por su interés en Angelito Print Bridge! Proyecto creado por Angelito Systems S.A.C. y publicado bajo Apache-2.0.

## Licencia de las contribuciones
Según la sección 5 de Apache-2.0, toda contribución que envíe se licencia bajo Apache-2.0, sin términos adicionales.
Firme sus commits con el *Developer Certificate of Origin* (`git commit -s`). La línea
`Signed-off-by: Nombre <correo>` certifica que tiene derecho a aportar ese código (https://developercertificate.org).
No aporte código copiado de proyectos con licencias incompatibles ni de QZ Tray.

## Antes de abrir un PR
```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
cd sdk/typescript && npm ci && npm run typecheck && npm test && npm run test:e2e
```
- Los tests **nunca** deben imprimir en papel ni auto-seleccionar impresoras instaladas: use la impresora `virtual:file`.
- Todo cambio en seguridad (Origin, Host, CORS, emparejamiento, permisos) necesita un test que lo cubra.
- Archivos nuevos: añada la cabecera `// Copyright <año> <autor>` y `// SPDX-License-Identifier: Apache-2.0`.
  Si modifica un archivo existente, conserve sus avisos; puede añadir el suyo.
- Documentación en español (se aceptan traducciones al inglés).
- Use [Conventional Commits](https://www.conventionalcommits.org/es/) (`feat:`, `fix:`, `docs:`…) y actualice `CHANGELOG.md`.

## Dependencias nuevas
Lea [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md). Solo se aceptan licencias permisivas compatibles con Apache-2.0
y paquetes mantenidos.

## Vulnerabilidades
No abra issues públicos para fallos de seguridad: siga [SECURITY.md](SECURITY.md).

## Conducta
Trato respetuoso y técnico. Los mantenedores pueden cerrar discusiones que no lo sean.
