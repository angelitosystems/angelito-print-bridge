// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Drawing.Printing;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;

namespace Angelito.PrintBridge.Windows;

public sealed record VirtualPrinterOptions(bool Enabled, string OutputDirectory)
{
    public const string Id = "virtual:file";
}

/// <summary>User-initiated changes to Windows printer settings (only called from an explicit UI action).</summary>
public static class WindowsPrinters
{
    public static void SetDefault(string printerName) => WinSpool.SetDefaultPrinter(printerName);

    /// <summary>True when Windows' "let Windows manage my default printer" is on: it may later switch the default to the last used printer.</summary>
    public static bool WindowsManagesDefault()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
        return key?.GetValue("LegacyDefaultPrinterMode") is not int legacy || legacy == 0;
    }
}

/// <summary>Printers installed in Windows (local + connections). Capabilities are what the driver declares.</summary>
public sealed class WindowsPrinterCatalog(VirtualPrinterOptions virtualPrinter, TimeProvider time) : IPrinterCatalog
{
    private static readonly TimeSpan CapabilityTtl = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, (PrinterCapabilities? Caps, DateTimeOffset At)> _caps = new();
    private readonly Lock _gate = new();

    public IReadOnlyList<PrinterInfo> List()
    {
        var def = WinSpool.DefaultPrinter();
        var list = WinSpool.ListPrinters()
            .Where(p => p.pPrinterName is not null)
            .Select(p => new PrinterInfo(p.pPrinterName!, p.pPrinterName!, p.pPrinterName == def, false, DescribeStatus(p.Status, p.Attributes),
                Capabilities(p.pPrinterName!)))
            .ToList();
        if (virtualPrinter.Enabled)
            list.Add(new PrinterInfo(VirtualPrinterOptions.Id, "Angelito Virtual (archivo)", false, true, ["ready"],
                new PrinterCapabilities(false, true, 1, [])));
        return list;
    }

    public PrinterInfo? Find(string printerId) => List().FirstOrDefault(p => p.Id == printerId);

    public string? DefaultPrinterId() => WinSpool.DefaultPrinter();

    private PrinterCapabilities? Capabilities(string name)
    {
        lock (_gate)
        {
            if (_caps.TryGetValue(name, out var c) && time.GetUtcNow() - c.At < CapabilityTtl) return c.Caps;
            PrinterCapabilities? caps = null;
            try
            {
                var ps = new PrinterSettings { PrinterName = name };
                if (ps.IsValid)
                    caps = new PrinterCapabilities(ps.CanDuplex, ps.SupportsColor, Math.Max(1, ps.MaximumCopies),
                        ps.PaperSizes.Cast<PaperSize>().Select(p => p.PaperName).Distinct().ToList());
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidPrinterException) { }
            _caps[name] = (caps, time.GetUtcNow());
            return caps;
        }
    }

    internal static IReadOnlyList<string> DescribeStatus(uint status, uint attributes)
    {
        var s = new List<string>();
        if ((status & 0x1) != 0) s.Add("paused");
        if ((status & 0x2) != 0) s.Add("error");
        if ((status & 0x8) != 0) s.Add("paperJam");
        if ((status & 0x10) != 0) s.Add("paperOut");
        if ((status & 0x80) != 0 || (attributes & 0x400) != 0) s.Add("offline");
        if ((status & 0x1000) != 0) s.Add("notAvailable");
        if ((status & 0x40000) != 0) s.Add("noToner");
        if ((status & 0x400000) != 0) s.Add("doorOpen");
        if (s.Count == 0) s.Add("ready");
        return s;
    }
}

