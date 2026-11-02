// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Jobs;
using Angelito.PrintBridge.Core.Persistence;
using Angelito.PrintBridge.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Angelito.PrintBridge.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apb-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

/// <summary>Driver double whose behaviour each test scripts. Records the real call sequence.</summary>
public sealed class ScriptedDriver : IPrintDriver
{
    public Func<SubmitRequest, CancellationToken, Task> Submit { get; set; } = (r, _) => { r.OnSpoolStarting(); return Task.CompletedTask; };
    public Func<PrintJob, SpoolStatus> Status { get; set; } = _ => new SpoolStatus(false, false, false, null);
    public bool CancelResult { get; set; } = true;
    public int Calls;

    public Task SubmitAsync(SubmitRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Submit(request, ct);
    }

    public SpoolStatus GetSpoolStatus(PrintJob job) => Status(job);
    public bool TryCancelSpooled(PrintJob job) => CancelResult;
}

public class OriginPolicyTests
{
    [Theory]
    [InlineData("https://app.example.com", "https://app.example.com")]
    [InlineData("https://APP.example.com:8443", "https://app.example.com:8443")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("http://127.0.0.1:3000", "http://127.0.0.1:3000")]
    public void Accepts_https_and_loopback_http(string origin, string expected) => Assert.Equal(expected, OriginPolicy.Normalize(origin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("http://app.example.com")]
    [InlineData("http://localhost.evil.com")]
    [InlineData("file:///C:/x.html")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://user:pw@app.example.com")]
    [InlineData("chrome-extension://abc")]
    [InlineData(" https://app.example.com")]
    public void Rejects_everything_else(string? origin) => Assert.Null(OriginPolicy.Normalize(origin));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("evil.com", false)]
    [InlineData("127.0.0.1.nip.io", false)]
    public void Host_check_blocks_dns_rebinding(string host, bool ok) => Assert.Equal(ok, OriginPolicy.IsAllowedHost(host));
}

public class StoreTests
{
    private static PrintJob Job(string grant = "g1", string? key = null, int priority = 0) => new()
    {
        Id = Guid.NewGuid(),
        GrantId = grant,
        IdempotencyKey = key,
        PrinterId = "P",
        Kind = DocumentKind.Raw,
        Priority = priority,
        SizeBytes = 3,
    };

    [Fact]
    public void Idempotency_key_returns_original_job_per_grant()
    {
        using var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var (a, createdA) = store.InsertJob(Job(key: "k"));
        var (b, createdB) = store.InsertJob(Job(key: "k"));
        var (c, createdC) = store.InsertJob(Job(grant: "g2", key: "k"));
        Assert.True(createdA);
        Assert.False(createdB);
        Assert.Equal(a.Id, b.Id);
        Assert.True(createdC);
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public void Claims_by_priority_then_age_and_never_twice()
    {
        using var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var low = store.InsertJob(Job(priority: 0)).Job;
        var high = store.InsertJob(Job(priority: 5)).Job;
        Assert.Equal(high.Id, store.ClaimNext()!.Id);
        var second = store.ClaimNext()!;
        Assert.Equal(low.Id, second.Id);
        Assert.Equal(JobState.Rendering, second.State);
        Assert.Equal(1, second.Attempts);
        Assert.Null(store.ClaimNext());
    }

    [Fact]
    public void Concurrent_claims_never_hand_out_the_same_job()
    {
        using var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        for (var i = 0; i < 200; i++) store.InsertJob(Job());
        var claimed = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        Parallel.For(0, 8, _ =>
        {
            while (store.ClaimNext() is { } j) claimed.Add(j.Id);
        });
        Assert.Equal(200, claimed.Count);
        Assert.Equal(200, claimed.Distinct().Count());
    }

    [Fact]
    public void Recovery_requeues_unspooled_and_marks_spooled_unknown()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "db.sqlite");
        var store = new BridgeStore(path);
        var notSpooled = store.InsertJob(Job()).Job;
        var spooled = store.InsertJob(Job()).Job;
        var submitted = store.InsertJob(Job()).Job;
        store.ClaimNext(); store.ClaimNext(); store.ClaimNext();
        store.MarkSpoolStarted(spooled.Id);
        store.MarkSpoolStarted(submitted.Id);
        store.Transition(submitted.Id, [JobState.Rendering], JobState.Submitted);

        var reopened = new BridgeStore(path); // simulates a process restart
        var (requeued, unknown) = reopened.RecoverAfterRestart();
        Assert.Equal((1, 1), (requeued, unknown));
        Assert.Equal(JobState.Queued, reopened.GetJob(notSpooled.Id)!.State);
        Assert.Equal(JobState.Unknown, reopened.GetJob(spooled.Id)!.State);
        Assert.Equal(JobState.Submitted, reopened.GetJob(submitted.Id)!.State);
    }

