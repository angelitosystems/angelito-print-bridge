// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Threading.Channels;
using Angelito.PrintBridge.Core.Abstractions;
using Angelito.PrintBridge.Core.Domain;
using Angelito.PrintBridge.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Angelito.PrintBridge.Core.Jobs;

public sealed record JobLimits(int MaxAttempts = 3, TimeSpan? ProcessingTimeout = null, TimeSpan? RetryBaseDelay = null)
{
    public TimeSpan Timeout => ProcessingTimeout ?? TimeSpan.FromMinutes(2);
    public TimeSpan RetryDelay => RetryBaseDelay ?? TimeSpan.FromSeconds(5);
}

/// <summary>
/// Owns the job lifecycle: accept → queue → render/spool → monitor spooler → terminal state.
/// Payloads live on disk only until the job reaches a terminal state.
/// </summary>
public sealed class JobService(
    BridgeStore store,
    IPrintDriver driver,
    string spoolDirectory,
    JobLimits limits,
    TimeProvider time,
    ILogger<JobService> log)
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public event Action<PrintJob>? JobChanged;

    public string PayloadPath(Guid id) => Path.Combine(spoolDirectory, $"{id:N}.bin");

    /// <summary>Startup recovery: fix interrupted jobs and delete payloads no live job needs.</summary>
    public void Recover()
    {
        Directory.CreateDirectory(spoolDirectory);
        var (requeued, unknown) = store.RecoverAfterRestart();
        var live = store.NonTerminalJobIds();
        foreach (var file in Directory.EnumerateFiles(spoolDirectory, "*.bin"))
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id) || !live.Contains(id))
                TryDelete(file);
        log.LogInformation("Recovery: {Requeued} requeued, {Unknown} marked Unknown", requeued, unknown);
        Wake();
    }

    public async Task<(PrintJob Job, bool Created)> EnqueueAsync(PrintJob job, Stream document, CancellationToken ct)
    {
        Directory.CreateDirectory(spoolDirectory);
        var path = PayloadPath(job.Id);
        await using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            await document.CopyToAsync(f, ct);
        var (saved, created) = store.InsertJob(job);
        if (!created) TryDelete(path);
        else
        {
            store.Audit("job.created", job.GrantId, detail: $"job={job.Id} kind={job.Kind} printer={job.PrinterId} bytes={job.SizeBytes}");
            Publish(saved);
            Wake();
        }
        return (saved, created);
    }

    public void Wake() => _wake.Writer.TryWrite(true);

    public async Task WaitForWorkAsync(TimeSpan max, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(max);
        try { await _wake.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    /// <summary>Processes one queued job. Returns false when nothing was due.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stopping)
    {
        var job = store.ClaimNext();
        if (job is null) return false;
        Publish(job);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        cts.CancelAfter(limits.Timeout);
        _running[job.Id] = cts;
        var spoolStarted = false;
        try
        {
            await driver.SubmitAsync(new SubmitRequest(job, PayloadPath(job.Id), () =>
            {
                store.MarkSpoolStarted(job.Id);
                spoolStarted = true;
            }), cts.Token);
            Finish(job.Id, [JobState.Rendering], JobState.Submitted, confirmation: Confirmation.None, keepPayload: false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Agent shutting down: Recover() decides on next start based on spool_started.
            throw;
        }
        catch (OperationCanceledException)
        {
            if (_cancelRequested.TryRemove(job.Id, out _))
                Finish(job.Id, [JobState.Rendering], JobState.Cancelled, "cancelled", "Cancelado por la aplicación.");
            else if (spoolStarted)
                Finish(job.Id, [JobState.Rendering], JobState.Unknown, ErrorCodes.Timeout, "Tiempo agotado durante el envío al spooler; no se reintenta para evitar duplicados.");
            else
                Finish(job.Id, [JobState.Rendering], JobState.Failed, ErrorCodes.Timeout, "Tiempo agotado antes de enviar al spooler.");
        }
        catch (PrintException ex) when (ex.Transient && !spoolStarted && job.Attempts < limits.MaxAttempts)
        {
            var delay = limits.RetryDelay * Math.Pow(2, job.Attempts - 1);
            var updated = store.Transition(job.Id, [JobState.Rendering], JobState.Queued, ex.Code, ex.Message, notBefore: time.GetUtcNow() + delay);
            if (updated is not null) Publish(updated);
            log.LogWarning("Job {JobId} attempt {Attempt} failed ({Code}); retrying in {Delay}", job.Id, job.Attempts, ex.Code, delay);
        }
        catch (PrintException ex)
        {
            Finish(job.Id, [JobState.Rendering], JobState.Failed, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Job {JobId} failed unexpectedly", job.Id);
            Finish(job.Id, [JobState.Rendering], spoolStarted ? JobState.Unknown : JobState.Failed, ErrorCodes.Internal, "Error interno del agente.");
        }
        finally
        {
            _running.TryRemove(job.Id, out _);
            _cancelRequested.TryRemove(job.Id, out _);
        }
        return true;
    }

    /// <summary>Checks Submitted jobs against the OS spooler and resolves the ones that left the queue.</summary>
    public void PollSpooler()
    {
        foreach (var job in store.ListJobs(null, 500, JobState.Submitted))
        {
            SpoolStatus s;
            try { s = driver.GetSpoolStatus(job); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Spooler status check failed for {JobId}", job.Id);
                continue;
            }
            if (s.Printed || !s.Found)
            {
                // Leaving the queue without a delete flag is the strongest signal Windows gives; it is not physical confirmation.
                if (s.Deleting) Finish(job.Id, [JobState.Submitted], JobState.Cancelled, "deleted_in_spooler", "Eliminado de la cola de Windows.");
                else Finish(job.Id, [JobState.Submitted], JobState.Completed, confirmation: Confirmation.Spooler);
            }
            else if (s.Status != job.SpoolerStatus)
            {
                var updated = store.SetSpoolerStatus(job.Id, s.Status);
                if (updated is not null) Publish(updated);
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, bool> _cancelRequested = new();

    public enum CancelResult { Cancelled, Requested, NotFound, AlreadyFinished, NotCancellable }

    /// <param name="grantId">Owner check; null only for the local administrator.</param>
    public CancelResult Cancel(Guid id, string? grantId)
    {
        var job = store.GetJob(id);
        if (job is null || (grantId is not null && job.GrantId != grantId)) return CancelResult.NotFound;
        if (job.IsTerminal) return CancelResult.AlreadyFinished;
        switch (job.State)
        {
            case JobState.Queued when Finish(id, [JobState.Queued], JobState.Cancelled, "cancelled", "Cancelado por la aplicación."):
                return CancelResult.Cancelled;
            case JobState.Rendering when _running.TryGetValue(id, out var cts):
                _cancelRequested[id] = true;
                cts.Cancel();
                return CancelResult.Requested;
            case JobState.Submitted when driver.TryCancelSpooled(job):
                Finish(id, [JobState.Submitted], JobState.Cancelled, "cancelled", "Eliminado de la cola de Windows; páginas ya impresas no se pueden revertir.");
                return CancelResult.Cancelled;
            default:
                return CancelResult.NotCancellable;
        }
    }

    private bool Finish(Guid id, JobState[] from, JobState to, string? code = null, string? message = null,
        Confirmation? confirmation = null, bool keepPayload = false)
    {
        var updated = store.Transition(id, from, to, code, message, confirmation);
        if (updated is null) return false;
        if (!keepPayload && to != JobState.Queued) TryDelete(PayloadPath(id));
        if (updated.IsTerminal)
            store.Audit($"job.{to.ToString().ToLowerInvariant()}", updated.GrantId, detail: $"job={id}{(code is null ? "" : $" code={code}")}");
        log.LogInformation("Job {JobId} → {State} {Code}", id, to, code);
        Publish(updated);
        return true;
    }

    private void Publish(PrintJob job)
    {
        try { JobChanged?.Invoke(job); }
        catch (Exception ex) { log.LogWarning(ex, "Job event subscriber failed"); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
