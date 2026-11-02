// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Persistence;

namespace Angelito.PrintBridge.Core.Security;

public static class Tokens
{
    public static string New(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool HashEquals(string token, string expectedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(expectedHash));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static partial class OriginPolicy
{
    private static readonly HashSet<string> LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    /// <summary>
    /// Normalizes a browser Origin. Only https origins are accepted, plus http on loopback for local development.
    /// Returns null for anything else (including "null", file:, paths, credentials).
    /// </summary>
    public static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 255) return null;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var u)) return null;
        if (u.AbsolutePath != "/" || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment) || !string.IsNullOrEmpty(u.UserInfo)) return null;
        if (origin != origin.Trim()) return null;
        var host = u.Host.ToLowerInvariant();
        var ok = u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && IsLoopback(host));
        if (!ok) return null;
        return u.IsDefaultPort ? $"{u.Scheme}://{u.IdnHost.ToLowerInvariant()}" : $"{u.Scheme}://{u.IdnHost.ToLowerInvariant()}:{u.Port}";
    }

    /// <summary>DNS-rebinding defence: the Host header must name the loopback interface, never an attacker domain.</summary>
    public static bool IsAllowedHost(string? hostHeaderWithoutPort) =>
        hostHeaderWithoutPort is not null && IsLoopback(hostHeaderWithoutPort.ToLowerInvariant());

    private static bool IsLoopback(string host) => LoopbackHosts.Contains(host);

    [GeneratedRegex(@"^[a-z0-9]+(\.[a-z0-9-]+){1,9}$")]
    private static partial Regex AppIdRegex();

    public static bool IsValidAppId(string? appId) => appId is { Length: <= 100 } && AppIdRegex().IsMatch(appId);

    public static bool IsValidAppName(string? name) =>
        name is { Length: > 0 and <= 80 } && name.All(ch => !char.IsControl(ch));
}

public sealed record Session(string GrantId, string AppId, string Origin, IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAt);

/// <summary>Short-lived, in-memory session tokens derived from a long-lived grant. Lost on restart by design.</summary>
public sealed class SessionService(BridgeStore store, TimeProvider time, TimeSpan lifetime)
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public (string Token, Session Session)? Issue(string grantToken, string origin)
    {
        var grant = store.FindActiveGrantByTokenHash(Tokens.Hash(grantToken));
        if (grant is null || grant.Origin != origin) return null;
        Sweep();
        var session = new Session(grant.Id, grant.AppId, grant.Origin, grant.Permissions.ToHashSet(), time.GetUtcNow() + lifetime);
        var token = Tokens.New();
        _sessions[Tokens.Hash(token)] = session;
        return (token, session);
    }

    public Session? Validate(string? token, string origin)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(Tokens.Hash(token), out var s)) return null;
        if (s.ExpiresAt <= time.GetUtcNow() || s.Origin != origin) return null;
        return s;
    }

    public void RevokeGrant(string grantId)
    {
        foreach (var (k, s) in _sessions)
            if (s.GrantId == grantId) _sessions.TryRemove(k, out _);
    }

    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var (k, s) in _sessions)
            if (s.ExpiresAt <= now) _sessions.TryRemove(k, out _);
    }
}

public enum PairStatus { Pending, Approved, Denied }

public sealed class PairRequest
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public required string Origin { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required string VerifierHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public PairStatus Status { get; set; }
    internal string? GrantToken { get; set; }
    internal string? GrantId { get; set; }
}

public sealed class PairingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Pairing: a web origin asks for permissions; a local user approves by matching a 6-digit code shown on both sides.
/// The resulting grant token is released once, only to the same origin holding the original verifier.
/// </summary>
public sealed class PairingService(BridgeStore store, SessionService sessions, TimeProvider time, TimeSpan lifetime)
{
    private const int MaxPending = 20;
    private const int MaxPendingPerOrigin = 3;
    private readonly ConcurrentDictionary<Guid, PairRequest> _requests = new();
    private readonly Lock _gate = new();