    [Fact]
    public void Transition_is_compare_and_set()
    {
        using var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var j = store.InsertJob(Job()).Job;
        Assert.NotNull(store.Transition(j.Id, [JobState.Queued], JobState.Cancelled));
        Assert.Null(store.Transition(j.Id, [JobState.Queued], JobState.Rendering));
        Assert.Equal(JobState.Cancelled, store.GetJob(j.Id)!.State);
    }
}

public class PairingTests
{
    private const string Origin = "https://app.example.com";
    private static readonly string Verifier = Tokens.New();

    private static (PairingService P, SessionService S, BridgeStore Store, TempDir Dir) Create()
    {
        var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var sessions = new SessionService(store, TimeProvider.System, TimeSpan.FromMinutes(15));
        return (new PairingService(store, sessions, TimeProvider.System, TimeSpan.FromMinutes(5)), sessions, store, dir);
    }

    [Fact]
    public void Token_released_once_only_to_same_origin_and_verifier()
    {
        var (p, s, _, dir) = Create();
        using var _d = dir;
        var req = p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier);
        Assert.Equal(PairStatus.Pending, p.Redeem(req.Id, Origin, Verifier)!.Value.Status);
        Assert.NotNull(p.Approve(req.Code));

        Assert.Null(p.Redeem(req.Id, "https://evil.example.com", Verifier));
        Assert.Null(p.Redeem(req.Id, Origin, Tokens.New()));
        var ok = p.Redeem(req.Id, Origin, Verifier)!.Value;
        Assert.Equal(PairStatus.Approved, ok.Status);
        Assert.Null(p.Redeem(req.Id, Origin, Verifier)); // one-time

        Assert.Null(s.Issue(ok.GrantToken!, "https://evil.example.com"));
        var session = s.Issue(ok.GrantToken!, Origin)!.Value;
        Assert.NotNull(s.Validate(session.Token, Origin));
        Assert.Null(s.Validate(session.Token, "https://evil.example.com"));
    }

    [Fact]
    public void Repairing_revokes_previous_grant_and_its_sessions()
    {
        var (p, s, store, dir) = Create();
        using var _d = dir;
        var r1 = p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier);
        p.Approve(r1.Code);
        var t1 = p.Redeem(r1.Id, Origin, Verifier)!.Value.GrantToken!;
        var s1 = s.Issue(t1, Origin)!.Value.Token;

        var r2 = p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier);
        p.Approve(r2.Code);
        Assert.Null(s.Validate(s1, Origin));
        Assert.Null(s.Issue(t1, Origin));
        Assert.Single(store.ListGrants());
    }

    [Fact]
    public void Rejects_invalid_requests_and_floods()
    {
        var (p, _, _, dir) = Create();
        using var _d = dir;
        Assert.Throws<PairingException>(() => p.Create("bad id", "POS", Origin, [Permissions.PrintersRead], Verifier));
        Assert.Throws<PairingException>(() => p.Create("com.example.pos", "POS", Origin, ["admin:all"], Verifier));
        Assert.Throws<PairingException>(() => p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], "short"));
        for (var i = 0; i < 3; i++) p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier);
        var ex = Assert.Throws<PairingException>(() => p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier));
        Assert.Equal("too_many_requests", ex.Code);
    }

    [Fact]
    public void Denied_request_never_yields_a_token()
    {
        var (p, _, store, dir) = Create();
        using var _d = dir;
        var req = p.Create("com.example.pos", "POS", Origin, [Permissions.PrintersRead], Verifier);
        Assert.True(p.Deny(req.Code));
        Assert.Null(p.Approve(req.Code));
        var r = p.Redeem(req.Id, Origin, Verifier)!.Value;
        Assert.Equal(PairStatus.Denied, r.Status);
        Assert.Null(r.GrantToken);
        Assert.Empty(store.ListGrants());
    }
}

public class JobServiceTests
{
    private static (JobService Jobs, BridgeStore Store, ScriptedDriver Driver, TempDir Dir) Create(JobLimits? limits = null)
    {
        var dir = new TempDir();
        var store = new BridgeStore(System.IO.Path.Combine(dir.Path, "db.sqlite"));
        var driver = new ScriptedDriver();
        var jobs = new JobService(store, driver, System.IO.Path.Combine(dir.Path, "spool"),
            limits ?? new JobLimits(3, TimeSpan.FromSeconds(5), TimeSpan.Zero), TimeProvider.System, NullLogger<JobService>.Instance);
        return (jobs, store, driver, dir);
    }

    private static async Task<PrintJob> Enqueue(JobService jobs, string? key = null)
    {
        var job = new PrintJob { Id = Guid.NewGuid(), GrantId = "g1", IdempotencyKey = key, PrinterId = "P", Kind = DocumentKind.Raw, SizeBytes = 3 };
        return (await jobs.EnqueueAsync(job, new MemoryStream([1, 2, 3]), CancellationToken.None)).Job;
    }

    [Fact]
    public async Task Happy_path_reaches_completed_with_spooler_confirmation_only()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        byte[]? seen = null;
        driver.Submit = (r, _) => { r.OnSpoolStarting(); seen = File.ReadAllBytes(r.PayloadPath); return Task.CompletedTask; };
        var job = await Enqueue(jobs);
        var events = new List<JobState>();
        jobs.JobChanged += j => events.Add(j.State);

