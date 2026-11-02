// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Persistence;
using Angelito.PrintBridge.Core.Security;

namespace Angelito.PrintBridge.Tests;

public class CertificateTests
{
    private const string Origin = "https://clinica.example.com";
    private const string AppId = "com.example.dental";
    private static readonly string[] Perms = [Permissions.PrintersRead, Permissions.PrintDocument];

    private static (PairingService P, SessionService S, BridgeStore Store, TempDir Dir) Create()
    {
        var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var sessions = new SessionService(store, TimeProvider.System, TimeSpan.FromMinutes(15));
        return (new PairingService(store, sessions, TimeProvider.System, TimeSpan.FromMinutes(5)), sessions, store, dir);
    }

    private static AppCertificates.Generated Trust(BridgeStore store, string origin = Origin, string[]? perms = null)
    {
        var g = AppCertificates.Generate("Dental", origin, 1);
        store.TrustCertificate(new TrustedCertificate(g.Fingerprint, "Dental", origin, perms ?? Perms, g.CertificatePem, g.NotAfter, DateTimeOffset.UtcNow));
        return g;
    }

    /// <summary>What the application's backend does with its private key.</summary>
    private static string Sign(string privateKeyPem, string message)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static (long Ts, string Nonce, string Sig) Signed(AppCertificates.Generated g, string origin = Origin, string[]? perms = null, long? ts = null)
    {
        var timestamp = ts ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Tokens.New(18);
        return (timestamp, nonce, Sign(g.PrivateKeyPem, AppCertificates.SigningMessage(origin, AppId, perms ?? Perms, timestamp, nonce)));
    }

    [Fact]
    public void Trusted_certificate_pairs_without_manual_approval_and_session_works()
    {
        var (p, s, store, dir) = Create();
        using var _d = dir;
        var g = Trust(store);
        var (ts, nonce, sig) = Signed(g);
        var (grant, token) = p.PairWithCertificate(AppId, "Dental", Origin, Perms, g.CertificatePem, ts, nonce, sig);
        Assert.Equal(g.Fingerprint, grant.CertificateFingerprint);
        Assert.NotNull(s.Issue(token, Origin));
        Assert.Contains(store.ListAudit(10), a => a.Event == "pair.certificate");
    }

    [Fact]
    public void Generated_private_key_is_never_stored()
    {
        var (_, _, store, dir) = Create();
        using var _d = dir;
        var g = Trust(store);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var db = File.ReadAllText(System.IO.Path.Combine(dir.Path, "db.sqlite"), Encoding.Latin1);
        Assert.DoesNotContain("PRIVATE KEY", db);
        Assert.DoesNotContain(g.PrivateKeyPem.Split('\n')[1], db);
    }

    [Fact]
    public void Attacks_are_rejected_with_specific_codes()
    {
        var (p, _, store, dir) = Create();
        using var _d = dir;
        var g = Trust(store);

        string Code(Action a) => Assert.Throws<PairingException>(a).Code;

        var untrusted = AppCertificates.Generate("Otro", Origin, 1);
        var (t1, n1, s1) = Signed(untrusted);
        Assert.Equal("untrusted_certificate", Code(() => p.PairWithCertificate(AppId, "X", Origin, Perms, untrusted.CertificatePem, t1, n1, s1)));

        var (t2, n2, s2) = Signed(g, origin: "https://evil.example.com");
        Assert.Equal("origin_mismatch", Code(() => p.PairWithCertificate(AppId, "X", "https://evil.example.com", Perms, g.CertificatePem, t2, n2, s2)));

        var (t3, n3, s3) = Signed(g);
        Assert.Equal("invalid_signature", Code(() => p.PairWithCertificate(AppId, "X", Origin, Perms, g.CertificatePem, t3 + 1, n3, s3)));

        string[] raw = [.. Perms, Permissions.PrintRaw];
        var (t4, n4, s4) = Signed(g, perms: raw);
        Assert.Equal("permission_not_allowed", Code(() => p.PairWithCertificate(AppId, "X", Origin, raw, g.CertificatePem, t4, n4, s4)));

        var (t5, n5, s5) = Signed(g, ts: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds());
        Assert.Equal("stale_signature", Code(() => p.PairWithCertificate(AppId, "X", Origin, Perms, g.CertificatePem, t5, n5, s5)));

        var (t6, n6, s6) = Signed(g);
        p.PairWithCertificate(AppId, "X", Origin, Perms, g.CertificatePem, t6, n6, s6);
        Assert.Equal("replayed_request", Code(() => p.PairWithCertificate(AppId, "X", Origin, Perms, g.CertificatePem, t6, n6, s6)));

        Assert.Equal("invalid_certificate", Code(() => p.PairWithCertificate(AppId, "X", Origin, Perms, "not a cert", t6, Tokens.New(18), s6)));
    }

    [Theory]
    [InlineData(DSASignatureFormat.IeeeP1363FixedFieldConcatenation)] // WebCrypto
    [InlineData(DSASignatureFormat.Rfc3279DerSequence)]               // OpenSSL / PHP openssl_sign / Node crypto
    public void Imported_ecdsa_certificate_is_accepted_in_both_signature_formats(DSASignatureFormat format)
    {
        var (p, _, store, dir) = Create();
        using var _d = dir;
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=External", ec, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pem = cert.ExportCertificatePem();
        store.TrustCertificate(new TrustedCertificate(AppCertificates.Fingerprint(cert), "Ext", Origin, Perms, pem, cert.NotAfter, DateTimeOffset.UtcNow));

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Tokens.New(18);
        var sig = ec.SignData(Encoding.UTF8.GetBytes(AppCertificates.SigningMessage(Origin, AppId, Perms, ts, nonce)), HashAlgorithmName.SHA256, format);
        var (grant, _) = p.PairWithCertificate(AppId, "Ext", Origin, Perms, pem, ts, nonce, Convert.ToBase64String(sig));
        Assert.NotNull(grant);
    }

    [Fact]
    public void Revoking_the_certificate_revokes_its_grants_and_blocks_new_pairings()
    {
        var (p, s, store, dir) = Create();
        using var _d = dir;
        var g = Trust(store);
        var (ts, nonce, sig) = Signed(g);
        var (_, token) = p.PairWithCertificate(AppId, "Dental", Origin, Perms, g.CertificatePem, ts, nonce, sig);

        Assert.True(store.RevokeCertificate(g.Fingerprint));
        Assert.Single(store.RevokeGrants(certificateFingerprint: g.Fingerprint));
        Assert.Null(s.Issue(token, Origin));
        var (t2, n2, s2) = Signed(g);
        Assert.Equal("untrusted_certificate", Assert.Throws<PairingException>(() => p.PairWithCertificate(AppId, "Dental", Origin, Perms, g.CertificatePem, t2, n2, s2)).Code);
    }

    [Fact]
    public void Generated_certificate_is_rsa_2048_for_signing_only_and_bound_to_origin()
    {
        var g = AppCertificates.Generate("Dental", Origin, 3);
        using var cert = X509Certificate2.CreateFromPem(g.CertificatePem);
        Assert.Equal(2048, cert.GetRSAPublicKey()!.KeySize);
        Assert.Contains(Origin, cert.Subject);
        Assert.Equal(X509KeyUsageFlags.DigitalSignature, cert.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages);
        Assert.InRange(g.NotAfter, DateTimeOffset.UtcNow.AddYears(3).AddDays(-2), DateTimeOffset.UtcNow.AddYears(3).AddDays(1));
    }
}
