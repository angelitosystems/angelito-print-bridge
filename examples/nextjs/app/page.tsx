// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Server Component (static); the interactive panel is a Client Component island.
import Link from "next/link";
import { PrintPanel } from "./print-panel";

export default function Page() {
  return (
    <main>
      <h1>Angelito Print Bridge · Next.js App Router</h1>
      <p>Esta página se renderiza en el servidor; la conexión con el agente local ocurre solo en su navegador.</p>
      <PrintPanel />
      <p><Link href="/jobs">Ver trabajos (otra ruta, misma conexión)</Link></p>
    </main>
  );
}
