# Avisos de terceros

Angelito Print Bridge (Apache-2.0, © 2026 Angelito Systems S.A.C.) utiliza los siguientes componentes.
Todas las licencias son compatibles con la distribución bajo Apache-2.0, incluido el uso comercial.
Los textos completos de cada licencia están en los repositorios enlazados y dentro de cada paquete NuGet/npm.

## Distribuidos con la aplicación (binarios)
| Componente | Licencia | Proyecto |
|---|---|---|
| .NET Runtime, ASP.NET Core, Microsoft.Extensions.* | MIT | https://github.com/dotnet/runtime · https://github.com/dotnet/aspnetcore |
| Microsoft.Data.Sqlite | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite (motor nativo) | Dominio público | https://sqlite.org/copyright.html |
| System.Drawing.Common, System.Security.Cryptography.ProtectedData | MIT | https://github.com/dotnet/runtime · https://github.com/dotnet/winforms |
| Microsoft.Windows.SDK.NET (proyecciones WinRT) | MIT | https://github.com/microsoft/CsWinRT |
| Serilog, Serilog.AspNetCore, Serilog.Sinks.File, Serilog.Formatting.Compact | Apache-2.0 | https://github.com/serilog/serilog |
| Avalonia (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia) | MIT | https://github.com/AvaloniaUI/Avalonia |
| SkiaSharp / HarfBuzzSharp (dependencias de Avalonia) | MIT | https://github.com/mono/SkiaSharp |
| Skia | BSD-3-Clause | https://skia.org |
| HarfBuzz | MIT ("Old MIT") | https://github.com/harfbuzz/harfbuzz |
| Fuente Inter (Avalonia.Fonts.Inter) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Iconos: Material Design Icons (rutas SVG de home, printer, list, shield-check, tune, information) | Apache-2.0 | https://github.com/Templarian/MaterialDesign |

## Solo para compilar o probar (no se distribuyen)
| Componente | Licencia |
|---|---|
| xUnit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing, coverlet.collector | MIT |
| TypeScript | Apache-2.0 |
| Inno Setup 6 (genera el instalador; el instalador resultante puede distribuirse comercialmente) | Licencia de Inno Setup |
| CycloneDX .NET (SBOM en CI) | Apache-2.0 |

El SDK `@angelitosystems/print-bridge` no tiene dependencias en tiempo de ejecución.

## Componentes del sistema operativo usados, no distribuidos
Windows Print Spooler (winspool), `Windows.Data.Pdf`, GDI+ y DPAPI forman parte de Windows y se usan mediante sus APIs públicas.

Esta lista se revisa en cada versión (ver [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md)). El SBOM CycloneDX generado por CI es la referencia exhaustiva, incluidas las dependencias transitivas.
