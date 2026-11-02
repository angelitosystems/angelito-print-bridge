// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import Link from "next/link";
import { JobList } from "./job-list";

export default function JobsPage() {
  return (
    <main>
      <h1>Trabajos</h1>
      <JobList />
      <p><Link href="/">Volver</Link></p>
    </main>
  );
}
