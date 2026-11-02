// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;
using System.Drawing.Printing;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Angelito.PrintBridge.Windows;

/// <summary>A document as a sequence of pages rendered on demand (one page in memory at a time).</summary>
internal interface IPageSource : IDisposable
{
    int Count { get; }
    SizeF PageSizeInches(int index);
    Bitmap Render(int index, int dpi);
}

internal static class DocumentValidation
{
    public static void Check(DocumentKind kind, ReadOnlySpan<byte> head)
    {
        var ok = kind switch
        {
            DocumentKind.Pdf => head.StartsWith("%PDF-"u8),
            DocumentKind.Image => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) // PNG
                                  || head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })    // JPEG
                                  || head.StartsWith("BM"u8),                              // BMP
            _ => true,
        };
        if (!ok) throw new PrintException(ErrorCodes.InvalidDocument, $"El contenido no es un {kind} válido.");
    }
}

/// <summary>Rasterizes PDF pages with the Windows built-in PDF engine (Windows.Data.Pdf). No third-party code.</summary>
internal sealed class PdfPageSource : IPageSource
{
    public const int MaxPages = 1000;
    private readonly PdfDocument _pdf;
    private readonly IRandomAccessStream _source; // pages are read lazily: must outlive the document

    private PdfPageSource(PdfDocument pdf, IRandomAccessStream source) => (_pdf, _source) = (pdf, source);

    public static async Task<PdfPageSource> LoadAsync(byte[] bytes)
    {
        var source = new MemoryStream(bytes, writable: false).AsRandomAccessStream();
        PdfDocument pdf;
        try
        {
            pdf = await PdfDocument.LoadFromStreamAsync(source);
            if (pdf.PageCount == 0) throw new PrintException(ErrorCodes.InvalidDocument, "El PDF no tiene páginas.");
            if (pdf.PageCount > MaxPages) throw new PrintException(ErrorCodes.InvalidDocument, $"El PDF supera {MaxPages} páginas.");
        }
        catch (Exception ex)
        {
            source.Dispose();
            if (ex is PrintException) throw;
            throw new PrintException(ErrorCodes.InvalidDocument, "PDF dañado, cifrado o no soportado.");
        }
        return new PdfPageSource(pdf, source);
    }

    public int Count => (int)_pdf.PageCount;

    public SizeF PageSizeInches(int index)
    {
        using var page = _pdf.GetPage((uint)index);
        return new SizeF((float)page.Size.Width / 96f, (float)page.Size.Height / 96f); // Size is in DIPs
    }

    // Windows.Data.Pdf applies the display scale factor to DestinationWidth/Height; calibrated on first render.
    private static double _displayScale;

    public Bitmap Render(int index, int dpi)
    {
        using var page = _pdf.GetPage((uint)index);
        var widthIn = page.Size.Width / 96;
        var scale = _displayScale > 0 ? _displayScale : 1;
        var wantedPx = Math.Max(1, widthIn * dpi);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, wantedPx / scale),
            DestinationHeight = (uint)Math.Max(1, page.Size.Height / 96 * dpi / scale),
        };
        using var ras = new InMemoryRandomAccessStream();
        page.RenderToStreamAsync(ras, options).AsTask().GetAwaiter().GetResult(); // PNG by default
        ras.Seek(0);
        using var png = ras.AsStreamForRead();
        using var decoded = new Bitmap(png);
        if (_displayScale == 0) _displayScale = decoded.Width / (double)options.DestinationWidth;
        var bmp = new Bitmap(decoded); // detach from the stream
        var actualDpi = (float)(bmp.Width / widthIn);
        bmp.SetResolution(actualDpi, actualDpi);
        return bmp;
    }

    public void Dispose() => _source.Dispose();
}

internal sealed class ImagePageSource(byte[] bytes) : IPageSource
{
    private readonly Bitmap _image = Load(bytes);

    private static Bitmap Load(byte[] b)
    {
        try
        {
            using var ms = new MemoryStream(b, writable: false);
            using var img = new Bitmap(ms);
            return new Bitmap(img);
        }
        catch (ArgumentException) { throw new PrintException(ErrorCodes.InvalidDocument, "Imagen dañada o no soportada."); }
    }

    public int Count => 1;