        Assert.True(await jobs.ProcessNextAsync(CancellationToken.None));
        Assert.Equal([1, 2, 3], seen);
        Assert.Equal(JobState.Submitted, store.GetJob(job.Id)!.State);
        Assert.False(File.Exists(jobs.PayloadPath(job.Id))); // payload not retained

        driver.Status = _ => new SpoolStatus(true, false, false, "printing");
        jobs.PollSpooler();
        Assert.Equal("printing", store.GetJob(job.Id)!.SpoolerStatus);
        driver.Status = _ => new SpoolStatus(false, false, false, null);
        jobs.PollSpooler();
        var done = store.GetJob(job.Id)!;
        Assert.Equal(JobState.Completed, done.State);
        Assert.Equal(Confirmation.Spooler, done.Confirmation);
        Assert.Equal([JobState.Rendering, JobState.Submitted, JobState.Submitted, JobState.Completed], events);
    }

    [Fact]
    public async Task Transient_failure_before_spooling_retries_then_fails()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        driver.Submit = (_, _) => throw new PrintException(ErrorCodes.SpoolerError, "offline", transient: true);
        var job = await Enqueue(jobs);
        for (var i = 0; i < 3; i++) Assert.True(await jobs.ProcessNextAsync(CancellationToken.None));
        Assert.False(await jobs.ProcessNextAsync(CancellationToken.None));
        var final = store.GetJob(job.Id)!;
        Assert.Equal(JobState.Failed, final.State);
        Assert.Equal(3, final.Attempts);
        Assert.Equal(3, driver.Calls);
    }

    [Fact]
    public async Task Failure_after_spooling_started_is_never_retried()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        driver.Submit = (r, _) => { r.OnSpoolStarting(); throw new PrintException(ErrorCodes.SpoolerError, "write failed", transient: true); };
        var job = await Enqueue(jobs);
        await jobs.ProcessNextAsync(CancellationToken.None);
        Assert.False(await jobs.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(JobState.Failed, store.GetJob(job.Id)!.State);
        Assert.Equal(1, driver.Calls);
    }

    [Fact]
    public async Task Crash_while_spooling_becomes_unknown_after_restart_and_is_not_reprinted()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        driver.Submit = (r, _) => { r.OnSpoolStarting(); throw new InvalidProgramException("simulated crash mid-spool"); };
        var job = await Enqueue(jobs);
        // Simulate a hard crash: the job row is left in Rendering with spool_started=1.
        store.ClaimNext();
        store.MarkSpoolStarted(job.Id);
        jobs.Recover();
        Assert.Equal(JobState.Unknown, store.GetJob(job.Id)!.State);
        Assert.False(await jobs.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(0, driver.Calls);
    }

    [Fact]
    public async Task Timeout_before_spooling_fails_without_unknown()
    {
        var (jobs, store, driver, dir) = Create(new JobLimits(1, TimeSpan.FromMilliseconds(200), TimeSpan.Zero));
        using var _d = dir;
        driver.Submit = async (_, ct) => await Task.Delay(Timeout.Infinite, ct);
        var job = await Enqueue(jobs);
        await jobs.ProcessNextAsync(CancellationToken.None);
        var j = store.GetJob(job.Id)!;
        Assert.Equal(JobState.Failed, j.State);
        Assert.Equal(ErrorCodes.Timeout, j.ErrorCode);
    }

    [Fact]
    public async Task Cancel_queued_and_rendering_jobs()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        var queued = await Enqueue(jobs);
        Assert.Equal(JobService.CancelResult.NotFound, jobs.Cancel(queued.Id, "other-grant"));
        Assert.Equal(JobService.CancelResult.Cancelled, jobs.Cancel(queued.Id, "g1"));
        Assert.Equal(JobService.CancelResult.AlreadyFinished, jobs.Cancel(queued.Id, "g1"));

        var started = new TaskCompletionSource();
        driver.Submit = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); };
        var rendering = await Enqueue(jobs);
        var run = jobs.ProcessNextAsync(CancellationToken.None);
        await started.Task;
        Assert.Equal(JobService.CancelResult.Requested, jobs.Cancel(rendering.Id, "g1"));
        await run;
        Assert.Equal(JobState.Cancelled, store.GetJob(rendering.Id)!.State);
    }

    [Fact]
    public async Task Hundreds_of_jobs_are_each_processed_exactly_once()
    {
        var (jobs, store, driver, dir) = Create();
        using var _d = dir;
        await Task.WhenAll(Enumerable.Range(0, 300).Select(i => Enqueue(jobs, key: $"k{i % 250}")));
        while (await jobs.ProcessNextAsync(CancellationToken.None)) { }
        Assert.Equal(250, driver.Calls);
        Assert.All(store.ListJobs("g1", 500), j => Assert.Equal(JobState.Submitted, j.State));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(jobs.PayloadPath(Guid.Empty))!));
    }
}
