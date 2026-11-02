# Copyright 2026 Angelito Systems S.A.C.
# SPDX-License-Identifier: Apache-2.0
<#
.SYNOPSIS
  Publica agente + app de escritorio (autocontenidos, sin requerir .NET instalado) y, si Inno Setup está
  disponible, compila el instalador.
.EXAMPLE
  ./installer/publish.ps1                 # x64
  ./installer/publish.ps1 -Runtime win-arm64
#>
param(
    [ValidateSet("win-x64", "win-arm64")] [string] $Runtime = "win-x64",
    [string] $Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts/$Runtime/app"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

foreach ($proj in "src/Angelito.PrintBridge.Agent", "src/Angelito.PrintBridge.Desktop") {
    dotnet publish (Join-Path $root $proj) -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=none -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló: $proj" }
}

foreach ($f in "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md", "TRADEMARKS.md") { Copy-Item (Join-Path $root $f) $out }

[xml] $props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
Write-Host "Publicado $version ($Runtime) en $out"

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue)?.Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    Write-Warning "Inno Setup 6 no está instalado: se omite el instalador. Carpeta lista en $out"
    return
}
$arch = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64compatible" }
& $iscc "/DAppVersion=$version" "/DSourceDir=$out" "/DArch=$arch" "/DRuntime=$Runtime" (Join-Path $PSScriptRoot "AngelitoPrintBridge.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC falló" }
# Firma de código: ver docs/RELEASE.md (se hace en CI con secretos; nunca con certificados en el repositorio).
