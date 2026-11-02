// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Angelito.PrintBridge.Agent;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Jobs;
using Angelito.PrintBridge.Core.Persistence;
using Angelito.PrintBridge.Core.Security;
using Angelito.PrintBridge.Windows;
using Microsoft.Extensions.Options;
using Serilog;

if (args.Length > 0 && AdminCli.Commands.Contains(args[0]))
    return await AdminCli.RunAsync(args);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var options = builder.Configuration.GetSection(BridgeOptions.Section).Get<BridgeOptions>() ?? new BridgeOptions();
builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection(BridgeOptions.Section));
Directory.CreateDirectory(options.DataDirectory);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(), Path.Combine(options.LogDirectory, "agent-.jsonl"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, fileSizeLimitBytes: 20 * 1024 * 1024));

builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenLocalhost(options.Port); // loopback only (127.0.0.1 and ::1); never exposed to the network
    k.Limits.MaxRequestBodySize = options.MaxDocumentBytes + 64 * 1024;
    k.AddServerHeader = false;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(f => f.MultipartBodyLengthLimit = options.MaxDocumentBytes);
builder.Services.ConfigureHttpJsonOptions(j => JsonDefaults.Apply(j.SerializerOptions));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKeyedSingleton("admin-token", (sp, _) => AdminToken.LoadOrCreate(sp.GetRequiredService<IOptions<BridgeOptions>>().Value.AdminTokenPath));
builder.Services.AddSingleton(sp => new BridgeStore(sp.GetRequiredService<IOptions<BridgeOptions>>().Value.DatabasePath, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new SessionService(sp.GetRequiredService<BridgeStore>(), sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<BridgeOptions>>().Value.SessionMinutes)));
builder.Services.AddSingleton(sp => new PairingService(sp.GetRequiredService<BridgeStore>(), sp.GetRequiredService<SessionService>(),
    sp.GetRequiredService<TimeProvider>(), TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<BridgeOptions>>().Value.PairingMinutes)));
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<BridgeOptions>>().Value;
    return new VirtualPrinterOptions(o.VirtualPrinterEnabled, o.VirtualOutput);
});
builder.Services.AddSingleton<IPrinterCatalog, WindowsPrinterCatalog>();
builder.Services.AddSingleton<IPrintDriver, WindowsPrintDriver>();
builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<BridgeOptions>>().Value;
    return new JobService(sp.GetRequiredService<BridgeStore>(), sp.GetRequiredService<IPrintDriver>(), o.SpoolDirectory,
        new JobLimits(o.MaxAttempts, TimeSpan.FromSeconds(o.JobTimeoutSeconds)), sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ILogger<JobService>>());
});
builder.Services.AddHostedService<QueueWorker>();
builder.Services.AddHostedService<SpoolMonitorWorker>();

builder.Services.AddRateLimiter(rl =>
{
    rl.RejectionStatusCode = 429;
    static string Key(HttpContext c) => c.Request.Headers.Origin.ToString() is { Length: > 0 } o ? o : "no-origin";
    rl.AddPolicy("api", c => RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => new FixedWindowRateLimiterOptions
    { PermitLimit = options.RequestsPerMinutePerOrigin, Window = TimeSpan.FromMinutes(1) }));
    rl.AddPolicy("pair", c => RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => new FixedWindowRateLimiterOptions
    { PermitLimit = options.PairRequestsPerMinutePerOrigin, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

_ = app.Services.GetRequiredKeyedService<string>("admin-token"); // create on first start so the CLI/UI can use it
app.Services.GetRequiredService<JobService>().Recover();

app.UseSerilogRequestLogging(); // logs method/path/status only — never bodies, headers or query strings
app.UseBridgeSecurity();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseRateLimiter();
app.MapBridgeApi();

app.Run();
return 0;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Apply(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Apply(JsonSerializerOptions o)
    {
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return o;
    }
}

internal sealed class QueueWorker(JobService jobs, ILogger<QueueWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await jobs.ProcessNextAsync(ct)) await jobs.WaitForWorkAsync(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Queue worker error");
                await Task.Delay(1000, ct).ContinueWith(_ => { });
            }
        }
    }
}

internal sealed class SpoolMonitorWorker(JobService jobs, ILogger<SpoolMonitorWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct).AsTask().ContinueWith(t => !t.IsCanceled && t.Result))
        {
            try { jobs.PollSpooler(); }
            catch (Exception ex) { log.LogError(ex, "Spool monitor error"); }
        }
    }
}

public partial class Program;
