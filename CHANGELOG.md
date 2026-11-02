# Changelog

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); el proyecto sigue
[Versionado Semántico](https://semver.org/lang/es/). Mientras sea 0.x, una versión menor puede romper compatibilidad.

## [0.3.0] — sin publicar
### Añadido (actualización)
- **Certificados de aplicación**: generar (RSA-2048) o importar (RSA/ECDSA) un certificado ligado a un origen y permisos;
  el backend del sitio firma y el SDK se empareja sin aprobación manual (`certificate` en `createPrintBridge`).
  Protección contra repetición (nonce + ventana de 5 min) y revocación en cascada. CLI: `certs`, `cert-generate`, `cert-revoke`.
- Establecer la impresora predeterminada de Windows desde la pestaña Impresoras.
- Rediseño visual: barra lateral oscura con degradado, navegación propia, acentos azul→violeta, minipanel más ancho.
- NOTICE con el aviso de atribución a Angelito Systems S.A.C. (se conserva según Apache-2.0 §4(d), sin requisitos adicionales).
- «Acerca de» con empresa, copyright y enlace a angelitosystems.net; créditos en README y metadatos del SDK.
### Cambiado
- **Licencia: ahora es software libre bajo Apache License 2.0** (antes propietario). Sin activaciones, pagos ni
  ediciones; todas las funciones disponibles para todos. Se añaden LICENSE, NOTICE, avisos de terceros y política de marca.
### Añadido
- Icono residente en la bandeja: clic izquierdo abre y cierra un minipanel; clic fuera lo cierra; doble clic abre el
  panel completo; menú contextual nativo con estado del agente e inicio con Windows.
- Minipanel rápido: estado, impresoras, predeterminada, pendientes, última impresión, apps autorizadas y accesos directos.
- Vigilancia: la app de bandeja reinicia el agente si se cae y reduce el sondeo cuando no hay ventanas visibles.
- Iconos en la barra lateral, estados vacíos ilustrados y pestaña «Acerca de» con autoría y licencias.
- «Salir» detiene la app y el agente sin interrumpir trabajos en curso.

## [0.2.0] — sin publicar
- App de escritorio Avalonia, API admin, página de prueba, apagado seguro e instalador Inno Setup (script).

## [0.1.0] — sin publicar
- Agente local, emparejamiento seguro, cola SQLite persistente, impresión PDF/imagen/RAW y SDK TypeScript.
