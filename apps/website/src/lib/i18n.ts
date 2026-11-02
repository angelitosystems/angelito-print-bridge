// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
export type Lang = "es" | "en";

export const base = import.meta.env.BASE_URL.replace(/\/?$/, "/");

/** Localized internal link, base-aware: href("en", "download/") → /angelito-print-bridge/en/download/ */
export function href(lang: Lang, path = ""): string {
  return `${base}${lang === "en" ? "en/" : ""}${path}`;
}

export const t = {
  es: {
    nav: { docs: "Documentación", frameworks: "Frameworks", examples: "Ejemplos", playground: "Laboratorio", download: "Descargar" },
    heroTitle: "Impresión local sin límites desde tus aplicaciones",
    heroSubtitle: "Conecta tus aplicaciones web con impresoras y dispositivos locales mediante un puente seguro, rápido y de código abierto.",
    ctaDownload: "Descargar para Windows",
    ctaStart: "Comenzar",
    ctaGithub: "Ver en GitHub",
    footer: "Software libre bajo Apache License 2.0 · Creado por Angelito Systems S.A.C.",
  },
  en: {
    nav: { docs: "Docs", frameworks: "Frameworks", examples: "Examples", playground: "Playground", download: "Download" },
    heroTitle: "Unlimited local printing from your applications",
    heroSubtitle: "Connect your web apps to local printers and devices through a secure, fast and open source bridge.",
    ctaDownload: "Download for Windows",
    ctaStart: "Get started",
    ctaGithub: "View on GitHub",
    footer: "Free software under the Apache License 2.0 · Created by Angelito Systems S.A.C.",
  },
} as const;

export const softwareJsonLd = {
  "@context": "https://schema.org", "@type": "SoftwareApplication", name: "Angelito Print Bridge",
  operatingSystem: "Windows 10, Windows 11", applicationCategory: "DeveloperApplication",
  offers: { "@type": "Offer", price: "0", priceCurrency: "USD" }, license: "https://www.apache.org/licenses/LICENSE-2.0",
  author: { "@type": "Organization", name: "Angelito Systems S.A.C.", url: "https://angelitosystems.net" },
};
