---
title: ¿Qué es Angelito Print Bridge?
description: Puente local, seguro y de código abierto entre aplicaciones web y las impresoras de Windows.
sidebar: { order: 1 }
---
Angelito Print Bridge permite que una aplicación web **autorizada** imprima en silencio —sin el diálogo de Windows— en
las impresoras del equipo del usuario: PDF, imágenes y comandos RAW (ESC/POS, ZPL, EPL).

Es software libre (Apache 2.0) creado por **Angelito Systems S.A.C.** y no depende de servicios externos: imprimir no
requiere internet.

## Piezas
- **Agente** (`AngelitoPrintBridge.exe`): servicio local en `127.0.0.1:41730` que habla con el spooler de Windows.
- **App de bandeja** (`AngelitoPrintBridge.Desktop.exe`): minipanel, aprobación de aplicaciones, impresoras, trabajos y certificados.
- **SDK** `@angelitosystems/print-bridge` y sus integraciones para React, Angular, Svelte y Vue.

## Requisitos
- Windows 10 2004+ o Windows 11 (x64; ARM64 compilado sin validar en hardware).
- Navegador moderno (Chrome, Edge o Firefox). Safari/macOS no está soportado todavía.
- Para el SDK: Node.js 20+ y acceso a GitHub Packages.

## Conceptos
- **Emparejamiento**: el usuario aprueba su aplicación una vez, comparando un código de 6 dígitos.
- **Certificado de aplicación**: alternativa sin intervención; su servidor firma y el equipo confía en el certificado.
- **Permisos**: `printers:read`, `print:document` y `print:raw` (este último, separado por seguridad).
- **Estados de trabajo**: `queued`, `rendering`, `submitted`, `completed` (salió de la cola de Windows; no confirma el papel), `failed`, `cancelled`, `unknown`.