/// <summary>Submits jobs to the Windows spooler: RAW via WritePrinter, PDF/images via GDI.</summary>
public sealed class WindowsPrintDriver(VirtualPrinterOptions virtualPrinter) : IPrintDriver
{
    public async Task SubmitAsync(SubmitRequest request, CancellationToken ct)
    {
        var job = request.Job;
        var bytes = await File.ReadAllBytesAsync(request.PayloadPath, ct);
        DocumentValidation.Check(job.Kind, bytes.AsSpan(0, Math.Min(16, bytes.Length)));

        var isVirtual = job.PrinterId == VirtualPrinterOptions.Id;
        if (!isVirtual)
        {
            using var probe = WinSpool.TryOpen(job.PrinterId)
                ?? throw new PrintException(ErrorCodes.PrinterNotFound, "La impresora no existe o no es accesible.");
        }

        if (isVirtual)
        {
            if (!virtualPrinter.Enabled) throw new PrintException(ErrorCodes.PrinterNotFound, "La impresora virtual está deshabilitada.");
            Directory.CreateDirectory(virtualPrinter.OutputDirectory);
        }

        // Spooler and GDI calls are blocking; keep them off the request threads.
        await Task.Run(async () =>
        {
            try
            {
                if (job.Kind == DocumentKind.Raw)
                {
                    RejectLayoutOptionsForRaw(job.Options);
                    if (job.Options.Copies is < 1 or > 99) throw new PrintException(ErrorCodes.UnsupportedOption, "copies debe estar entre 1 y 99.");
                    if (isVirtual)
                    {
                        request.OnSpoolStarting();
                        var path = Path.Combine(virtualPrinter.OutputDirectory, $"{job.Id:N}.bin");
                        await File.WriteAllBytesAsync(path, Enumerable.Repeat(bytes, job.Options.Copies).SelectMany(b => b).ToArray(), ct);
                        return;
                    }
                    WinSpool.WriteRaw(job.PrinterId, job.SpoolDocumentName, bytes, job.Options.Copies, null, request.OnSpoolStarting, ct);
                    return;
                }

                using IPageSource pages = job.Kind == DocumentKind.Pdf ? await PdfPageSource.LoadAsync(bytes) : new ImagePageSource(bytes);
                if (isVirtual)
                {
                    // Virtual output = the exact rasters a physical printer would receive, one PNG per page.
                    request.OnSpoolStarting();
                    for (var i = 0; i < pages.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var bmp = pages.Render(i, 150);
                        bmp.Save(Path.Combine(virtualPrinter.OutputDirectory, $"{job.Id:N}-p{i + 1}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    return;
                }
                GdiPrinter.Print(job.PrinterId, job.SpoolDocumentName, pages, job.Options, null, request.OnSpoolStarting, ct);
            }
            catch (Win32Exception ex)
            {
                // Typical causes: printer offline, spooler restarting, driver rejecting RAW. Retry only if nothing was spooled.
                throw new PrintException(ErrorCodes.SpoolerError, $"Error del spooler de Windows ({ex.NativeErrorCode}): {ex.Message}", transient: true);
            }
            catch (InvalidPrinterException)
            {
                throw new PrintException(ErrorCodes.PrinterNotFound, "La impresora no existe o no es accesible.");
            }
        }, ct);
    }

    private static void RejectLayoutOptionsForRaw(PrintOptions o)
    {
        if (o.Orientation is not null || o.PaperSize is not null || o.Duplex is not null || o.Scale != ScaleMode.Fit)
            throw new PrintException(ErrorCodes.UnsupportedOption, "Las opciones de diseño no aplican a trabajos RAW; use comandos del lenguaje de la impresora.");
    }

    public SpoolStatus GetSpoolStatus(PrintJob job)
    {
        using var h = job.PrinterId == VirtualPrinterOptions.Id ? null : WinSpool.TryOpen(job.PrinterId);
        if (h is null) return new SpoolStatus(false, false, false, null);
        var match = WinSpool.ListJobs(h).Cast<WinSpool.JOB_INFO_1?>().FirstOrDefault(j => j!.Value.pDocument == job.SpoolDocumentName);
        if (match is not { } j) return new SpoolStatus(false, false, false, null);
        return new SpoolStatus(true,
            (j.Status & WinSpool.JOB_STATUS_PRINTED) != 0,
            (j.Status & (WinSpool.JOB_STATUS_DELETING | WinSpool.JOB_STATUS_DELETED)) != 0,
            DescribeJob(j.Status, j.pStatus));
    }

    public bool TryCancelSpooled(PrintJob job)
    {
        using var h = job.PrinterId == VirtualPrinterOptions.Id ? null : WinSpool.TryOpen(job.PrinterId);
        if (h is null) return false;
        var match = WinSpool.ListJobs(h).Where(j => j.pDocument == job.SpoolDocumentName).Select(j => (uint?)j.JobId).FirstOrDefault();
        return match is { } id && WinSpool.DeleteJob(h, id);
    }

    internal static string DescribeJob(uint status, string? driverText)
    {
        var parts = new List<string>();
        if ((status & WinSpool.JOB_STATUS_ERROR) != 0) parts.Add("error");
        if ((status & WinSpool.JOB_STATUS_OFFLINE) != 0) parts.Add("offline");
        if ((status & WinSpool.JOB_STATUS_PAPEROUT) != 0) parts.Add("paperOut");
        if ((status & WinSpool.JOB_STATUS_USER_INTERVENTION) != 0) parts.Add("userIntervention");
        if ((status & WinSpool.JOB_STATUS_BLOCKED_DEVQ) != 0) parts.Add("blocked");
        if ((status & WinSpool.JOB_STATUS_PRINTING) != 0) parts.Add("printing");
        if (parts.Count == 0) parts.Add("spooled");
        if (!string.IsNullOrWhiteSpace(driverText)) parts.Add(driverText.Trim());
        return string.Join(", ", parts);
    }
}
