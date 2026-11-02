// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Server Component: only the provider below is a Client Component.
import type { ReactNode } from "react";
import { Providers } from "./providers";

export const metadata = { title: "Angelito Print Bridge · Next.js", description: "Ejemplo App Router" };

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="es">
      <body style={{ fontFamily: "system-ui", maxWidth: 720, margin: "2rem auto", padding: "0 1rem" }}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
