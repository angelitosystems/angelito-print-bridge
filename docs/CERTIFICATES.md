# Certificados de aplicación (autorización sin aprobación manual)

Con un certificado autorizado, su sitio se conecta e imprime en silencio sin que el usuario apruebe un código cada vez.
Es el mismo modelo de confianza que popularizó QZ Tray (certificado público + firma con clave privada en el servidor),
implementado de forma independiente. No es una licencia comercial: es un control de seguridad.

## 1. Crear y autorizar el certificado
- **App de escritorio** › *Certificados* › origen (`https://app.suempresa.com`), nombre y permisos › **Generar certificado y llaves**.
  Se guardan `certificate.pem` y `private-key.pem` en `Documentos\Angelito Print Bridge\certificados\…`.
- **Consola**: `AngelitoPrintBridge.exe cert-generate https://app.suempresa.com "Mi App" .\certs printers:read,print:document`
- **Otros equipos**: *Certificados* › **Importar certificado (.pem)** con el mismo origen y permisos.
  También puede importar un certificado propio (RSA o ECDSA P-256), por ejemplo creado con OpenSSL.

El agente guarda **solo el certificado público**, ligado a un origen y a unos permisos. La clave privada se entrega una
vez y no queda en el equipo.

## 2. Guardar la clave privada en su servidor
`private-key.pem` nunca debe estar en JavaScript, en el repositorio ni en el navegador. Cárguela desde un secreto del servidor.

## 3. Firmar en el backend
El SDK envía a su endpoint un texto como este y espera la firma en Base64 (SHA-256; RSA PKCS#1 v1.5 o ECDSA):
```
angelito-print-bridge/v1
https://app.suempresa.com
com.suempresa.app
print:document,printers:read
1762062000000
<nonce aleatorio>
```
Proteja el endpoint con la sesión de su aplicación: solo usuarios autenticados deben obtener firmas.

**Node.js (Express)**
```js
import { createSign } from "node:crypto";
app.post("/print-bridge/sign", requireLogin, express.text(), (req, res) =>
  res.send(createSign("SHA256").update(req.body).sign(process.env.APB_PRIVATE_KEY, "base64")));
```

**PHP / Laravel**
```php
Route::post('/print-bridge/sign', function (Request $r) {
    openssl_sign($r->getContent(), $sig, config('services.print_bridge.private_key'), OPENSSL_ALGO_SHA256);
    return response(base64_encode($sig));
})->middleware('auth');
```

**.NET (ASP.NET Core)**
```csharp
app.MapPost("/print-bridge/sign", async (HttpRequest req, RSA key) =>
{
    var message = await new StreamReader(req.Body).ReadToEndAsync();
    return Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}).RequireAuthorization();
```

**Python**
```python
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding
key = serialization.load_pem_private_key(open("private-key.pem", "rb").read(), None)
signature = base64.b64encode(key.sign(message.encode(), padding.PKCS1v15(), hashes.SHA256())).decode()
```

## 4. Configurar el SDK
```ts
import { createPrintBridge } from "@angelitosystems/print-bridge";
import certificatePem from "./certificate.pem?raw"; // el certificado es público

const bridge = createPrintBridge({
  appId: "com.suempresa.app",
  appName: "Mi App",
  certificate: {
    pem: certificatePem,
    permissions: ["printers:read", "print:document"],
    sign: async (message) =>
      (await fetch("/print-bridge/sign", { method: "POST", body: message, credentials: "include" })).text(),
  },
});
await bridge.connect(); // se empareja solo si el certificado está autorizado en este equipo
```

## Seguridad
- El agente verifica: certificado autorizado y vigente, mismo **origen**, permisos dentro de los autorizados, firma
  válida, marca de tiempo dentro de ±5 minutos y **nonce de un solo uso** (contra repeticiones).
- Revocar el certificado (UI o `cert-revoke`) revoca al instante las autorizaciones emitidas con él.
- Si la clave privada se filtra: revoque el certificado, genere uno nuevo y despliéguelo.
- Pruebas: `CertificateTests` (.NET) y el E2E `certificate identity…` (SDK).
