// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Verifies that every internal link/asset in the built site (dist/) resolves to a file. Fails the build otherwise.
import { readdirSync, readFileSync, existsSync, statSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const dist = fileURLToPath(new URL("../dist/", import.meta.url));
const base = (process.env.BASE_PATH ?? "/angelito-print-bridge").replace(/\/$/, "");
const files = [];
const walk = (d) => { for (const f of readdirSync(d)) { const p = join(d, f); statSync(p).isDirectory() ? walk(p) : p.endsWith(".html") && files.push(p); } };
walk(dist);

const broken = [];
for (const file of files) {
  const html = readFileSync(file, "utf8");
  for (const [, url] of html.matchAll(/(?:href|src)="([^"#?]+)[^"]*"/g)) {
    if (/^(https?:|mailto:|data:|\/\/)/.test(url)) continue;
    // Example demos are only present when built with `build:pages` (--examples flag); plain builds skip them.
    if (!process.argv.includes("--examples") && new RegExp(`^${base}/examples/[^/]+/`).test(url)) continue;
    let path;
    if (url.startsWith("/")) {
      if (!url.startsWith(base + "/") && url !== base) { broken.push(`${file}: ${url} (outside base ${base})`); continue; }
      path = join(dist, url.slice(base.length));
    } else {
      path = join(dirname(file), url);
    }
    const ok = existsSync(path) && (statSync(path).isFile() || existsSync(join(path, "index.html")));
    if (!ok) broken.push(`${file.replace(dist, "")}: ${url}`);
  }
}
if (broken.length) {
  console.error(`Broken internal links (${broken.length}):\n` + [...new Set(broken)].slice(0, 50).join("\n"));
  process.exit(1);
}
console.log(`Link check OK: ${files.length} pages, no broken internal links.`);
