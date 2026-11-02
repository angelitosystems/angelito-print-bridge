// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";
import sitemap from "@astrojs/sitemap";
import tailwindcss from "@tailwindcss/vite";

// GitHub Pages project site. For the future custom domain (printbridge.angelitosystems.net) set
// SITE_URL=https://printbridge.angelitosystems.net and BASE_PATH=/ in the Pages workflow.
const site = process.env.SITE_URL ?? "https://angelitosystems.github.io";
const base = process.env.BASE_PATH ?? "/angelito-print-bridge";

export default defineConfig({
  site,
  base,
  trailingSlash: "always",
  integrations: [
    starlight({
      title: { es: "Angelito Print Bridge", en: "Angelito Print Bridge" },
      description: "Impresión local segura desde aplicaciones web. Open source (Apache-2.0) por Angelito Systems S.A.C.",
      logo: { src: "./src/assets/emblem.png", alt: "Angelito Print Bridge" },
      favicon: "/favicon.png",
      defaultLocale: "root",
      locales: {
        root: { label: "Español", lang: "es" },
        en: { label: "English", lang: "en" },
      },
      social: [{ icon: "github", label: "GitHub", href: "https://github.com/angelitosystems/angelito-print-bridge" }],
      editLink: { baseUrl: "https://github.com/angelitosystems/angelito-print-bridge/edit/main/apps/website/" },
      customCss: ["./src/styles/global.css"],
      sidebar: [
        { label: "Introducción", translations: { en: "Introduction" }, items: [{ autogenerate: { directory: "getting-started" } }] },
        { label: "JavaScript / TypeScript", items: [{ autogenerate: { directory: "javascript" } }] },
        { label: "Frameworks", items: [
          { label: "React + Vite", link: "/react/" },
          { label: "Next.js", link: "/nextjs/" },
          { label: "Angular", link: "/angular/" },
          { label: "Svelte / SvelteKit", link: "/svelte/" },
          { label: "Vue / Nuxt", link: "/vue/" },
        ] },
        { label: "Referencia API", translations: { en: "API reference" }, link: "/api/" },
        { label: "Seguridad", translations: { en: "Security" }, link: "/security/" },
        { label: "Solución de problemas", translations: { en: "Troubleshooting" }, link: "/troubleshooting/" },
      ],
      head: [
        { tag: "meta", attrs: { property: "og:image", content: `${site}${base}/images/logo.png` } },
        { tag: "meta", attrs: { name: "twitter:card", content: "summary_large_image" } },
      ],
    }),
    sitemap({ i18n: { defaultLocale: "es", locales: { es: "es", en: "en" } } }),
  ],
  vite: { plugins: [tailwindcss()] },
});
