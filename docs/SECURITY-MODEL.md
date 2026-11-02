# Modelo de seguridad y emparejamiento

Para reportar vulnerabilidades vea [SECURITY.md](../SECURITY.md).

## Modelo de amenazas
| Amenaza | Mitigación | Verificado por |
|---|---|---|
| Sitio desconocido imprime o lee impresoras | Todo endpoint (salvo `/v1/hello` y emparejamiento) exige sesión ligada al origen | `ApiTests.Session_is_bound_to_its_origin`, E2E |
| Sitio se auto-autoriza | Aprobación solo vía API admin: rechaza cualquier petición con `Origin`, exige token DPAPI del usuario | `Admin_api_is_unreachable_from_pages_and_without_token` |
| Otro sitio roba el token durante el emparejamiento | Token entregado una vez, solo al mismo origen y con el verificador original | `PairingTests.Token_released_once_only_to_same_origin_and_verifier` |
| DNS rebinding | Validación de `Host` (solo loopback) + `AllowedHosts` | `Rebinding_host_is_rejected`, smoke manual |
| Orígenes inseguros | Solo `https://` o `http://` de loopback; se rechazan `null`, `file:`, rutas, credenciales | `OriginPolicyTests` |
| CSRF | No hay cookies; autenticación por `Authorization: Bearer` (fuerza preflight CORS) | diseño |
| Replay | Sesiones de 15 min, verificador de un solo uso, idempotencia por `idempotencyKey` | `Idempotency_*`, E2E |
| Inundación | Rate limit por origen (API 600/min, emparejamiento 10/min), máx. 20 solicitudes pendientes, 3 por origen | `Pairing_is_rate_limited_per_origin`, `Rejects_invalid_requests_and_floods` |
| Documentos maliciosos/grandes | Límite 50 MB (RAW 5 MB), validación de firma (PDF/PNG/JPEG/BMP), máx. 1000 páginas, timeout 120 s | `Permissions_and_limits_are_enforced`, `Corrupt_pdf_*` |
| RAW peligroso | Permiso separado `print:raw` | `Permissions_and_limits_are_enforced` |
| Fuga de datos en logs | Logs sin cuerpos, cabeceras ni query; nombre en spooler `APB-{jobId}`; auditoría sin contenido | E2E `audit trail…` |
| Revocación | `DELETE /v1/admin/grants/{id}` invalida grant y sesiones al instante | `Revoking_a_grant_*`, E2E |

## Fuera del alcance (limitaciones explícitas)
- **Malware local con la misma cuenta de Windows** puede falsificar `Origin` y leer el token admin (DPAPI CurrentUser). Ningún agente local puede impedirlo.
- **XSS en el sitio autorizado** puede usar el grant guardado en `localStorage` (igual que robaría una cookie de sesión). El grant solo funciona desde ese origen y es revocable.
- **Payloads en disco**: `spool/*.bin` existe en claro solo hasta que el spooler acepta el trabajo. Pendiente: cifrado DPAPI en reposo.
- **Renderizado en proceso**: el motor PDF corre dentro del agente. Pendiente: proceso aislado de bajo privilegio.
- Clientes nativos (sin `Origin`) aún no están soportados; requerirán identidad de aplicación firmada.

## Emparejamiento (flujo)
1. Página: `bridge.pair({ permissions, onCode })` → `POST /v1/pair/requests {appId, appName, permissions, verifier}`.
2. El agente muestra la solicitud (fase 1: `AngelitoPrintBridge.exe requests`) con el mismo código de 6 dígitos que ve la página.
3. Usuario aprueba: `AngelitoPrintBridge.exe approve 123456` (o `deny`).
4. La página canjea `POST /v1/pair/requests/{id}/redeem {verifier}` → `grantToken` (una vez).
5. `POST /v1/session {grantToken}` → `sessionToken` (15 min). El SDK renueva automáticamente.

## Criptografía y certificados (estado)
Implementado: tokens aleatorios de 256 bits (CSPRNG), hashes SHA-256 en reposo, comparación en tiempo constante, DPAPI para el token admin.
Pendiente: firma de código Authenticode (certificado de Angelito Systems en CI o firma en la nube) y firma de paquetes de actualización.
No existe firma de licencias comerciales: el proyecto es Apache-2.0. La autorización de aplicaciones (emparejamiento) es un control de seguridad independiente.
