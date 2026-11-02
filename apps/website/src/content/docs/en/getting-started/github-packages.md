---
title: Install the SDKs from GitHub Packages
description: npm configuration and authentication for the @angelitosystems packages.
sidebar: { order: 4 }
---
Packages are published to **GitHub Packages** (`https://npm.pkg.github.com`), not npmjs.com.
GitHub Packages **requires authentication to install npm packages, even public ones**.

1. Create a classic token with `read:packages`. In GitHub Actions use `GITHUB_TOKEN` with `permissions: packages: read`.
2. Configure the registry (never commit tokens):
```ini
@angelitosystems:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${GITHUB_PACKAGES_TOKEN}
```
3. Install: `npm install @angelitosystems/print-bridge`

Only the core package (0.3.0) is published today; the React, Angular, Svelte and Vue integrations are in the repository
and will be published after approval.
