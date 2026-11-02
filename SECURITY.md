# Política de seguridad

## Versiones con soporte
Mientras el proyecto esté en 0.x, solo la última versión publicada recibe correcciones de seguridad.

## Cómo reportar una vulnerabilidad
**No** abra un issue público. Use el reporte privado de GitHub:
*Security → Report a vulnerability* en el repositorio oficial de Angelito Systems S.A.C.

Incluya versión, sistema operativo, pasos para reproducir e impacto. Intentamos responder en 5 días hábiles y
coordinar la publicación de la corrección antes de divulgar detalles.

## Alcance
Dentro de alcance: el agente local (API, emparejamiento, autorización, cola), la app de escritorio, el instalador y
el SDK. El modelo de amenazas y sus limitaciones conocidas están en [docs/SECURITY-MODEL.md](docs/SECURITY-MODEL.md).

Fuera de alcance: malware ya ejecutándose con la misma cuenta de Windows, XSS en sitios de terceros autorizados por
el usuario, y versiones modificadas no publicadas por Angelito Systems S.A.C.
