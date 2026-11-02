// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace Angelito.PrintBridge.Agent;

/// <summary>Generates a one-page A4 PDF test page (standard Helvetica, WinAnsi) without third-party libraries.</summary>
public static class TestPage
{
    public static byte[] Build(string printerName, string version, DateTimeOffset at)
    {
        var lines = new (int Size, string Text)[]
        {
            (22, "Angelito Print Bridge"),
            (14, "Página de prueba"),
            (11, ""),
            (11, $"Impresora: {printerName}"),
            (11, $"Fecha: {at.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}"),
            (11, $"Versión del agente: {version}"),
            (11, ""),
            (11, "Si puede leer esta página, la impresión silenciosa funciona."),
            (11, "Angelito Systems S.A.C. - angelitosystems.net"),
        };
        var content = new StringBuilder("0.13 0.33 0.85 rg 56 770 483 3 re f 0 g\n");
        var y = 740;
        foreach (var (size, text) in lines)
        {
            content.Append(CultureInfo.InvariantCulture, $"BT /F1 {size} Tf 56 {y} Td ({Escape(text)}) Tj ET\n");
            y -= size + 10;
        }

        var latin1 = Encoding.Latin1;
        var stream = latin1.GetBytes(content.ToString());
        var objects = new List<byte[]>
        {
            latin1.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            latin1.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            latin1.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"),
            latin1.GetBytes($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(latin1.GetBytes("\nendstream")).ToArray(),
            latin1.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(latin1.GetBytes(s));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n");
            ms.Write(objects[i]);
            Write("\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    // Printer names are user-controlled: escape PDF string delimiters and drop non-Latin-1 characters.
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\').Append(ch);
            else if (ch < 0x20) continue;
            else sb.Append(ch <= 0xFF ? ch : '?');
        }
        return sb.ToString();
    }
}
