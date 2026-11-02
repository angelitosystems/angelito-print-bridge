# Distribución, instalación y firma

## Construir
```powershell
./installer/publish.ps1                    # win-x64 → artifacts/win-x64/app (+ instalador si existe Inno Setup 6)
./installer/publish.ps1 -Runtime win-arm64 # ARM64: compila, pero NO validado en hardware ARM64
```
Salida autocontenida (no requiere .NET en el cliente): `AngelitoPrintBridge.exe` (agente) y
`AngelitoPrintBridge.Desktop.exe` (bandeja + UI), ~265 MB sin comprimir. Pendiente: recorte (trimming) para reducir tamaño.

## Instalador (Inno Setup 6, `installer/AngelitoPrintBridge.iss`)
- **Por usuario** (por defecto, sin admin): `%LOCALAPPDATA%\Programs\Angelito Print Bridge`.
- **Todo el equipo**: `/ALLUSERS` (requiere admin) → `Program Files`. El inicio automático usa HKLM en ese modo.
- **Silenciosa**: `Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="autostart" /LOG="install.log"`.
- **Actualización**: antes de copiar, ejecuta `AngelitoPrintBridge.exe shutdown --if-idle`; si hay trabajos en
  proceso (código 4) la actualización se detiene con un mensaje. Los trabajos ya en el spooler de Windows no se pierden.
- **Desinstalación**: detiene agente y UI, elimina el inicio automático. Los datos (autorizaciones, auditoría,
  registros) se conservan salvo que el usuario elija borrarlos.
- Estado: script escrito; **no compilado ni probado en este equipo** (Inno Setup no instalado).

## Firma de código (requisitos externos)
| Qué | Requisito | Dónde vive el secreto |
|---|---|---|
| Authenticode de `.exe` e instalador | Certificado OV/EV a nombre de Angelito Systems S.A.C. (CA pública), o Azure Trusted Signing | HSM/servicio en la nube; credenciales en *GitHub Environments* con aprobación manual |
| Sellado de tiempo | Servidor RFC 3161 de la CA | — |
| Paquete npm | GitHub Packages de la organización `angelitosystems` | `GITHUB_TOKEN` automático (sin tokens guardados) |
| NuGet (SDK .NET futuro) | Cuenta nuget.org + firma de paquete | `NUGET_API_KEY` |
Nunca se guardan certificados, claves privadas ni tokens en el repositorio. Sin firma, SmartScreen mostrará advertencias.

## Versionado y tags (monorepo)
Semantic Versioning: PATCH = correcciones, MINOR = funciones compatibles, MAJOR = cambios incompatibles
(en 0.x una MINOR puede romper compatibilidad). El agente y el SDK se versionan **por separado**:

| Componente | Versión en | Tag del release | Workflow | Destino |
|---|---|---|---|---|
| Agente Windows + app de bandeja + instaladores | `Directory.Build.props` | `agent-vX.Y.Z` | `release-agent.yml` | adjuntos del GitHub Release |
| SDK `@angelitosystems/print-bridge` | `packages/core/package.json` | `sdk-vX.Y.Z` | `publish-sdk.yml` | GitHub Packages (npm) |

Flujo: subir la versión en el archivo correspondiente → merge a `main` (CI verde) → crear un GitHub Release con el tag.
Los workflows verifican que el tag coincida con la versión, que el commit esté en `main`, ejecutan todas las pruebas
y no publican versiones repetidas. Solo los releases estables (no *prerelease*) publican el SDK.
Publicar requiere solo `GITHUB_TOKEN` (`packages: write` únicamente en `publish-sdk.yml`); los PR nunca reciben
permisos de escritura ni usan `pull_request_target`.

## Releases reproducibles
- Versionado semántico; la versión vive en `Directory.Build.props` y `packages/core/package.json`.
- Builds deterministas (`Deterministic`, `ContinuousIntegrationBuild` en CI) y dependencias bloqueadas (`packages.lock.json`, `package-lock.json`).
- `release-agent.yml` (al publicar un release `agent-vX.Y.Z`) compila, prueba y adjunta zips portables, instaladores y `SHA256SUMS.txt`.
- La carpeta publicada incluye LICENSE, NOTICE, THIRD-PARTY-NOTICES.md y TRADEMARKS.md.

## Publicación
Ningún paquete ni release se publica sin autorización explícita del responsable de Angelito Systems S.A.C.
