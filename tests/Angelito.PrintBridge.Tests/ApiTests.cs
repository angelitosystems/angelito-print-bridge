// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Angelito.PrintBridge.Tests;

public sealed class FakeCatalog : IPrinterCatalog
{
    public IReadOnlyList<PrinterInfo> List() =>
        [new PrinterInfo("Thermal", "Thermal", true, false, ["ready"], new PrinterCapabilities(false, false, 1, ["80mm"]))];
    public PrinterInfo? Find(string printerId) => List().FirstOrDefault(p => p.Id == printerId);
    public string? DefaultPrinterId() => "Thermal";
}

public sealed class AgentFactory : WebApplicationFactory<Program>
{
    public readonly TempDir Dir = new();
    public readonly ScriptedDriver Driver = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Bridge:DataDirectory", Dir.Path);
        builder.UseSetting("Bridge:PairRequestsPerMinutePerOrigin", "10");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IPrinterCatalog, FakeCatalog>();
            s.AddSingleton<IPrintDriver>(Driver);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Dir.Dispose();
    }
}

public class ApiTests(AgentFactory factory) : IClassFixture<AgentFactory>
{
    private static int _originSeq;

    private HttpClient Client(string? origin)
    {
        var c = factory.CreateClient();
        if (origin is not null) c.DefaultRequestHeaders.Add("Origin", origin);
        return c;
    }

