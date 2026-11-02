"use client";
// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { usePrintQueue } from "@angelitosystems/print-bridge-react";

export function JobList() {
  const { jobs, active } = usePrintQueue();
  if (!jobs.length) return <p>Aún no hay trabajos en esta sesión.</p>;
  return (
    <>
      <p>En curso: {active}</p>
      <ul>{jobs.map((j) => <li key={j.jobId}>{j.kind} → {j.printerId}: {j.state}</li>)}</ul>
    </>
  );
}
