// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Core.Domain;

namespace Angelito.PrintBridge.Core.Abstractions;

public interface IPrinterCatalog
{
    IReadOnlyList<PrinterInfo> List();
    PrinterInfo? Find(string printerId);
    string? DefaultPrinterId();
}

public sealed record SubmitRequest(PrintJob Job, string PayloadPath, Action OnSpoolStarting);

public sealed record SpoolStatus(bool Found, bool Printed, bool Deleting, string? Status);

public interface IPrintDriver
{
    /// <summary>Hands the document to the OS spooler. Returns once the spooler accepted it.</summary>
    Task SubmitAsync(SubmitRequest request, CancellationToken ct);

    SpoolStatus GetSpoolStatus(PrintJob job);

    bool TryCancelSpooled(PrintJob job);
}

public static class ErrorCodes
{
    public const string PrinterNotFound = "printer_not_found";
    public const string PrinterUnavailable = "printer_unavailable";
    public const string UnsupportedOption = "unsupported_option";
    public const string InvalidDocument = "invalid_document";
    public const string SpoolerError = "spooler_error";
    public const string Timeout = "timeout";
    public const string Internal = "internal_error";
}

public sealed class PrintException(string code, string message, bool transient = false) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>True when retrying later may succeed (e.g. printer temporarily offline).</summary>
    public bool Transient { get; } = transient;
}
