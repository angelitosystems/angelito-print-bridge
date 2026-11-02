// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Jobs;
using Angelito.PrintBridge.Core.Persistence;
using Angelito.PrintBridge.Core.Security;
using Microsoft.Extensions.Options;

namespace Angelito.PrintBridge.Agent;

public sealed record PairRequestBody(string AppId, string AppName, string[] Permissions, string Verifier);
public sealed record RedeemBody(string Verifier);
public sealed record SessionBody(string GrantToken);
public sealed record CertificatePairBody(string AppId, string AppName, string[] Permissions, string Certificate, long Timestamp, string Nonce, string Signature);
public sealed record CertificateImportBody(string Certificate, string Origin, string Name, string[] Permissions);
public sealed record CertificateGenerateBody(string Origin, string Name, string[] Permissions, int Years = 3);
public sealed record JobMeta(DocumentKind Kind, string? PrinterId, PrintOptions? Options, int Priority = 0, string? IdempotencyKey = null);

public sealed record JobDto(
    Guid JobId, JobState State, string PrinterId, DocumentKind Kind, int Attempts, Confirmation Confirmation,
    ApiErrorBody? Error, string? SpoolerStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static JobDto From(PrintJob j) => new(j.Id, j.State, j.PrinterId, j.Kind, j.Attempts, j.Confirmation,
        j.ErrorCode is null ? null : new ApiErrorBody(j.ErrorCode, j.ErrorMessage ?? ""), j.SpoolerStatus, j.CreatedAt, j.UpdatedAt);
}

public sealed record TestPrintBody(string PrinterId);
public sealed record AdminJobDto(JobDto Job, string AppName);
public sealed record AgentStatus(string Version, int Port, string DataDirectory, DateTimeOffset StartedAt, bool VirtualPrinter,
    int QueuedJobs, int PendingPairings, int Grants);

