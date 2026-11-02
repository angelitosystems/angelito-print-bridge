// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Angelito.PrintBridge.Core.Security;
using System.Text.Json.Serialization;
using Angelito.PrintBridge.Core.Domain;
using Microsoft.Data.Sqlite;

namespace Angelito.PrintBridge.Core.Persistence;

/// <summary>SQLite persistence for grants, jobs and audit. One short-lived connection per call (pooled), WAL mode.</summary>
public sealed class BridgeStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public BridgeStore(string databasePath, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS grants (
              id TEXT PRIMARY KEY, app_id TEXT NOT NULL, app_name TEXT NOT NULL, origin TEXT NOT NULL,
              permissions TEXT NOT NULL, token_hash TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL, revoked_at TEXT);
            CREATE TABLE IF NOT EXISTS jobs (
              id TEXT PRIMARY KEY, grant_id TEXT NOT NULL, idempotency_key TEXT, printer_id TEXT NOT NULL,
              kind TEXT NOT NULL, options TEXT NOT NULL, priority INTEGER NOT NULL, state TEXT NOT NULL,
              attempts INTEGER NOT NULL, spool_started INTEGER NOT NULL, error_code TEXT, error_message TEXT,
              spooler_status TEXT, confirmation TEXT NOT NULL, size_bytes INTEGER NOT NULL,
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL, not_before TEXT);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_jobs_idem ON jobs(grant_id, idempotency_key) WHERE idempotency_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_jobs_state ON jobs(state, priority DESC, created_at);
            CREATE TABLE IF NOT EXISTS trusted_certs (
              fingerprint TEXT PRIMARY KEY, name TEXT NOT NULL, origin TEXT NOT NULL, permissions TEXT NOT NULL,
              cert_pem TEXT NOT NULL, not_after TEXT NOT NULL, created_at TEXT NOT NULL, revoked_at TEXT);
            CREATE TABLE IF NOT EXISTS audit (
              id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, event TEXT NOT NULL,
              grant_id TEXT, origin TEXT, detail TEXT);
            """);
        // v0.4: grants issued by certificate pairing remember the certificate.
        using var cols = Cmd(c, "SELECT COUNT(*) FROM pragma_table_info('grants') WHERE name='cert_fp'");
        if (Convert.ToInt32(cols.ExecuteScalar()) == 0) Exec(c, "ALTER TABLE grants ADD COLUMN cert_fp TEXT");
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Cmd(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private string Now() => _time.GetUtcNow().ToString("O");

    // ---------- Grants ----------

    public void InsertGrant(Grant g)
    {
        using var c = Open();
        Exec(c, "INSERT INTO grants (id,app_id,app_name,origin,permissions,token_hash,created_at,revoked_at,cert_fp) VALUES ($id,$app,$name,$origin,$perms,$hash,$created,NULL,$fp)",
            ("$id", g.Id), ("$app", g.AppId), ("$name", g.AppName), ("$origin", g.Origin),
            ("$perms", string.Join(',', g.Permissions)), ("$hash", g.TokenHash), ("$created", g.CreatedAt.ToString("O")),
            ("$fp", g.CertificateFingerprint));
    }

    public Grant? FindActiveGrantByTokenHash(string tokenHash) =>
        QueryGrants("WHERE token_hash=$h AND revoked_at IS NULL", ("$h", tokenHash)).FirstOrDefault();

    public Grant? FindGrant(string id) => QueryGrants("WHERE id=$id", ("$id", id)).FirstOrDefault();

    public IReadOnlyList<Grant> ListGrants(bool includeRevoked = false) =>
        QueryGrants(includeRevoked ? "ORDER BY created_at DESC" : "WHERE revoked_at IS NULL ORDER BY created_at DESC");

    /// <summary>Revokes active grants matching the filter. Returns the revoked ids.</summary>
    public IReadOnlyList<string> RevokeGrants(string? id = null, string? appId = null, string? origin = null, string? certificateFingerprint = null)
    {
        var active = QueryGrants("WHERE revoked_at IS NULL")
            .Where(g => (id is null || g.Id == id) && (appId is null || g.AppId == appId) && (origin is null || g.Origin == origin)
                        && (certificateFingerprint is null || g.CertificateFingerprint == certificateFingerprint))
            .Select(g => g.Id).ToList();
        using var c = Open();
        foreach (var gid in active)
            Exec(c, "UPDATE grants SET revoked_at=$now WHERE id=$id", ("$now", Now()), ("$id", gid));
        return active;
    }

    private List<Grant> QueryGrants(string where, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, $"SELECT id,app_id,app_name,origin,permissions,token_hash,created_at,revoked_at,cert_fp FROM grants {where}", args);
        using var r = cmd.ExecuteReader();
        var list = new List<Grant>();
        while (r.Read())
            list.Add(new Grant
            {
                Id = r.GetString(0),
                AppId = r.GetString(1),
                AppName = r.GetString(2),
                Origin = r.GetString(3),
                Permissions = r.GetString(4).Split(',', StringSplitOptions.RemoveEmptyEntries),
                TokenHash = r.GetString(5),
                CreatedAt = DateTimeOffset.Parse(r.GetString(6)),
                RevokedAt = r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7)),
                CertificateFingerprint = r.IsDBNull(8) ? null : r.GetString(8),
            });
        return list;
    }

    // ---------- Trusted application certificates ----------

    public void TrustCertificate(TrustedCertificate t)
    {
        using var c = Open();
        Exec(c, """
            INSERT INTO trusted_certs VALUES ($fp,$name,$origin,$perms,$pem,$na,$created,NULL)
            ON CONFLICT(fingerprint) DO UPDATE SET name=$name, origin=$origin, permissions=$perms, revoked_at=NULL
            """,
            ("$fp", t.Fingerprint), ("$name", t.Name), ("$origin", t.Origin), ("$perms", string.Join(',', t.Permissions)),
            ("$pem", t.CertificatePem), ("$na", t.NotAfter.ToString("O")), ("$created", t.CreatedAt.ToString("O")));
    }

    public TrustedCertificate? FindTrustedCertificate(string fingerprint) =>
        QueryCerts("WHERE fingerprint=$fp AND revoked_at IS NULL", ("$fp", fingerprint)).FirstOrDefault();

    public IReadOnlyList<TrustedCertificate> ListTrustedCertificates() => QueryCerts("WHERE revoked_at IS NULL ORDER BY created_at DESC");

    public bool RevokeCertificate(string fingerprint)
    {
        using var c = Open();
        using var cmd = Cmd(c, "UPDATE trusted_certs SET revoked_at=$now WHERE fingerprint=$fp AND revoked_at IS NULL", ("$now", Now()), ("$fp", fingerprint));
        return cmd.ExecuteNonQuery() == 1;
    }

    private List<TrustedCertificate> QueryCerts(string where, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = Cmd(c, $"SELECT fingerprint,name,origin,permissions,cert_pem,not_after,created_at FROM trusted_certs {where}", args);
        using var r = cmd.ExecuteReader();
        var list = new List<TrustedCertificate>();
        while (r.Read())
            list.Add(new TrustedCertificate(r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries), r.GetString(4),
                DateTimeOffset.Parse(r.GetString(5)), DateTimeOffset.Parse(r.GetString(6))));
        return list;
    }

    // ---------- Jobs ----------

    private const string JobColumns =
        "id,grant_id,idempotency_key,printer_id,kind,options,priority,state,attempts,spool_started,error_code,error_message,spooler_status,confirmation,size_bytes,created_at,updated_at,not_before";

    /// <summary>Inserts the job, or returns the existing one when (grant, idempotency key) was already used.</summary>
    public (PrintJob Job, bool Created) InsertJob(PrintJob job)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (job.IdempotencyKey is not null)
        {
            var existing = ReadJobs(c, "WHERE grant_id=$g AND idempotency_key=$k", ("$g", job.GrantId), ("$k", job.IdempotencyKey));
            if (existing.Count > 0) return (existing[0], false);
        }
        var now = Now();
        using (var cmd = Cmd(c, $"INSERT INTO jobs ({JobColumns}) VALUES ($id,$g,$k,$p,$kind,$opt,$prio,$state,0,0,NULL,NULL,NULL,$conf,$size,$now,$now,NULL)",
            ("$id", job.Id.ToString()), ("$g", job.GrantId), ("$k", job.IdempotencyKey), ("$p", job.PrinterId),
            ("$kind", job.Kind.ToString()), ("$opt", JsonSerializer.Serialize(job.Options, Json)), ("$prio", job.Priority),
            ("$state", nameof(JobState.Queued)), ("$conf", nameof(Confirmation.None)), ("$size", job.SizeBytes), ("$now", now)))
        {
            cmd.Transaction = tx;
            try { cmd.ExecuteNonQuery(); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && job.IdempotencyKey is not null)
            {
                // Same key inserted concurrently by another request: return the winner.
                tx.Rollback();
                return (ReadJobs(c, "WHERE grant_id=$g AND idempotency_key=$k", ("$g", job.GrantId), ("$k", job.IdempotencyKey))[0], false);
            }
        }
        tx.Commit();
        return (GetJob(job.Id)!, true);
    }

    public PrintJob? GetJob(Guid id)
    {
        using var c = Open();
        return ReadJobs(c, "WHERE id=$id", ("$id", id.ToString())).FirstOrDefault();
    }

    public IReadOnlyList<PrintJob> ListJobs(string? grantId, int limit, JobState? state = null)
    {
        using var c = Open();
        return ReadJobs(c,
            "WHERE ($g IS NULL OR grant_id=$g) AND ($s IS NULL OR state=$s) ORDER BY created_at DESC LIMIT $l",
            ("$g", grantId), ("$s", state?.ToString()), ("$l", Math.Clamp(limit, 1, 500)));
    }

    /// <summary>Atomically takes the highest-priority due job, moving it Queued → Rendering.</summary>
    public PrintJob? ClaimNext()
    {
        using var c = Open();
        using var cmd = Cmd(c, """
            UPDATE jobs SET state='Rendering', attempts=attempts+1, updated_at=$now
            WHERE id = (SELECT id FROM jobs WHERE state='Queued' AND (not_before IS NULL OR not_before <= $now)
                        ORDER BY priority DESC, created_at LIMIT 1)
            RETURNING id
            """, ("$now", Now()));
        var id = cmd.ExecuteScalar() as string;
        return id is null ? null : ReadJobs(c, "WHERE id=$id", ("$id", id)).FirstOrDefault();
    }

    /// <summary>
    /// Compare-and-set state transition. Returns the updated job, or null if the job was not in one of <paramref name="from"/>.
    /// </summary>
    public PrintJob? Transition(Guid id, JobState[] from, JobState to, string? errorCode = null, string? errorMessage = null,
        Confirmation? confirmation = null, DateTimeOffset? notBefore = null)
    {
        using var c = Open();
        var states = string.Join(',', from.Select(s => $"'{s}'"));
        using var cmd = Cmd(c, $"""
            UPDATE jobs SET state=$to, error_code=$ec, error_message=$em, updated_at=$now, not_before=$nb,
              confirmation=COALESCE($conf, confirmation)
            WHERE id=$id AND state IN ({states})
            """,
            ("$to", to.ToString()), ("$ec", errorCode), ("$em", errorMessage), ("$now", Now()),
            ("$nb", notBefore?.ToString("O")), ("$conf", confirmation?.ToString()), ("$id", id.ToString()));
        return cmd.ExecuteNonQuery() == 1 ? ReadJobs(c, "WHERE id=$id", ("$id", id.ToString())).FirstOrDefault() : null;
    }

    /// <summary>Persisted before any byte reaches the spooler, so a crash afterwards is never retried blindly.</summary>
    public void MarkSpoolStarted(Guid id)
    {
        using var c = Open();
        Exec(c, "UPDATE jobs SET spool_started=1, updated_at=$now WHERE id=$id", ("$now", Now()), ("$id", id.ToString()));
    }

    public PrintJob? SetSpoolerStatus(Guid id, string? status)
    {
        using var c = Open();
        Exec(c, "UPDATE jobs SET spooler_status=$s, updated_at=$now WHERE id=$id AND COALESCE(spooler_status,'') <> COALESCE($s,'')",
            ("$s", status), ("$now", Now()), ("$id", id.ToString()));
        return ReadJobs(c, "WHERE id=$id", ("$id", id.ToString())).FirstOrDefault();
    }

    /// <summary>
    /// Run at startup. Jobs interrupted before touching the spooler go back to the queue; jobs interrupted while
    /// spooling become Unknown (never reprinted automatically). Submitted jobs stay and are re-checked against the spooler.
    /// </summary>
    public (int Requeued, int Unknown) RecoverAfterRestart()
    {
        using var c = Open();
        using var a = Cmd(c, "UPDATE jobs SET state='Queued', updated_at=$now WHERE state='Rendering' AND spool_started=0", ("$now", Now()));
        var requeued = a.ExecuteNonQuery();
        using var b = Cmd(c, """
            UPDATE jobs SET state='Unknown', error_code='interrupted',
              error_message='El agente se detuvo mientras enviaba el trabajo al spooler; no se reimprime automáticamente.', updated_at=$now
            WHERE state='Rendering' AND spool_started=1
            """, ("$now", Now()));
        return (requeued, b.ExecuteNonQuery());
    }

    public IReadOnlySet<Guid> NonTerminalJobIds()
    {
        using var c = Open();
        return ReadJobs(c, "WHERE state IN ('Queued','Rendering')").Select(j => j.Id).ToHashSet();
    }

    private static List<PrintJob> ReadJobs(SqliteConnection c, string where, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, $"SELECT {JobColumns} FROM jobs {where}", args);
        using var r = cmd.ExecuteReader();
        var list = new List<PrintJob>();
        while (r.Read())
            list.Add(new PrintJob
            {
                Id = Guid.Parse(r.GetString(0)),
                GrantId = r.GetString(1),
                IdempotencyKey = r.IsDBNull(2) ? null : r.GetString(2),
                PrinterId = r.GetString(3),
                Kind = Enum.Parse<DocumentKind>(r.GetString(4)),
                Options = JsonSerializer.Deserialize<PrintOptions>(r.GetString(5), Json) ?? new(),
                Priority = r.GetInt32(6),
                State = Enum.Parse<JobState>(r.GetString(7)),
                Attempts = r.GetInt32(8),
                SpoolStarted = r.GetInt32(9) == 1,
                ErrorCode = r.IsDBNull(10) ? null : r.GetString(10),
                ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
                SpoolerStatus = r.IsDBNull(12) ? null : r.GetString(12),
                Confirmation = Enum.Parse<Confirmation>(r.GetString(13)),
                SizeBytes = r.GetInt64(14),
                CreatedAt = DateTimeOffset.Parse(r.GetString(15)),
                UpdatedAt = DateTimeOffset.Parse(r.GetString(16)),
                NotBefore = r.IsDBNull(17) ? null : DateTimeOffset.Parse(r.GetString(17)),
            });
        return list;
    }

    // ---------- Audit ----------

    /// <summary>Audit entries must never contain document content, tokens or personal data.</summary>
    public void Audit(string evt, string? grantId = null, string? origin = null, string? detail = null)
    {
        using var c = Open();
        Exec(c, "INSERT INTO audit (at,event,grant_id,origin,detail) VALUES ($at,$e,$g,$o,$d)",
            ("$at", Now()), ("$e", evt), ("$g", grantId), ("$o", origin), ("$d", detail));
    }

    public IReadOnlyList<AuditEntry> ListAudit(int limit)
    {
        using var c = Open();
        using var cmd = Cmd(c, "SELECT id,at,event,grant_id,origin,detail FROM audit ORDER BY id DESC LIMIT $l", ("$l", Math.Clamp(limit, 1, 1000)));
        using var r = cmd.ExecuteReader();
        var list = new List<AuditEntry>();
        while (r.Read())
            list.Add(new AuditEntry(r.GetInt64(0), DateTimeOffset.Parse(r.GetString(1)), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }
}
