// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing.Printing;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Windows;

namespace Angelito.PrintBridge.Tests;

/// <summary>Exercises the real Windows components (PDF engine, GDI, spooler). No paper is ever printed:
/// spooler output is redirected to files.</summary>
public class WindowsTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public async Task Windows_pdf_engine_renders_real_pdf_at_requested_dpi()
    {
        using var pdf = await PdfPageSource.LoadAsync(File.ReadAllBytes(Path.Combine(Fixtures, "sample.pdf")));
        Assert.Equal(1, pdf.Count);
        var size = pdf.PageSizeInches(0);
        Assert.Equal(8.26, size.Width, 1); // A4
        pdf.Render(0, 150).Dispose(); // first render calibrates the display scale factor
        using var bmp = pdf.Render(0, 150);
        Assert.InRange(bmp.Width, 1235, 1245);
        Assert.InRange(bmp.HorizontalResolution, 149, 151);
        if (Environment.GetEnvironmentVariable("APB_DUMP_RENDER") is { } dump) bmp.Save(dump);
        // The page must contain ink (the text), not just a white canvas.
        var dark = 0;
        for (var y = 0; y < bmp.Height / 4; y += 2)
            for (var x = 0; x < bmp.Width; x += 2)
                if (bmp.GetPixel(x, y).GetBrightness() < 0.5f) dark++;
        Assert.True(dark > 50, $"expected rendered text, found {dark} dark pixels");
    }

    [Fact]
    public async Task Corrupt_pdf_is_rejected_as_invalid_document()
    {
        var ex = await Assert.ThrowsAsync<PrintException>(() => PdfPageSource.LoadAsync(File.ReadAllBytes(Path.Combine(Fixtures, "corrupt.pdf"))));
        Assert.Equal(ErrorCodes.InvalidDocument, ex.Code);
    }

    [Theory]
    [InlineData(DocumentKind.Pdf, new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C })]
    [InlineData(DocumentKind.Image, new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    public void Magic_bytes_are_validated(DocumentKind kind, byte[] head) =>
        Assert.Equal(ErrorCodes.InvalidDocument, Assert.Throws<PrintException>(() => DocumentValidation.Check(kind, head)).Code);

    [Fact]
    public void Lists_installed_printers_with_declared_capabilities()
    {
        var catalog = new WindowsPrinterCatalog(new VirtualPrinterOptions(true, Path.GetTempPath()), TimeProvider.System);
        var printers = catalog.List();
        Assert.Contains(printers, p => p.Id == VirtualPrinterOptions.Id && p.IsVirtual);
        Assert.All(printers, p => Assert.NotEmpty(p.Status));
        Assert.True(printers.Count(p => p.IsDefault) <= 1);
    }

    [Fact]
    public async Task Virtual_printer_writes_rendered_pages()
    {
        using var dir = new TempDir();
        var driver = new WindowsPrintDriver(new VirtualPrinterOptions(true, dir.Path));
        var payload = Path.Combine(dir.Path, "p.bin");
        File.Copy(Path.Combine(Fixtures, "sample.pdf"), payload);
        var job = new PrintJob { Id = Guid.NewGuid(), GrantId = "g", PrinterId = VirtualPrinterOptions.Id, Kind = DocumentKind.Pdf };
        var spooled = false;
        await driver.SubmitAsync(new SubmitRequest(job, payload, () => spooled = true), CancellationToken.None);
        Assert.True(spooled);
        Assert.True(File.Exists(Path.Combine(dir.Path, $"{job.Id:N}-p1.png")));
    }

    /// <summary>
    /// Real GDI → driver → spooler path, redirected to a file. Opt-in only: set APB_TEST_PRINTER to a driver known to
    /// honour print-to-file. Never auto-picked, because some drivers (e.g. OneNote) open applications even when printing to file.
    /// </summary>
    [Fact]
    public async Task Gdi_path_spools_pdf_through_a_real_driver_to_file()
    {
        var printer = Environment.GetEnvironmentVariable("APB_TEST_PRINTER");
        if (string.IsNullOrEmpty(printer)) return;
        using var dir = new TempDir();
        var output = Path.Combine(dir.Path, "out.prn");
        using var pdf = await PdfPageSource.LoadAsync(File.ReadAllBytes(Path.Combine(Fixtures, "sample.pdf")));
        var spooled = false;
        await Task.Run(() => GdiPrinter.Print(printer, "APB-test", pdf, new PrintOptions(), output, () => spooled = true, CancellationToken.None));
        Assert.True(spooled);
        for (var i = 0; i < 50 && (!File.Exists(output) || new FileInfo(output).Length == 0); i++) await Task.Delay(200);
        Assert.True(new FileInfo(output).Length > 0, $"driver '{printer}' produced no output");
    }

    [Fact]
    public void Unsupported_options_are_reported_not_ignored()
    {
        // Fails during option validation, before anything is spooled. OneNote excluded: its driver launches the app.
        var printer = Environment.GetEnvironmentVariable("APB_TEST_PRINTER") ?? PrinterSettings.InstalledPrinters.Cast<string>()
            .FirstOrDefault(n => !n.Contains("OneNote", StringComparison.OrdinalIgnoreCase) && new PrinterSettings { PrinterName = n }.IsValid);
        if (printer is null) return;
        using var pdfStub = new ImagePageSource(File.ReadAllBytes(Path.Combine(Fixtures, "dot.png")));
        var ex = Assert.Throws<PrintException>(() => GdiPrinter.Print(printer, "APB-test", pdfStub,
            new PrintOptions { PaperSize = "Papel inexistente 123" }, Path.Combine(Path.GetTempPath(), "never.prn"), () => { }, CancellationToken.None));
        Assert.Equal(ErrorCodes.UnsupportedOption, ex.Code);
    }

    [Fact]
    public void Printer_status_flags_are_decoded()
    {
        Assert.Equal(["ready"], WindowsPrinterCatalog.DescribeStatus(0, 0));
        Assert.Equal(["paperOut", "offline"], WindowsPrinterCatalog.DescribeStatus(0x10, 0x400));
    }
}