public static class Endpoints
{
    /// <summary>Owner of test pages printed from the desktop app. Not a real grant: it can never authenticate.</summary>
    public const string LocalAdminGrant = "local-admin";
    public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static readonly string Version = typeof(Endpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static string Origin(HttpContext ctx) => OriginPolicy.Normalize(ctx.Request.Headers.Origin)!;

    /// <summary>Resolves the session and checks the permission; returns null and writes nothing on success.</summary>
    private static (Session? Session, IResult? Error) Authorize(HttpContext ctx, SessionService sessions, string? permission)
    {
        var s = sessions.Validate(HttpSecurity.BearerToken(ctx.Request), Origin(ctx));
        if (s is null) return (null, HttpSecurity.Problem(401, "unauthorized", "Sesión inválida o vencida."));
        if (permission is not null && !s.Permissions.Contains(permission))
            return (null, HttpSecurity.Problem(403, "permission_denied", $"La aplicación no tiene el permiso '{permission}'."));
        return (s, null);
    }

    public static void MapBridgeApi(this WebApplication app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/hello", () => Results.Ok(new { product = "Angelito Print Bridge", vendor = "Angelito Systems S.A.C.", version = Version, apiVersion = 1 }));

        // ---------- Pairing ----------
        v1.MapPost("/pair/requests", (PairRequestBody body, HttpContext ctx, PairingService pairing) =>
        {
            try
            {
                var r = pairing.Create(body.AppId, body.AppName, Origin(ctx), body.Permissions ?? [], body.Verifier ?? "");
                return Results.Json(new { requestId = r.Id, code = r.Code, expiresAt = r.ExpiresAt, origin = r.Origin }, statusCode: 201);
            }
            catch (PairingException ex)
            {
                return HttpSecurity.Problem(ex.Code == "too_many_requests" ? 429 : 400, ex.Code, ex.Message);
            }
        }).RequireRateLimiting("pair");

        v1.MapPost("/pair/requests/{id:guid}/redeem", (Guid id, RedeemBody body, HttpContext ctx, PairingService pairing) =>
        {
            var r = pairing.Redeem(id, Origin(ctx), body.Verifier ?? "");
            if (r is null) return HttpSecurity.Problem(404, "pair_request_not_found", "Solicitud inexistente, vencida o de otro origen.");
            return r.Value.Status switch
            {
                PairStatus.Approved => Results.Ok(new { status = "approved", grantToken = r.Value.GrantToken, grantId = r.Value.GrantId, permissions = r.Value.Permissions }),
                PairStatus.Denied => HttpSecurity.Problem(403, "pair_denied", "El usuario rechazó el emparejamiento."),
                _ => Results.Ok(new { status = "pending" }),
            };
        }).RequireRateLimiting("api");

        v1.MapPost("/pair/certificate", (CertificatePairBody body, HttpContext ctx, PairingService pairing) =>
        {
            try
            {
                var (grant, token) = pairing.PairWithCertificate(body.AppId ?? "", body.AppName ?? "", Origin(ctx), body.Permissions ?? [],
                    body.Certificate ?? "", body.Timestamp, body.Nonce ?? "", body.Signature ?? "");
                return Results.Ok(new { status = "approved", grantToken = token, grantId = grant.Id, permissions = grant.Permissions });
            }
            catch (PairingException ex)
            {
                var status = ex.Code is "invalid_app_id" or "invalid_app_name" or "invalid_permissions" or "invalid_nonce" or "invalid_certificate" ? 400 : 403;
                return HttpSecurity.Problem(status, ex.Code, ex.Message);
            }
        }).RequireRateLimiting("pair");

        v1.MapPost("/session", (SessionBody body, HttpContext ctx, SessionService sessions, BridgeStore store) =>
        {
            var issued = sessions.Issue(body.GrantToken ?? "", Origin(ctx));
            if (issued is null)
            {
                store.Audit("auth.rejected", origin: Origin(ctx), detail: "invalid or revoked grant");
                return HttpSecurity.Problem(401, "invalid_grant", "Autorización inválida o revocada. Vuelva a emparejar.");
            }
            var (token, s) = issued.Value;
            return Results.Ok(new { sessionToken = token, expiresAt = s.ExpiresAt, grantId = s.GrantId, permissions = s.Permissions });
        }).RequireRateLimiting("pair");

        // ---------- Printers ----------
        v1.MapGet("/printers", (HttpContext ctx, SessionService sessions, IPrinterCatalog catalog) =>
        {
            var (_, err) = Authorize(ctx, sessions, Permissions.PrintersRead);
            return err ?? Results.Ok(catalog.List());
        }).RequireRateLimiting("api");

        // ---------- Jobs ----------
        v1.MapPost("/jobs", async (HttpContext ctx, SessionService sessions, IPrinterCatalog catalog, JobService jobs,
            IOptions<BridgeOptions> opt, BridgeStore store) =>
        {
            var (s, err) = Authorize(ctx, sessions, null);
            if (err is not null) return err;
            if (!ctx.Request.HasFormContentType) return HttpSecurity.Problem(415, "invalid_request", "Use multipart/form-data con campos 'job' y 'document'.");

            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            JobMeta? meta;
            try { meta = JsonSerializer.Deserialize<JobMeta>(form["job"].ToString(), JsonDefaults.Options); }
            catch (JsonException) { meta = null; }
            var file = form.Files.GetFile("document");
            if (meta is null || file is null) return HttpSecurity.Problem(400, "invalid_request", "Faltan 'job' o 'document'.");
            if (!s!.Permissions.Contains(Permissions.For(meta.Kind)))
                return HttpSecurity.Problem(403, "permission_denied", $"La aplicación no tiene el permiso '{Permissions.For(meta.Kind)}'.");
            var max = meta.Kind == DocumentKind.Raw ? opt.Value.MaxRawBytes : opt.Value.MaxDocumentBytes;
            if (file.Length == 0 || file.Length > max) return HttpSecurity.Problem(413, "document_too_large", $"El documento debe tener entre 1 y {max} bytes.");
            if (meta.IdempotencyKey is { Length: > 128 }) return HttpSecurity.Problem(400, "invalid_request", "idempotencyKey supera 128 caracteres.");

            var printerId = meta.PrinterId ?? catalog.DefaultPrinterId();
            if (printerId is null || catalog.Find(printerId) is null)
                return HttpSecurity.Problem(404, ErrorCodes.PrinterNotFound, "Impresora no encontrada.");

            var job = new PrintJob
            {
                Id = Guid.NewGuid(),
                GrantId = s.GrantId,
                IdempotencyKey = meta.IdempotencyKey ?? ctx.Request.Headers["Idempotency-Key"].FirstOrDefault(),
                PrinterId = printerId,
                Kind = meta.Kind,
                Options = meta.Options ?? new(),
                Priority = Math.Clamp(meta.Priority, -10, 10),
                SizeBytes = file.Length,
            };
            await using var stream = file.OpenReadStream();
            var (saved, created) = await jobs.EnqueueAsync(job, stream, ctx.RequestAborted);
            return Results.Json(JobDto.From(saved), JsonDefaults.Options, statusCode: created ? 202 : 200);
        }).RequireRateLimiting("api").DisableAntiforgery();

        v1.MapGet("/jobs", (HttpContext ctx, SessionService sessions, BridgeStore store, int? limit) =>
        {
            var (s, err) = Authorize(ctx, sessions, null);
            return err ?? Results.Ok(store.ListJobs(s!.GrantId, limit ?? 50).Select(JobDto.From));
        }).RequireRateLimiting("api");

        v1.MapGet("/jobs/{id:guid}", (Guid id, HttpContext ctx, SessionService sessions, BridgeStore store) =>
        {
            var (s, err) = Authorize(ctx, sessions, null);
            if (err is not null) return err;
            var job = store.GetJob(id);
            return job is null || job.GrantId != s!.GrantId
                ? HttpSecurity.Problem(404, "job_not_found", "Trabajo no encontrado.")
                : Results.Ok(JobDto.From(job));
        }).RequireRateLimiting("api");

        v1.MapPost("/jobs/{id:guid}/cancel", (Guid id, HttpContext ctx, SessionService sessions, JobService jobs, BridgeStore store) =>
        {
            var (s, err) = Authorize(ctx, sessions, null);
            if (err is not null) return err;
            var result = jobs.Cancel(id, s!.GrantId);
            return result switch
            {
                JobService.CancelResult.NotFound => HttpSecurity.Problem(404, "job_not_found", "Trabajo no encontrado."),
                JobService.CancelResult.AlreadyFinished => HttpSecurity.Problem(409, "job_finished", "El trabajo ya terminó."),
                JobService.CancelResult.NotCancellable => HttpSecurity.Problem(409, "not_cancellable", "No se pudo cancelar en este estado."),
                _ => Results.Ok(new { result = result.ToString().ToLowerInvariant(), job = JobDto.From(store.GetJob(id)!) }),
            };
        }).RequireRateLimiting("api");

        v1.Map("/events", EventsSocket);

        MapAdmin(v1.MapGroup("/admin"));
    }

    /// <summary>
    /// WebSocket job events. Browsers cannot set headers on WebSocket, and tokens in URLs leak into logs,
    /// so the first message must be {"type":"auth","sessionToken":"..."}.
    /// </summary>
    private static async Task EventsSocket(HttpContext ctx, SessionService sessions, JobService jobs)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            await HttpSecurity.Error(ctx, 400, "websocket_required", "Se requiere WebSocket.");
            return;
        }
        var origin = Origin(ctx);
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[4096];
        using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        authTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        Session? session = null;
        try
        {
            var first = await ws.ReceiveAsync(buffer, authTimeout.Token);
            if (first.MessageType == WebSocketMessageType.Text && first.EndOfMessage)
            {
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, first.Count));
                if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "auth" &&
                    doc.RootElement.TryGetProperty("sessionToken", out var tok))
                    session = sessions.Validate(tok.GetString(), origin);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or WebSocketException) { }

        if (session is null)
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync((WebSocketCloseStatus)4401, "unauthorized", CancellationToken.None);
            return;
        }

        var outbox = Channel.CreateBounded<object>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
        void OnJob(PrintJob j)
        {
            if (j.GrantId == session.GrantId) outbox.Writer.TryWrite(new { type = "job", job = JobDto.From(j) });
        }
        jobs.JobChanged += OnJob;
        try
        {
            outbox.Writer.TryWrite(new { type = "ready", grantId = session.GrantId });
            var sender = Task.Run(async () =>
            {
                await foreach (var msg in outbox.Reader.ReadAllAsync(ctx.RequestAborted))
                    await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg, JsonDefaults.Options), WebSocketMessageType.Text, true, ctx.RequestAborted);
            });
            while (ws.State == WebSocketState.Open && session.ExpiresAt > DateTimeOffset.UtcNow)
            {
                var r = await ws.ReceiveAsync(buffer, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
            outbox.Writer.TryComplete();
            await sender.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally { jobs.JobChanged -= OnJob; }
    }

    private static object CertificateDto(TrustedCertificate c) =>
        new { c.Fingerprint, c.Name, c.Origin, c.Permissions, c.NotAfter, c.CreatedAt };

    private static IResult? Validate(string? origin, string? name, string[]? permissions)
    {
        if (OriginPolicy.Normalize(origin) is null) return HttpSecurity.Problem(400, "invalid_origin", "Origen inválido: use https://dominio (o http://localhost para desarrollo).");
        if (!OriginPolicy.IsValidAppName(name)) return HttpSecurity.Problem(400, "invalid_name", "Nombre inválido.");
        if (permissions is not { Length: > 0 } || permissions.Any(p => !Permissions.All.Contains(p)))
            return HttpSecurity.Problem(400, "invalid_permissions", "Permisos inválidos.");
        return null;
    }

    private static int ActiveJobs(BridgeStore s) =>
        s.ListJobs(null, 500, JobState.Queued).Count + s.ListJobs(null, 500, JobState.Rendering).Count;

    /// <summary>
    /// Push channel for the desktop app (like QZ Tray's local socket): notifies pairing requests and job changes
    /// instantly. Authenticated by the admin endpoint filter (Admin token header); pages can never reach it.
    /// </summary>
    private static async Task AdminEventsSocket(HttpContext ctx, PairingService pairing, JobService jobs)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            await HttpSecurity.Error(ctx, 400, "websocket_required", "Se requiere WebSocket.");
            return;
        }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        void OnPairing() => outbox.Writer.TryWrite("pairing");
        void OnJob(PrintJob _) => outbox.Writer.TryWrite("job");
        pairing.Changed += OnPairing;
        jobs.JobChanged += OnJob;
        try
        {
            outbox.Writer.TryWrite("ready");
            var sender = Task.Run(async () =>
            {
                await foreach (var type in outbox.Reader.ReadAllAsync(ctx.RequestAborted))
                    await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type }), WebSocketMessageType.Text, true, ctx.RequestAborted);
            });
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open)
                if ((await ws.ReceiveAsync(buffer, ctx.RequestAborted)).MessageType == WebSocketMessageType.Close) break;
            outbox.Writer.TryComplete();
            await sender.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { });
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally
        {
            pairing.Changed -= OnPairing;
            jobs.JobChanged -= OnJob;
        }
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.AddEndpointFilter(async (ic, next) =>
        {
            var ctx = ic.HttpContext;
            var token = HttpSecurity.BearerToken(ctx.Request, "Admin");
            var expected = ctx.RequestServices.GetRequiredKeyedService<string>("admin-token");
            if (token is null || !Tokens.FixedTimeEquals(token, expected))
                return HttpSecurity.Problem(401, "unauthorized", "Token de administración inválido.");
            return await next(ic);
        });

        admin.Map("/events", AdminEventsSocket);
        admin.MapGet("/pair-requests", (PairingService p) => p.Pending().Select(r => new
        {
            code = r.Code,
            appId = r.AppId,
            appName = r.AppName,
            origin = r.Origin,
            permissions = r.Permissions,
            expiresAt = r.ExpiresAt,
        }));
        admin.MapPost("/pair-requests/{code}/approve", (string code, PairingService p) =>
            p.Approve(code) is { } g ? Results.Ok(new { grantId = g.Id, g.AppId, g.Origin, g.Permissions }) : HttpSecurity.Problem(404, "not_found", "Código no encontrado o vencido."));
        admin.MapPost("/pair-requests/{code}/deny", (string code, PairingService p) =>
            p.Deny(code) ? Results.Ok() : HttpSecurity.Problem(404, "not_found", "Código no encontrado o vencido."));
        admin.MapGet("/grants", (BridgeStore s) => s.ListGrants().Select(g => new { g.Id, g.AppId, g.AppName, g.Origin, g.Permissions, g.CreatedAt }));
        admin.MapDelete("/grants/{id}", (string id, BridgeStore s, SessionService sessions) =>
        {
            var revoked = s.RevokeGrants(id: id);
            foreach (var g in revoked)
            {
                sessions.RevokeGrant(g);
                s.Audit("grant.revoked", g);
            }
            return revoked.Count > 0 ? Results.Ok() : HttpSecurity.Problem(404, "not_found", "Autorización no encontrada.");
        });
        admin.MapGet("/jobs", (BridgeStore s, int? limit) =>
        {
            var apps = s.ListGrants(includeRevoked: true).ToDictionary(g => g.Id, g => g.AppName);
            apps[LocalAdminGrant] = "Página de prueba";
            return s.ListJobs(null, limit ?? 100).Select(j => new AdminJobDto(JobDto.From(j), apps.GetValueOrDefault(j.GrantId, "?")));
        });
        admin.MapPost("/jobs/{id:guid}/cancel", (Guid id, JobService jobs) => jobs.Cancel(id, null) switch
        {
            JobService.CancelResult.Cancelled or JobService.CancelResult.Requested => Results.Ok(),
            JobService.CancelResult.NotFound => HttpSecurity.Problem(404, "job_not_found", "Trabajo no encontrado."),
            _ => HttpSecurity.Problem(409, "not_cancellable", "No se puede cancelar en este estado."),
        });
        admin.MapGet("/printers", (IPrinterCatalog c) => c.List());
        admin.MapPost("/printers/test", async (TestPrintBody body, IPrinterCatalog catalog, JobService jobs) =>
        {
            if (catalog.Find(body.PrinterId) is null) return HttpSecurity.Problem(404, ErrorCodes.PrinterNotFound, "Impresora no encontrada.");
            var pdf = TestPage.Build(body.PrinterId, Version, DateTimeOffset.Now);
            var job = new PrintJob
            {
                Id = Guid.NewGuid(),
                GrantId = LocalAdminGrant,
                PrinterId = body.PrinterId,
                Kind = DocumentKind.Pdf,
                SizeBytes = pdf.Length,
            };
            var (saved, _) = await jobs.EnqueueAsync(job, new MemoryStream(pdf), CancellationToken.None);
            return Results.Json(JobDto.From(saved), JsonDefaults.Options, statusCode: 202);
        });
        admin.MapGet("/certificates", (BridgeStore s) => s.ListTrustedCertificates().Select(CertificateDto));
        admin.MapPost("/certificates", (CertificateImportBody body, BridgeStore s) =>
        {
            if (Validate(body.Origin, body.Name, body.Permissions) is { } error) return error;
            try
            {
                using var cert = AppCertificates.Parse(body.Certificate ?? "");
                var trusted = new TrustedCertificate(AppCertificates.Fingerprint(cert), body.Name, OriginPolicy.Normalize(body.Origin)!,
                    body.Permissions.Distinct().ToList(), cert.ExportCertificatePem(), cert.NotAfter.ToUniversalTime(), DateTimeOffset.UtcNow);
                s.TrustCertificate(trusted);
                s.Audit("certificate.trusted", origin: trusted.Origin, detail: $"cert={trusted.Fingerprint[..16]} imported");
                return Results.Ok(CertificateDto(trusted));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return HttpSecurity.Problem(400, "invalid_certificate", "El archivo no es un certificado PEM válido.");
            }
        });
        admin.MapPost("/certificates/generate", (CertificateGenerateBody body, BridgeStore s) =>
        {
            if (Validate(body.Origin, body.Name, body.Permissions) is { } error) return error;
            var origin = OriginPolicy.Normalize(body.Origin)!;
            var g = AppCertificates.Generate(body.Name, origin, body.Years);
            var trusted = new TrustedCertificate(g.Fingerprint, body.Name, origin, body.Permissions.Distinct().ToList(), g.CertificatePem, g.NotAfter, DateTimeOffset.UtcNow);
            s.TrustCertificate(trusted);
            s.Audit("certificate.trusted", origin: origin, detail: $"cert={g.Fingerprint[..16]} generated");
            // The private key is returned once and never stored by the agent.
            return Results.Ok(new { certificate = CertificateDto(trusted), certificatePem = g.CertificatePem, privateKeyPem = g.PrivateKeyPem });
        });
        admin.MapDelete("/certificates/{fingerprint}", (string fingerprint, BridgeStore s, SessionService sessions) =>
        {
            if (!s.RevokeCertificate(fingerprint)) return HttpSecurity.Problem(404, "not_found", "Certificado no encontrado.");
            foreach (var g in s.RevokeGrants(certificateFingerprint: fingerprint)) sessions.RevokeGrant(g);
            s.Audit("certificate.revoked", detail: $"cert={fingerprint[..Math.Min(16, fingerprint.Length)]}");
            return Results.Ok();
        });
        admin.MapGet("/status", (BridgeStore s, PairingService p, IOptions<BridgeOptions> o) => new AgentStatus(
            Version, o.Value.Port, o.Value.DataDirectory, StartedAt, o.Value.VirtualPrinterEnabled,
            ActiveJobs(s), p.Pending().Count, s.ListGrants().Count));
        admin.MapPost("/shutdown", (IHostApplicationLifetime life, BridgeStore s, bool? ifIdle) =>
        {
            // Installers/updaters pass ifIdle so a job being rendered or spooled is never interrupted.
            if (ifIdle == true && ActiveJobs(s) > 0)
                return HttpSecurity.Problem(409, "jobs_active", "Hay trabajos en proceso; intente de nuevo en unos segundos.");
            s.Audit("agent.shutdown", detail: "requested by local admin");
            life.StopApplication();
            return Results.Accepted();
        });
        admin.MapGet("/audit", (BridgeStore s, int? limit) => s.ListAudit(limit ?? 200));
    }
}
