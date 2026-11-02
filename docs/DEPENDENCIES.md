# Política de dependencias

1. **Licencias aceptadas**: MIT, Apache-2.0, BSD-2/3-Clause, ISC, Zlib, dominio público; SIL OFL 1.1 solo para fuentes.
   **No** se aceptan GPL/LGPL/AGPL, SSPL, BUSL ni licencias comerciales o con tarifas por uso (p. ej. WiX v6 con OSMF).
2. **Mínimo necesario**: antes de añadir un paquete, compruebe si la BCL de .NET, una API de Windows o un paquete ya
   presente lo resuelve. El SDK TypeScript no tiene dependencias en tiempo de ejecución y debe seguir así.
3. **Mantenimiento**: paquetes con publicaciones en los últimos 12 meses y sin vulnerabilidades conocidas.
4. **Versiones fijas y reproducibles**: versiones exactas en `.csproj`; `packages.lock.json` (NuGet) y
   `package-lock.json` (npm) versionados; CI restaura en modo bloqueado.
5. **Verificación continua**: CI ejecuta `dotnet list package --vulnerable --include-transitive`, `npm audit` y genera
   un SBOM CycloneDX en cada build.
6. **Registro**: toda dependencia distribuida se añade a [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) en el mismo PR.
