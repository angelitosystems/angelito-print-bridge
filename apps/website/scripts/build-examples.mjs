// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Builds every example as a static app under <site base>/examples/<name>/ and copies it into dist/ for GitHub Pages.
import { execSync } from "node:child_process";
import { cpSync, existsSync, rmSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../../", import.meta.url));
const dist = fileURLToPath(new URL("../dist/", import.meta.url));
const base = (process.env.BASE_PATH ?? "/angelito-print-bridge").replace(/\/$/, "");

// name → [build command, output folder]
const examples = {
  "vanilla-typescript": ["npm run build", "dist"],
  "react-vite": ["npm run build", "dist"],
  "vue-vite": ["npm run build", "dist"],
  nextjs: ["npm run build", "out"],
  nuxt: ["npm run build", ".output/public"],
  sveltekit: ["npm run build", "build"],
  angular: ["npx ng build --base-href {base}", "dist/browser"],
};

for (const [name, [command, output]] of Object.entries(examples)) {
  const dir = join(root, "examples", name);
  const exampleBase = `${base}/examples/${name}/`;
  console.log(`\n▶ ${name} → ${exampleBase}`);
  rmSync(join(dir, output), { recursive: true, force: true });
  execSync(command.replace("{base}", exampleBase), { cwd: dir, stdio: "inherit", env: { ...process.env, EXAMPLE_BASE: exampleBase } });
  const out = join(dir, output);
  if (!existsSync(join(out, "index.html"))) throw new Error(`${name}: no index.html in ${out}`);
  const target = join(dist, "examples", name);
  rmSync(target, { recursive: true, force: true });
  cpSync(out, target, { recursive: true });
}
console.log("\nExamples copied into dist/examples/.");