    /// <summary>Raised after a pairing request is created, approved or denied (pushed to the desktop app).</summary>
    public event Action? Changed;

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); } catch (Exception) { } // a failing listener must never break pairing
    }

    public PairRequest Create(string appId, string appName, string origin, IReadOnlyList<string> permissions, string verifier)
    {
        if (!OriginPolicy.IsValidAppId(appId)) throw new PairingException("invalid_app_id", "appId inválido (ej. com.empresa.app).");
        if (!OriginPolicy.IsValidAppName(appName)) throw new PairingException("invalid_app_name", "appName inválido.");
        if (verifier is not { Length: >= 43 and <= 128 }) throw new PairingException("invalid_verifier", "verifier debe tener entre 43 y 128 caracteres.");
        var perms = permissions.Distinct().ToList();
        if (perms.Count == 0 || perms.Any(p => !Domain.Permissions.All.Contains(p)))
            throw new PairingException("invalid_permissions", "Permisos inválidos.");

        lock (_gate)
        {
            Sweep();
            var pending = _requests.Values.Where(r => r.Status == PairStatus.Pending).ToList();
            if (pending.Count >= MaxPending || pending.Count(r => r.Origin == origin) >= MaxPendingPerOrigin)
                throw new PairingException("too_many_requests", "Demasiadas solicitudes de emparejamiento pendientes.");
            string code;
            do code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            while (pending.Any(r => r.Code == code));
            var req = new PairRequest
            {
                Id = Guid.NewGuid(),
                Code = code,
                AppId = appId,
                AppName = appName,
                Origin = origin,
                Permissions = perms,
                VerifierHash = Tokens.Hash(verifier),
                ExpiresAt = time.GetUtcNow() + lifetime,
            };
            _requests[req.Id] = req;
            store.Audit("pair.requested", origin: origin, detail: $"app={appId} perms={string.Join(' ', perms)}");
            RaiseChanged();
            return req;
        }
    }

    public IReadOnlyList<PairRequest> Pending()
    {
        lock (_gate)
        {
            Sweep();
            return _requests.Values.Where(r => r.Status == PairStatus.Pending).OrderBy(r => r.ExpiresAt).ToList();
        }
    }

    public Grant? Approve(string code)
    {
        lock (_gate)
        {
            Sweep();
            var req = _requests.Values.FirstOrDefault(r => r.Status == PairStatus.Pending && r.Code == code);
            if (req is null) return null;
            var (grant, token) = IssueGrant(req.AppId, req.AppName, req.Origin, req.Permissions, null);
            req.Status = PairStatus.Approved;
            req.GrantToken = token;
            req.GrantId = grant.Id;
            store.Audit("pair.approved", grant.Id, grant.Origin, $"app={grant.AppId}");
            RaiseChanged();
            return grant;
        }
    }

    /// <summary>One active grant per (app, origin): re-pairing replaces the old one.</summary>
    private (Grant Grant, string Token) IssueGrant(string appId, string appName, string origin, IReadOnlyList<string> permissions, string? certificateFingerprint)
    {
        foreach (var old in store.RevokeGrants(appId: appId, origin: origin)) sessions.RevokeGrant(old);
        var token = Tokens.New();
        var grant = new Grant
        {
            Id = "g_" + Tokens.New(9),
            AppId = appId,
            AppName = appName,
            Origin = origin,
            Permissions = permissions,
            TokenHash = Tokens.Hash(token),
            CreatedAt = time.GetUtcNow(),
            CertificateFingerprint = certificateFingerprint,
        };
        store.InsertGrant(grant);
        return (grant, token);
    }

    private static readonly TimeSpan SignatureWindow = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new();

    /// <summary>
    /// Pairing without a manual approval: the request is signed by the private key of a certificate the local
    /// administrator already trusts for this origin. Replay-protected by timestamp window + single-use nonce.
    /// </summary>
    public (Grant Grant, string Token) PairWithCertificate(string appId, string appName, string origin, IReadOnlyList<string> permissions,
        string certificatePem, long timestamp, string nonce, string signatureBase64)
    {
        if (!OriginPolicy.IsValidAppId(appId)) throw new PairingException("invalid_app_id", "appId inválido (ej. com.empresa.app).");
        if (!OriginPolicy.IsValidAppName(appName)) throw new PairingException("invalid_app_name", "appName inválido.");
        var perms = permissions.Distinct().ToList();
        if (perms.Count == 0 || perms.Any(p => !Domain.Permissions.All.Contains(p)))
            throw new PairingException("invalid_permissions", "Permisos inválidos.");
        if (nonce is not { Length: >= 16 and <= 128 }) throw new PairingException("invalid_nonce", "nonce debe tener entre 16 y 128 caracteres.");

        System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
        byte[] signature;
        try
        {
            cert = AppCertificates.Parse(certificatePem);
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new PairingException("invalid_certificate", "Certificado o firma con formato inválido.");
        }
        using var _ = cert;
        var fingerprint = AppCertificates.Fingerprint(cert);
        var trusted = store.FindTrustedCertificate(fingerprint);
        var now = time.GetUtcNow();
        if (trusted is null) Reject("untrusted_certificate", "Este certificado no está autorizado en este equipo.");
        if (trusted!.Origin != origin) Reject("origin_mismatch", "El certificado no está autorizado para este origen.");
        if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime()) Reject("certificate_expired", "El certificado está vencido o aún no es válido.");
        if (perms.Any(p => !trusted.Permissions.Contains(p))) Reject("permission_not_allowed", "El certificado no autoriza alguno de los permisos solicitados.");
        if ((now - DateTimeOffset.FromUnixTimeMilliseconds(timestamp)).Duration() > SignatureWindow)
            Reject("stale_signature", "La firma está fuera de la ventana de 5 minutos. Revise el reloj del equipo.");
        if (!AppCertificates.Verify(cert, AppCertificates.SigningMessage(origin, appId, perms, timestamp, nonce), signature))
            Reject("invalid_signature", "La firma no corresponde al certificado.");
        foreach (var (k, exp) in _usedNonces)
            if (exp < now) _usedNonces.TryRemove(k, out var _x);
        if (!_usedNonces.TryAdd(fingerprint + ":" + nonce, now + SignatureWindow * 2)) Reject("replayed_request", "Solicitud repetida.");

        lock (_gate)
        {
            var issued = IssueGrant(appId, appName, origin, perms, fingerprint);
            store.Audit("pair.certificate", issued.Grant.Id, origin, $"app={appId} cert={fingerprint[..16]}");
            return issued;
        }

        void Reject(string code, string message)
        {
            store.Audit("auth.rejected", origin: origin, detail: $"certificate pairing: {code}");
            throw new PairingException(code, message);
        }
    }

    public bool Deny(string code)
    {
        lock (_gate)
        {
            var req = _requests.Values.FirstOrDefault(r => r.Status == PairStatus.Pending && r.Code == code);
            if (req is null) return false;
            req.Status = PairStatus.Denied;
            store.Audit("pair.denied", origin: req.Origin, detail: $"app={req.AppId}");
            RaiseChanged();
            return true;
        }
    }

    /// <summary>Polled by the requesting page. Returns the grant token exactly once.</summary>
    public (PairStatus Status, string? GrantToken, string? GrantId, IReadOnlyList<string> Permissions)? Redeem(Guid id, string origin, string verifier)
    {
        lock (_gate)
        {
            Sweep();
            if (!_requests.TryGetValue(id, out var req) || req.Origin != origin || !Tokens.HashEquals(verifier, req.VerifierHash))
                return null;
            if (req.Status == PairStatus.Approved)
            {
                _requests.TryRemove(id, out _);
                return (req.Status, req.GrantToken, req.GrantId, req.Permissions);
            }
            if (req.Status == PairStatus.Denied) _requests.TryRemove(id, out _);
            return (req.Status, null, null, req.Permissions);
        }
    }

    private void Sweep()
    {
        var now = time.GetUtcNow();
        foreach (var (k, r) in _requests)
            if (r.ExpiresAt <= now) _requests.TryRemove(k, out _);
    }
}
