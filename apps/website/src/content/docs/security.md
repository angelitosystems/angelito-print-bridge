---
title: Seguridad
description: Autorización, orígenes, emparejamiento, permisos y certificados.
---
- **Solo loopback**: el agente escucha en `127.0.0.1`/`::1`; nunca expone puertos a la red.
- **Origen**: solo `https://` (o `http://localhost` en desarrollo). Se valida el `Host` contra DNS rebinding.
- **Emparejamiento**: código de 6 dígitos + verificador secreto; el token se entrega una vez y solo al mismo origen.
- **Sesiones** de 15 minutos ligadas al origen; revocar una aplicación las corta al instante.
- **Permisos**: `printers:read`, `print:document` y `print:raw` (RAW separado: controla corte, cajón, etc.).
- **Límites**: tamaño de documento, validación de formato, límite de peticiones por origen.
- **Local Network Access**: Chrome/Edge piden permiso para acceder a localhost desde sitios públicos. Es esperado y no se elude.

## Certificados de aplicación
El administrador genera o importa un certificado para un origen (app de bandeja › Certificados o
`AngelitoPrintBridge.exe cert-generate`). Su **servidor** firma con la clave privada:
```
angelito-print-bridge/v1 · origen · appId · permisos ordenados · timestamp · nonce
```
El agente verifica certificado autorizado y vigente, origen, permisos, firma (RSA o ECDSA), ventana de 5 minutos y nonce
de un solo uso. Revocar el certificado revoca sus autorizaciones.

## Buenas prácticas
- Nunca ponga claves privadas ni tokens del agente en JavaScript público.
- Pida solo los permisos necesarios.
- Proteja su endpoint de firma con la sesión de su aplicación.
- Reporte vulnerabilidades de forma privada (Security › Report a vulnerability en GitHub).
