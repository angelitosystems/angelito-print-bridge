---
title: Next.js (App Router)
description: Usar Angelito Print Bridge en Next.js sin errores de SSR ni hidratación.
---
Use `@angelitosystems/print-bridge-react`: ya incluye `"use client"`. El agente está en el equipo del visitante, por lo
que **nunca** se contacta desde el servidor de Next.js.
```tsx
// app/providers.tsx
"use client";
import { PrintBridgeProvider } from "@angelitosystems/print-bridge-react";
export function Providers({ children }: { children: React.ReactNode }) {
  return <PrintBridgeProvider options={{ appId: "com.miempresa.web" }}>{children}</PrintBridgeProvider>;
}
```
```tsx
// app/layout.tsx (Server Component)
import { Providers } from "./providers";
export default function RootLayout({ children }) {
  return <html lang="es"><body><Providers>{children}</Providers></body></html>;
}
```
- Los componentes que usan hooks deben ser Client Components (`"use client"`); las páginas pueden seguir siendo de servidor.
- El HTML del servidor y el primer render del cliente coinciden (`state: "disconnected"`): sin errores de hidratación.
- La conexión se comparte al navegar entre rutas porque el provider vive en el layout.

Ejemplo verificado (`next build` + `next start`, sin errores de hidratación): `examples/nextjs`.