    private HttpClient Admin()
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Admin", factory.Services.GetRequiredKeyedService<string>("admin-token"));
        return c;
    }

    /// <summary>Runs the complete browser pairing flow and returns a client holding a session for a fresh origin.</summary>
    private async Task<(HttpClient Client, string Origin, string Session)> Paired(params string[] permissions)
    {
        var origin = $"https://app{Interlocked.Increment(ref _originSeq)}.example.com";
        var c = Client(origin);
        var verifier = Tokens.New();
        var res = await c.PostAsJsonAsync("/v1/pair/requests", new { appId = "com.example.pos", appName = "POS", permissions, verifier });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var req = await res.Content.ReadFromJsonAsync<JsonElement>();
        var code = req.GetProperty("code").GetString();
        var id = req.GetProperty("requestId").GetString();

        var pending = await (await c.PostAsJsonAsync($"/v1/pair/requests/{id}/redeem", new { verifier })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", pending.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await Admin().PostAsync($"/v1/admin/pair-requests/{code}/approve", null)).StatusCode);
        var grant = await (await c.PostAsJsonAsync($"/v1/pair/requests/{id}/redeem", new { verifier })).Content.ReadFromJsonAsync<JsonElement>();
        var session = await (await c.PostAsJsonAsync("/v1/session", new { grantToken = grant.GetProperty("grantToken").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
        var token = session.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (c, origin, token);
    }

    private static MultipartFormDataContent Job(object meta, byte[] doc)
    {
        var form = new MultipartFormDataContent { { new StringContent(JsonSerializer.Serialize(meta)), "job" } };
        form.Add(new ByteArrayContent(doc), "document", "doc.bin");
        return form;
    }

    [Fact]
    public async Task Hello_is_public_but_everything_else_needs_a_valid_origin()
    {
        Assert.Equal(HttpStatusCode.OK, (await Client(null).GetAsync("/v1/hello")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(null).GetAsync("/v1/printers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("http://evil.example.com").GetAsync("/v1/hello")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client("https://app.example.com").GetAsync("/v1/printers")).StatusCode);
    }

    [Fact]
    public async Task Rebinding_host_is_rejected()
    {
        var c = Client("https://app.example.com");
        var req = new HttpRequestMessage(HttpMethod.Get, "/v1/hello");
        req.Headers.Host = "attacker.example.com";
        var res = await c.SendAsync(req);
        Assert.True(res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.MisdirectedRequest, res.StatusCode.ToString());
    }

    [Fact]
    public async Task Preflight_echoes_origin_and_allows_private_network()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/v1/jobs");
        req.Headers.Add("Origin", "https://app.example.com");
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Private-Network", "true");
        var res = await factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal("https://app.example.com", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", res.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
    }

    [Fact]
    public async Task Admin_api_is_unreachable_from_pages_and_without_token()
    {
        var page = Client("https://app.example.com");
        page.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Admin", factory.Services.GetRequiredKeyedService<string>("admin-token"));
        Assert.Equal(HttpStatusCode.Forbidden, (await page.GetAsync("/v1/admin/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).GetAsync("/v1/admin/grants")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Admin().GetAsync("/v1/admin/grants")).StatusCode);
    }

    [Fact]
    public async Task Session_is_bound_to_its_origin()
    {
        var (_, _, token) = await Paired(Permissions.PrintersRead);
        var other = Client("https://other.example.com");
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/v1/printers")).StatusCode);
    }

    [Fact]
    public async Task Paired_app_prints_and_follows_job_to_completion()
    {
        var (c, _, _) = await Paired(Permissions.PrintersRead, Permissions.PrintRaw);
        var printers = await c.GetFromJsonAsync<JsonElement>("/v1/printers");
        Assert.Equal("Thermal", printers[0].GetProperty("id").GetString());

        var res = await c.PostAsync("/v1/jobs", Job(new { kind = "raw", printerId = "Thermal", idempotencyKey = "ticket-1" }, [0x1B, 0x40]));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var job = await res.Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("jobId").GetString();

        var again = await c.PostAsync("/v1/jobs", Job(new { kind = "raw", printerId = "Thermal", idempotencyKey = "ticket-1" }, [0x1B, 0x40]));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(id, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString());

        var state = "";
        for (var i = 0; i < 50 && state != "completed"; i++)
        {
            await Task.Delay(200);
            state = (await c.GetFromJsonAsync<JsonElement>($"/v1/jobs/{id}")).GetProperty("state").GetString()!;
        }
        Assert.Equal("completed", state);
    }

    [Fact]
    public async Task Permissions_and_limits_are_enforced()
    {
        var (c, _, _) = await Paired(Permissions.PrintDocument);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/v1/printers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/v1/jobs", Job(new { kind = "raw" }, [1]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/v1/jobs", Job(new { kind = "pdf", printerId = "Nope" }, "%PDF-"u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync("/v1/jobs", Job(new { kind = "pdf" }, []))).StatusCode);
    }

    [Fact]
    public async Task Jobs_of_other_apps_are_invisible()
    {
        var (a, _, _) = await Paired(Permissions.PrintRaw);
        var (b, _, _) = await Paired(Permissions.PrintRaw);
        var job = await (await a.PostAsync("/v1/jobs", Job(new { kind = "raw" }, [1]))).Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("jobId").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/v1/jobs/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/v1/jobs/{id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_grant_kills_its_sessions_immediately()
    {
        var (c, origin, _) = await Paired(Permissions.PrintersRead);
        var grants = await Admin().GetFromJsonAsync<JsonElement>("/v1/admin/grants");
        var id = grants.EnumerateArray().First(g => g.GetProperty("origin").GetString() == origin).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await Admin().DeleteAsync($"/v1/admin/grants/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/v1/printers")).StatusCode);
    }

    [Fact]
    public async Task Pairing_is_rate_limited_per_origin()
    {
        var c = Client("https://flood.example.com");
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
            codes.Add((await c.PostAsJsonAsync("/v1/pair/requests", new { appId = "com.x.y", appName = "X", permissions = new[] { "printers:read" }, verifier = Tokens.New() })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
    }

    [Fact]
    public async Task Websocket_requires_auth_message_and_streams_own_job_events()
    {
        var (c, origin, token) = await Paired(Permissions.PrintRaw);
        var wsClient = factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = r => r.Headers.Origin = origin;

        using (var bad = await wsClient.ConnectAsync(new Uri(factory.Server.BaseAddress, "/v1/events"), CancellationToken.None))
        {
            await bad.SendAsync(Encoding.UTF8.GetBytes("""{"type":"auth","sessionToken":"wrong"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
            var r = await bad.ReceiveAsync(new byte[1024], CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Close, r.MessageType);
            Assert.Equal(4401, (int)bad.CloseStatus!);
        }

        using var ws = await wsClient.ConnectAsync(new Uri(factory.Server.BaseAddress, "/v1/events"), CancellationToken.None);
        await ws.SendAsync(Encoding.UTF8.GetBytes($$"""{"type":"auth","sessionToken":"{{token}}"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal("ready", (await Receive(ws)).GetProperty("type").GetString());

        await c.PostAsync("/v1/jobs", Job(new { kind = "raw" }, [1]));
        var states = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!states.Contains("completed"))
            states.Add((await Receive(ws, cts.Token)).GetProperty("job").GetProperty("state").GetString()!);
        Assert.Equal("queued", states[0]);
        Assert.Contains("submitted", states);
    }

    private static async Task<JsonElement> Receive(WebSocket ws, CancellationToken ct = default)
    {
        var buf = new byte[16 * 1024];
        var r = await ws.ReceiveAsync(buf, ct);
        return JsonDocument.Parse(buf.AsMemory(0, r.Count)).RootElement.Clone();
    }
}