    public SizeF PageSizeInches(int index)
    {
        float dx = _image.HorizontalResolution > 1 ? _image.HorizontalResolution : 96, dy = _image.VerticalResolution > 1 ? _image.VerticalResolution : 96;
        return new SizeF(_image.Width / dx, _image.Height / dy);
    }

    public Bitmap Render(int index, int dpi) => new(_image);

    public void Dispose() => _image.Dispose();
}

/// <summary>Prints page sources through GDI (System.Drawing.Printing) silently — no dialogs, no GPU required.</summary>
internal static class GdiPrinter
{
    private const int RenderDpi = 300;

    public static void Print(string printerName, string docName, IPageSource pages, PrintOptions o, string? outputFile,
        Action onSpoolStarting, CancellationToken ct)
    {
        using var doc = new PrintDocument();
        doc.PrinterSettings.PrinterName = printerName;
        if (!doc.PrinterSettings.IsValid) throw new PrintException(ErrorCodes.PrinterNotFound, "La impresora no existe.");
        doc.DocumentName = docName;
        doc.PrintController = new StandardPrintController(); // default controller shows a progress window
        Apply(doc, o, pages);
        if (outputFile is not null)
        {
            doc.PrinterSettings.PrintToFile = true;
            doc.PrinterSettings.PrintFileName = outputFile;
        }

        var index = 0;
        Exception? pageError = null;
        doc.PrintPage += (_, e) =>
        {
            try
            {
                if (ct.IsCancellationRequested) { e.Cancel = true; return; }
                DrawPage(e, pages, index, o.Scale);
                index++;
                e.HasMorePages = index < pages.Count;
            }
            catch (Exception ex)
            {
                pageError = ex;
                e.Cancel = true;
            }
        };
        onSpoolStarting();
        doc.Print();
        ct.ThrowIfCancellationRequested();
        if (pageError is not null)
            throw pageError as PrintException ?? new PrintException(ErrorCodes.InvalidDocument, "No se pudo renderizar una página.");
    }

    private static void Apply(PrintDocument doc, PrintOptions o, IPageSource pages)
    {
        var ps = doc.PrinterSettings;
        if (o.Copies < 1 || o.Copies > Math.Max(1, ps.MaximumCopies))
            throw new PrintException(ErrorCodes.UnsupportedOption, $"copies debe estar entre 1 y {Math.Max(1, ps.MaximumCopies)} para esta impresora.");
        ps.Copies = (short)o.Copies;

        if (o.Duplex is { } d && d != DuplexMode.Simplex)
        {
            if (!ps.CanDuplex) throw new PrintException(ErrorCodes.UnsupportedOption, "La impresora no declara soporte dúplex.");
            ps.Duplex = d == DuplexMode.LongEdge ? Duplex.Vertical : Duplex.Horizontal;
        }
        else if (o.Duplex == DuplexMode.Simplex) ps.Duplex = Duplex.Simplex;

        if (o.PaperSize is { } paperName)
        {
            var paper = ps.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => string.Equals(p.PaperName, paperName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PrintException(ErrorCodes.UnsupportedOption, $"La impresora no declara el tamaño de papel '{paperName}'.");
            doc.DefaultPageSettings.PaperSize = paper;
        }

        if (o.Orientation is { } orientation) doc.DefaultPageSettings.Landscape = orientation == Orientation.Landscape;
        else
        {
            var first = pages.PageSizeInches(0);
            doc.DefaultPageSettings.Landscape = first.Width > first.Height;
        }
    }

    private static void DrawPage(PrintPageEventArgs e, IPageSource pages, int index, ScaleMode scale)
    {
        var g = e.Graphics!;
        var area = g.VisibleClipBounds; // printable area in 1/100 inch, origin at hard margin, orientation-aware
        var dpi = (int)Math.Min(RenderDpi, Math.Max(150, g.DpiX));
        using var bmp = pages.Render(index, dpi);
        var size = pages.PageSizeInches(index);
        float w = size.Width * 100, h = size.Height * 100;
        if (scale == ScaleMode.Fit || w > area.Width || h > area.Height)
        {
            var k = Math.Min(area.Width / w, area.Height / h);
            if (scale == ScaleMode.None) k = Math.Min(k, 1);
            w *= k;
            h *= k;
        }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(bmp, area.X + (area.Width - w) / 2, area.Y, w, h);
    }
}
