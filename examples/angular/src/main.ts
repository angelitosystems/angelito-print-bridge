// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0
import { provideZonelessChangeDetection } from "@angular/core";
import { bootstrapApplication } from "@angular/platform-browser";
import { providePrintBridge } from "@angelitosystems/print-bridge-angular";
import { AppComponent } from "./app/app.component";

bootstrapApplication(AppComponent, {
  providers: [
    provideZonelessChangeDetection(),
    providePrintBridge({ options: { appId: "com.angelitosystems.example.angular", appName: "Ejemplo Angular" } }),
  ],
}).catch((e) => console.error(e));
