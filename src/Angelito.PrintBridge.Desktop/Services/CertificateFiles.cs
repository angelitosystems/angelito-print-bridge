// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

namespace Angelito.PrintBridge.Desktop.Services;

/// <summary>Writes a freshly generated application certificate and its private key for the developer to deploy.</summary>
public static class CertificateFiles
{
    public static string Save(GeneratedCertificate g)
    {
        var slug = new string(g.Certificate.Name.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray()).Trim('-');
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Angelito Print Bridge", "certificados",
            $"{(slug.Length > 0 ? slug : "app")}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "certificate.pem"), g.CertificatePem);
        File.WriteAllText(Path.Combine(folder, "private-key.pem"), g.PrivateKeyPem);
        File.WriteAllText(Path.Combine(folder, "LEEME.txt"), $"""
            Certificado de aplicación — Angelito Print Bridge
            Aplicación: {g.Certificate.Name}
            Origen autorizado: {g.Certificate.Origin}
            Permisos: {string.Join(", ", g.Certificate.Permissions)}
            Huella SHA-256: {g.Certificate.Fingerprint}
            Vence: {g.Certificate.NotAfter:yyyy-MM-dd}

            certificate.pem  → público: inclúyalo en su web y páselo al SDK (opción `certificate.pem`).
            private-key.pem  → SECRETO: guárdelo solo en su servidor. Nunca en JavaScript, repositorios ni correos.

            Su backend debe firmar el texto que el SDK le envía (SHA-256, RSA PKCS#1 v1.5) y devolverlo en Base64.
            Ejemplos: docs/CERTIFICATES.md del repositorio.

            Otros equipos: importe certificate.pem en Angelito Print Bridge › Certificados con el mismo origen.
            """);
        return folder;
    }
}
