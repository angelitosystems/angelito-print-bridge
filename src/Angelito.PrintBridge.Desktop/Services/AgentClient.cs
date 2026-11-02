// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Windows;

namespace Angelito.PrintBridge.Desktop.Services;

public sealed record AgentStatus(string Version, int Port, string DataDirectory, DateTimeOffset StartedAt, bool VirtualPrinter,
    int QueuedJobs, int PendingPairings, int Grants);
public sealed record PairRequestInfo(string Code, string AppId, string AppName, string Origin, string[] Permissions, DateTimeOffset ExpiresAt);
public sealed record GrantInfo(string Id, string AppId, string AppName, string Origin, string[] Permissions, DateTimeOffset CreatedAt);
public sealed record JobError(string Code, string Message);
public sealed record JobInfo(Guid JobId, string State, string PrinterId, string Kind, int Attempts, string Confirmation,
    JobError? Error, string? SpoolerStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AdminJob(JobInfo Job, string AppName);
public sealed record PrinterCaps(bool Duplex, bool Color, int MaxCopies, string[] PaperSizes);
public sealed record PrinterItem(string Id, string Name, bool IsDefault, bool IsVirtual, string[] Status, PrinterCaps? Capabilities);
public sealed record CertificateInfo(string Fingerprint, string Name, string Origin, string[] Permissions, DateTimeOffset NotAfter, DateTimeOffset CreatedAt);
public sealed record GeneratedCertificate(CertificateInfo Certificate, string CertificatePem, string PrivateKeyPem);
public sealed record AuditItem(long Id, DateTimeOffset At, string Event, string? GrantId, string? Origin, string? Detail);

public class AgentUnavailableException(string message) : Exception(message);

/// <summary>Something answers on the agent port but it is not this user's agent (another copy or another Windows user).</summary>
public sealed class AgentNotOwnedException(int port) : AgentUnavailableException(
    $"El puerto {port} lo usa otra instancia de Angelito Print Bridge (otra copia o otro usuario de Windows). Ciérrela para usar esta.");

/// <summary>Client for the agent's local admin API. Authenticates with the DPAPI-protected admin token.</summary>
public sealed class AgentClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _tokenPath;

    public AgentClient(int port, string dataDirectory)
    {
        Port = port;
        DataDirectory = dataDirectory;
        _tokenPath = BridgeDefaults.AdminTokenPath(dataDirectory);
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/v1/"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public int Port { get; }
    public string DataDirectory { get; }

    public async Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var hello = await _http.GetFromJsonAsync<JsonElement>("hello", cts.Token);
            return hello.TryGetProperty("product", out var p) && p.GetString() == "Angelito Print Bridge";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return false; }
    }

    public Task<AgentStatus> StatusAsync() => Get<AgentStatus>("admin/status");
    public Task<PairRequestInfo[]> PairRequestsAsync() => Get<PairRequestInfo[]>("admin/pair-requests");
    public Task<GrantInfo[]> GrantsAsync() => Get<GrantInfo[]>("admin/grants");
    public Task<AdminJob[]> JobsAsync(int limit = 200) => Get<AdminJob[]>($"admin/jobs?limit={limit}");
    public Task<PrinterItem[]> PrintersAsync() => Get<PrinterItem[]>("admin/printers");
    public Task<AuditItem[]> AuditAsync(int limit = 200) => Get<AuditItem[]>($"admin/audit?limit={limit}");

    public Task<CertificateInfo[]> CertificatesAsync() => Get<CertificateInfo[]>("admin/certificates");
    public Task<GeneratedCertificate> GenerateCertificateAsync(string origin, string name, string[] permissions) =>
        Post<GeneratedCertificate>("admin/certificates/generate", new { origin, name, permissions, years = 3 });
    public Task<CertificateInfo> ImportCertificateAsync(string pem, string origin, string name, string[] permissions) =>
        Post<CertificateInfo>("admin/certificates", new { certificate = pem, origin, name, permissions });
    public Task RevokeCertificateAsync(string fingerprint) => Send(HttpMethod.Delete, $"admin/certificates/{Uri.EscapeDataString(fingerprint)}");

    public Task ApproveAsync(string code) => Send(HttpMethod.Post, $"admin/pair-requests/{Uri.EscapeDataString(code)}/approve");
    public Task DenyAsync(string code) => Send(HttpMethod.Post, $"admin/pair-requests/{Uri.EscapeDataString(code)}/deny");
    public Task RevokeAsync(string grantId) => Send(HttpMethod.Delete, $"admin/grants/{Uri.EscapeDataString(grantId)}");
    public Task CancelJobAsync(Guid id) => Send(HttpMethod.Post, $"admin/jobs/{id}/cancel");
    public Task TestPrintAsync(string printerId) => Send(HttpMethod.Post, "admin/printers/test", new { printerId });
    public Task ShutdownAsync(bool ifIdle = false) => Send(HttpMethod.Post, ifIdle ? "admin/shutdown?ifIdle=true" : "admin/shutdown");

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        // Called only after the port answered: without our token, that agent belongs to someone else.
        if (!File.Exists(_tokenPath)) throw new AgentNotOwnedException(Port);
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Admin", AdminToken.Read(_tokenPath));
        return req;
    }

    private async Task<T> Get<T>(string path)
    {
        using var res = await Execute(Request(HttpMethod.Get, path));
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> Post<T>(string path, object body)
    {
        var req = Request(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        using var res = await Execute(req);
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task Send(HttpMethod method, string path, object? body = null)
    {
        var req = Request(method, path);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var _ = await Execute(req);
    }

    private async Task<HttpResponseMessage> Execute(HttpRequestMessage req)
    {
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AgentUnavailableException("El agente no responde.");
        }
        finally { req.Dispose(); }
        if (res.IsSuccessStatusCode) return res;
        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && req.RequestUri?.ToString().StartsWith("admin/") == true)
        {
            res.Dispose();
            throw new AgentNotOwnedException(Port);
        }
        var msg = await res.Content.ReadAsStringAsync();
        res.Dispose();
        try { msg = JsonDocument.Parse(msg).RootElement.GetProperty("error").GetProperty("message").GetString() ?? msg; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        throw new InvalidOperationException(msg);
    }

    /// <summary>
    /// Push channel from the agent (pairing requests, job changes). Returns when the connection closes; the caller
    /// reconnects. Each event type ("ready", "pairing", "job") is passed to <paramref name="onEvent"/>.
    /// </summary>
    public async Task ListenAsync(Func<string, Task> onEvent, CancellationToken ct)
    {
        if (!File.Exists(_tokenPath)) throw new AgentNotOwnedException(Port);
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Admin {AdminToken.Read(_tokenPath)}");
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/v1/admin/events"), ct);
        var buffer = new byte[1024];
        while (ws.State == System.Net.WebSockets.WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
            var type = JsonDocument.Parse(buffer.AsMemory(0, r.Count)).RootElement.GetProperty("type").GetString();
            if (type is not null) await onEvent(type);
        }
    }

    public void Dispose() => _http.Dispose();
}
