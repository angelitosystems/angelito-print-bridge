// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

namespace Angelito.PrintBridge.Core.Domain;

public enum JobState { Queued, Rendering, Submitted, Completed, Failed, Cancelled, Unknown }

public enum DocumentKind { Pdf, Image, Raw }

public enum Orientation { Portrait, Landscape }

public enum DuplexMode { Simplex, LongEdge, ShortEdge }

public enum ScaleMode { Fit, None }

/// <summary>How far the agent could confirm the outcome. The agent never claims paper came out.</summary>
public enum Confirmation { None, Spooler }

public sealed record PrintOptions
{
    public int Copies { get; init; } = 1;
    public Orientation? Orientation { get; init; }
    public string? PaperSize { get; init; }
    public DuplexMode? Duplex { get; init; }
    public ScaleMode Scale { get; init; } = ScaleMode.Fit;
}

public sealed record PrintJob
{
    public required Guid Id { get; init; }
    public required string GrantId { get; init; }
    public string? IdempotencyKey { get; init; }
    public required string PrinterId { get; init; }
    public required DocumentKind Kind { get; init; }
    public PrintOptions Options { get; init; } = new();
    public int Priority { get; init; }
    public JobState State { get; init; } = JobState.Queued;
    public int Attempts { get; init; }
    public bool SpoolStarted { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SpoolerStatus { get; init; }
    public Confirmation Confirmation { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>Name shown in the Windows queue. Uses the job id so no client data (e.g. patient names) leaks.</summary>
    public string SpoolDocumentName => $"APB-{Id:N}";

    public bool IsTerminal => State is JobState.Completed or JobState.Failed or JobState.Cancelled or JobState.Unknown;
}

public sealed record Grant
{
    public required string Id { get; init; }
    public required string AppId { get; init; }
    public required string AppName { get; init; }
    public required string Origin { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Set when the grant was issued by certificate pairing; revoking the certificate revokes it.</summary>
    public string? CertificateFingerprint { get; init; }
}

public static class Permissions
{
    public const string PrintersRead = "printers:read";
    public const string PrintDocument = "print:document";
    public const string PrintRaw = "print:raw";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { PrintersRead, PrintDocument, PrintRaw };

    public static string For(DocumentKind kind) => kind == DocumentKind.Raw ? PrintRaw : PrintDocument;
}

public sealed record PrinterCapabilities(bool Duplex, bool Color, int MaxCopies, IReadOnlyList<string> PaperSizes);

public sealed record PrinterInfo(
    string Id,
    string Name,
    bool IsDefault,
    bool IsVirtual,
    IReadOnlyList<string> Status,
    PrinterCapabilities? Capabilities);

public sealed record AuditEntry(long Id, DateTimeOffset At, string Event, string? GrantId, string? Origin, string? Detail);
