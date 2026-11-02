// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Build-time data from the public GitHub Releases API (GitHub Pages is static: refreshed on every deploy/release).

export interface AgentAsset { name: string; url: string; size: number; sha256: string | null; arch: string; kind: "installer" | "portable" | "checksums" }
export interface AgentRelease { tag: string; version: string; name: string; publishedAt: string; url: string; notes: string; assets: AgentAsset[] }

const REPO = "angelitosystems/angelito-print-bridge";

export async function latestAgentRelease(): Promise<AgentRelease | null> {
  try {
    const headers: Record<string, string> = { Accept: "application/vnd.github+json" };
    if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=30`, { headers });
    if (!res.ok) return null;
    const releases = (await res.json()) as Array<{ tag_name: string; name: string; published_at: string; html_url: string; body: string; draft: boolean; prerelease: boolean; assets: Array<{ name: string; browser_download_url: string; size: number }> }>;
    const r = releases.find((x) => x.tag_name.startsWith("agent-v") && !x.draft && !x.prerelease);
    if (!r) return null;
    const sumsAsset = r.assets.find((a) => a.name === "SHA256SUMS.txt");
    const sums = new Map<string, string>();
    if (sumsAsset) {
      const text = await (await fetch(sumsAsset.browser_download_url)).text();
      for (const line of text.split(/\r?\n/)) {
        const [hash, file] = line.trim().split(/\s+/);
        if (hash && file) sums.set(file, hash);
      }
    }
    return {
      tag: r.tag_name,
      version: r.tag_name.replace(/^agent-v/, ""),
      name: r.name,
      publishedAt: r.published_at,
      url: r.html_url,
      notes: r.body,
      assets: r.assets.map((a) => ({
        name: a.name,
        url: a.browser_download_url,
        size: a.size,
        sha256: sums.get(a.name) ?? null,
        arch: a.name.includes("arm64") ? "ARM64" : a.name.includes("x64") ? "x64" : "—",
        kind: a.name.endsWith(".exe") ? "installer" : a.name.endsWith(".zip") ? "portable" : "checksums",
      })),
    };
  } catch {
    return null;
  }
}
