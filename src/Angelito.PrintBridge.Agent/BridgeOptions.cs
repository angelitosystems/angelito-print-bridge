// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Core.Domain;

namespace Angelito.PrintBridge.Agent;

public sealed class BridgeOptions
{
    public const string Section = "Bridge";

    /// <summary>Loopback port. The SDK probes this and the next two ports.</summary>
    public int Port { get; set; } = BridgeDefaults.Port;

    public string DataDirectory { get; set; } = BridgeDefaults.DataDirectory;

    public long MaxDocumentBytes { get; set; } = 50 * 1024 * 1024;
    public long MaxRawBytes { get; set; } = 5 * 1024 * 1024;
    public int SessionMinutes { get; set; } = 15;
    public int PairingMinutes { get; set; } = 5;
    public int JobTimeoutSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public int RequestsPerMinutePerOrigin { get; set; } = 600;
    public int PairRequestsPerMinutePerOrigin { get; set; } = 10;

    /// <summary>Exposes "virtual:file" which writes output files instead of paper. Off by default.</summary>
    public bool VirtualPrinterEnabled { get; set; }

    public string? VirtualOutputDirectory { get; set; }

    public string DatabasePath => Path.Combine(DataDirectory, "bridge.db");
    public string SpoolDirectory => Path.Combine(DataDirectory, "spool");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string AdminTokenPath => BridgeDefaults.AdminTokenPath(DataDirectory);
    public string VirtualOutput => VirtualOutputDirectory ?? Path.Combine(DataDirectory, "virtual-output");
}
