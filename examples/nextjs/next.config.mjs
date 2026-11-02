// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
// Static export so the demo can be hosted on GitHub Pages under EXAMPLE_BASE.
const basePath = (process.env.EXAMPLE_BASE ?? "/").replace(/\/$/, "");

export default { output: "export", basePath, trailingSlash: true, env: { NEXT_PUBLIC_BASE_PATH: basePath } };
