// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Angelito.PrintBridge.Core.Security;

/// <summary>An application certificate the local administrator trusts for one origin and a set of permissions.</summary>
public sealed record TrustedCertificate(
    string Fingerprint, string Name, string Origin, IReadOnlyList<string> Permissions, string CertificatePem,
    DateTimeOffset NotAfter, DateTimeOffset CreatedAt);

/// <summary>
/// Application identity by certificate (the model QZ Tray popularised): the site's backend keeps the private key and
/// signs each pairing request; the agent only stores the public certificate. Nothing here is a commercial license.
/// </summary>
public static class AppCertificates
{
    public const string SignaturePrefix = "angelito-print-bridge/v1";

    public sealed record Generated(string CertificatePem, string PrivateKeyPem, string Fingerprint, DateTimeOffset NotAfter);

    public static Generated Generate(string name, string origin, int years)
    {
        using var rsa = RSA.Create(2048);
        var dn = new X500DistinguishedNameBuilder();
        dn.AddCommonName(name);
        dn.AddOrganizationalUnitName(origin);
        dn.AddOrganizationName("Angelito Print Bridge application");
        var req = new CertificateRequest(dn.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var now = DateTimeOffset.UtcNow;
        using var cert = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(Math.Clamp(years, 1, 10)));
        return new Generated(cert.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), Fingerprint(cert), cert.NotAfter.ToUniversalTime());
    }

    /// <summary>Parses a PEM certificate. Throws <see cref="CryptographicException"/> when invalid.</summary>
    public static X509Certificate2 Parse(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || pem.Length > 20_000) throw new CryptographicException("Certificado vacío o demasiado grande.");
        return X509Certificate2.CreateFromPem(pem);
    }

    public static string Fingerprint(X509Certificate2 cert) => cert.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>Exact text the application's backend must sign (UTF-8, SHA-256).</summary>
    public static string SigningMessage(string origin, string appId, IEnumerable<string> permissions, long timestamp, string nonce) =>
        string.Join('\n', SignaturePrefix, origin, appId, string.Join(',', permissions.Order(StringComparer.Ordinal)), timestamp, nonce);

    /// <summary>Verifies RSA PKCS#1 v1.5 or ECDSA (IEEE P1363 as WebCrypto produces, or DER as OpenSSL produces) SHA-256 signatures.</summary>
    public static bool Verify(X509Certificate2 cert, string message, byte[] signature)
    {
        var data = Encoding.UTF8.GetBytes(message);
        using (var rsa = cert.GetRSAPublicKey())
            if (rsa is not null) return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ec = cert.GetECDsaPublicKey();
        return ec is not null &&
               (ec.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation) ||
                ec.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }
}
